use crate::{
    platform::{Lease, Mapping, Signal},
    Error, Options, Result,
};
use std::{
    cell::UnsafeCell,
    ptr,
    sync::{
        atomic::{AtomicI32, AtomicI64, AtomicU64, Ordering::*},
        OnceLock,
    },
    time::{Duration, Instant},
};

/// Maximum number of concurrently connected publisher objects.
pub const MAX_PUBLISHERS: usize = 2048;
const SLOT_SIZE: usize = 128;
const TABLE_OFFSET: usize = 256;
pub(crate) const BUFFER_OFFSET: usize = TABLE_OFFSET + MAX_PUBLISHERS * SLOT_SIZE;
const RECOVERY_MS: u64 = 10_000;

fn now_ms() -> u64 {
    static START: OnceLock<Instant> = OnceLock::new();
    START.get_or_init(Instant::now).elapsed().as_millis() as u64
}

#[repr(C)]
struct Header {
    read: AtomicI64,
    write: AtomicI64,
    reader: AtomicI64,
    notification: AtomicI32,
    participant: AtomicI32,
}

struct Shared {
    // Drop the semaphore before releasing the mapping's participant lifetime lock.
    signal: Signal,
    mapping: Mapping,
    options: Options,
    #[cfg(test)]
    fail_notification: std::sync::atomic::AtomicBool,
}

impl Shared {
    fn open(options: Options) -> Result<Self> {
        options.validate()?;
        let mapping = Mapping::open(&options)?;
        let signal = Signal::open(&options.name)?;
        Ok(Self {
            signal,
            mapping,
            options,
            #[cfg(test)]
            fail_notification: std::sync::atomic::AtomicBool::new(false),
        })
    }

    #[inline]
    fn header(&self) -> &Header {
        unsafe { &*self.mapping.ptr.as_ptr().cast() }
    }
    #[inline]
    fn gate(&self) -> &AtomicI32 {
        unsafe { &*self.mapping.ptr.as_ptr().add(128).cast() }
    }
    #[inline]
    fn owner(&self, slot: usize) -> &AtomicI64 {
        unsafe {
            &*self
                .mapping
                .ptr
                .as_ptr()
                .add(TABLE_OFFSET + slot * SLOT_SIZE)
                .cast()
        }
    }
    #[inline]
    fn active(&self, slot: usize) -> &AtomicI32 {
        unsafe {
            &*self
                .mapping
                .ptr
                .as_ptr()
                .add(TABLE_OFFSET + slot * SLOT_SIZE + 8)
                .cast()
        }
    }

    fn register(&self) -> Result<(i64, Lease)> {
        let counter = &self.header().participant;
        let id = loop {
            let previous = counter.load(Acquire);
            let next = previous.checked_add(1).ok_or(Error::Exhausted)?;
            if counter
                .compare_exchange(previous, next, SeqCst, Acquire)
                .is_ok()
            {
                break next as i64;
            }
        };
        Ok((id, Lease::new(&self.options, id)?))
    }

    fn slot(&self, id: i64) -> Result<usize> {
        for pass in 0..2 {
            for slot in 0..MAX_PUBLISHERS {
                let owner = self.owner(slot).load(Acquire);
                if owner != 0 && (pass == 0 || Lease::alive(&self.options, owner)) {
                    continue;
                }
                if self
                    .owner(slot)
                    .compare_exchange(owner, id, SeqCst, Acquire)
                    .is_ok()
                {
                    self.active(slot).swap(0, SeqCst);
                    return Ok(slot);
                }
            }
        }
        Err(Error::PublisherLimit)
    }

    fn any_active(&self) -> bool {
        // The gate is closed before this scan. A newly registered publisher cannot
        // start writing after its slot has been inspected.
        (0..MAX_PUBLISHERS).any(|slot| {
            let owner = self.owner(slot).load(Acquire);
            owner != 0 && self.active(slot).load(Acquire) != 0 && Lease::alive(&self.options, owner)
        })
    }

    #[inline]
    fn pointer(&self, offset: i64) -> *mut u8 {
        debug_assert!(offset >= 0);
        unsafe {
            self.mapping
                .ptr
                .as_ptr()
                .add(BUFFER_OFFSET + offset as usize % self.options.capacity)
        }
    }

    #[inline]
    fn state(&self, offset: i64) -> &AtomicI32 {
        unsafe { &*self.pointer(offset).cast() }
    }

    unsafe fn write(&self, offset: i64, source: &[u8]) {
        let right = source
            .len()
            .min(self.options.capacity - offset as usize % self.options.capacity);
        ptr::copy_nonoverlapping(source.as_ptr(), self.pointer(offset), right);
        ptr::copy_nonoverlapping(
            source.as_ptr().add(right),
            self.pointer(0),
            source.len() - right,
        );
    }

    unsafe fn read(&self, offset: i64, target: &mut [u8]) {
        let right = target
            .len()
            .min(self.options.capacity - offset as usize % self.options.capacity);
        ptr::copy_nonoverlapping(self.pointer(offset), target.as_mut_ptr(), right);
        ptr::copy_nonoverlapping(
            self.pointer(0),
            target.as_mut_ptr().add(right),
            target.len() - right,
        );
    }

    unsafe fn clear(&self, offset: i64, length: usize) {
        debug_assert!(length <= self.options.capacity);
        let right = length.min(self.options.capacity - offset as usize % self.options.capacity);
        ptr::write_bytes(self.pointer(offset), 0, right);
        ptr::write_bytes(self.pointer(0), 0, length - right);
    }

    fn notify(&self) {
        if self.header().notification.swap(1, SeqCst) == 0 {
            // Notification is a hint: readers retry even if an OS post fails.
            // A wakeup failure must never hide a committed send or consumed message.
            let _ = self.post_notification();
        }
    }

    #[inline]
    fn post_notification(&self) -> Result<()> {
        #[cfg(test)]
        if self.fail_notification.load(Relaxed) {
            return Err(std::io::Error::other("injected notification failure").into());
        }
        self.signal.post()
    }

    fn empty(&self) -> bool {
        self.header().read.load(Acquire) == self.header().write.load(Acquire)
    }
}

/// A concurrently usable publisher. Dropping it releases its registration.
pub struct Publisher {
    _lease: Lease,
    shared: Shared,
    id: i64,
    slot: usize,
}

struct Active<'a>(&'a AtomicI32);
impl Drop for Active<'_> {
    fn drop(&mut self) {
        self.0.fetch_sub(1, SeqCst);
    }
}

impl Publisher {
    /// Creates or joins a queue with the supplied identity and capacity.
    ///
    /// # Errors
    /// Rejects invalid options, capacity mismatches, exhausted registrations,
    /// publisher limits (publishers only), and operating system failures.
    pub fn open(options: &Options) -> Result<Self> {
        let shared = Shared::open(options.clone())?;
        let (id, lease) = shared.register()?;
        let slot = shared.slot(id)?;
        Ok(Self {
            _lease: lease,
            shared,
            id,
            slot,
        })
    }

    /// Returns [`Error::Full`] when there is insufficient space or recovery closes admission.
    pub fn try_send(&self, message: &[u8]) -> Result<()> {
        let count = self.shared.active(self.slot);
        count.fetch_add(1, SeqCst);
        let _active = Active(count);
        if self.shared.gate().load(Acquire) != 0 {
            return Err(Error::Full);
        }
        self.send_admitted(message)
    }

    /// Publishes an ordered prefix, amortizing publisher admission across a batch.
    /// Returns the committed prefix length. A short count (including zero) means
    /// full, recovery, or a mid-batch error; retry the unsent suffix to observe a
    /// persistent error. An error before any commit is returned immediately.
    pub fn try_send_batch(&self, messages: &[&[u8]]) -> Result<usize> {
        let count = self.shared.active(self.slot);
        count.fetch_add(1, SeqCst);
        let _active = Active(count);
        if self.shared.gate().load(Acquire) != 0 {
            return Ok(0);
        }
        let mut sent = 0;
        for message in messages {
            match self.send_admitted(message) {
                Ok(()) => sent += 1,
                Err(Error::Full) => break,
                // Preserve the committed prefix even if a lifetime counter runs
                // out mid-batch. Retrying the remainder surfaces the error.
                Err(_) if sent > 0 => break,
                Err(error) => return Err(error),
            }
        }
        Ok(sent)
    }

    fn send_admitted(&self, message: &[u8]) -> Result<()> {
        if message.len() > i32::MAX as usize {
            return Err(Error::Invalid("message exceeds the protocol length limit"));
        }
        let length = (message.len() + 15) & !7;
        let Some(max_used) = self.shared.options.capacity.checked_sub(length) else {
            return Err(Error::Full);
        };
        let header = self.shared.header();
        loop {
            let read = header.read.load(Acquire);
            let write = header.write.load(Acquire);
            let Some(used) = write.checked_sub(read) else {
                return Err(Error::Corrupt);
            };
            if used < 0 || used > max_used as i64 {
                return Err(Error::Full);
            }
            let next = write.checked_add(length as i64).ok_or(Error::Exhausted)?;
            if header
                .write
                .compare_exchange(write, next, SeqCst, Acquire)
                .is_err()
            {
                continue;
            }
            // The reservation belongs exclusively to this call until readiness is released.
            unsafe {
                self.shared.write(write + 8, message);
                self.shared
                    .pointer(write)
                    .add(4)
                    .cast::<i32>()
                    .write(message.len() as i32);
            }
            self.shared.state(write).store(2, Release);
            self.shared.notify();
            return Ok(());
        }
    }
}

impl Drop for Publisher {
    fn drop(&mut self) {
        // A forked child must not release its parent's shared registration.
        if !self._lease.is_current_process() {
            return;
        }
        // Safe Rust cannot drop an endpoint while a call still borrows it.
        let _ = self
            .shared
            .owner(self.slot)
            .compare_exchange(self.id, 0, SeqCst, Acquire);
    }
}

#[derive(Clone, Copy)]
struct Pending {
    started: u64,
    read: i64,
    tail: i64,
}

/// A subscriber. Multiple subscribers compete for messages; delivery is not broadcast.
pub struct Subscriber {
    _lease: Lease,
    shared: Shared,
    id: i64,
    pending: UnsafeCell<Option<Pending>>,
    next_check: AtomicU64,
}

// `pending` is only accessed after acquiring the shared reader lock. The unique
// participant ID also prevents two calls on this same subscriber from owning it.
unsafe impl Sync for Subscriber {}

struct ReadGuard<'a> {
    owner: &'a AtomicI64,
    id: i64,
}
impl Drop for ReadGuard<'_> {
    fn drop(&mut self) {
        let _ = self.owner.compare_exchange(self.id, 0, SeqCst, Acquire);
    }
}
struct GateGuard<'a>(&'a AtomicI32);
impl Drop for GateGuard<'_> {
    fn drop(&mut self) {
        self.0.swap(0, SeqCst);
    }
}

impl Subscriber {
    /// Creates or joins a queue with the supplied identity and capacity.
    ///
    /// # Errors
    /// Rejects invalid options, capacity mismatches, exhausted registrations,
    /// publisher limits (publishers only), and operating system failures.
    pub fn open(options: &Options) -> Result<Self> {
        let shared = Shared::open(options.clone())?;
        let (id, lease) = shared.register()?;
        Ok(Self {
            _lease: lease,
            shared,
            id,
            pending: UnsafeCell::new(None),
            next_check: AtomicU64::new(now_ms() + RECOVERY_MS),
        })
    }

    /// Copies and consumes a ready message, allocating a result vector.
    pub fn try_recv(&self) -> Result<Option<Vec<u8>>> {
        self.receive_with(|shared, offset, length| {
            let mut message = vec![0; length];
            unsafe {
                shared.read(offset, &mut message);
            }
            message
        })
    }

    /// Copies into caller-owned storage. An undersized buffer truncates and consumes
    /// the message, matching the .NET v3 API. The return value is bytes copied.
    pub fn try_recv_into(&self, buffer: &mut [u8]) -> Result<Option<usize>> {
        self.receive_with(|shared, offset, length| {
            let length = length.min(buffer.len());
            unsafe {
                shared.read(offset, &mut buffer[..length]);
            }
            length
        })
    }

    /// Waits for a message indefinitely.
    pub fn recv(&self) -> Result<Vec<u8>> {
        // The unbounded path only returns on delivery or error.
        self.receive_wait(None)
            .map(|message| message.expect("unbounded wait timed out"))
    }

    /// Waits for a message, or returns None after the timeout. A zero timeout
    /// performs one attempt. Missed notifications retain the five-millisecond retry.
    pub fn recv_timeout(&self, timeout: Duration) -> Result<Option<Vec<u8>>> {
        self.receive_wait(Some(timeout))
    }

    fn receive_wait(&self, timeout: Option<Duration>) -> Result<Option<Vec<u8>>> {
        let started = Instant::now();
        let mut relay = false;
        let result = (|| loop {
            if let Some(message) = self.try_recv()? {
                return Ok(Some(message));
            }
            let wait = match timeout {
                Some(limit) => {
                    let elapsed = started.elapsed();
                    if elapsed >= limit {
                        return Ok(None);
                    }
                    (limit - elapsed).min(Duration::from_millis(5))
                }
                None => Duration::from_millis(5),
            };
            if self.shared.signal.wait(wait)? {
                self.shared.header().notification.swap(0, SeqCst);
                relay = true;
            }
        })();
        if relay && !self.shared.empty() {
            self.shared.notify();
        }
        result
    }

    fn receive_with<T>(&self, copy: impl FnOnce(&Shared, i64, usize) -> T) -> Result<Option<T>> {
        let shared = &self.shared;
        let header = shared.header();
        let owner = header.reader.load(Acquire);
        if owner != 0 {
            self.recover_reader(owner);
            return Ok(None);
        }
        // Dead-reader repair must precede the empty check: recovery can die after
        // advancing read to write but before reopening publisher admission.
        if shared.empty()
            || header
                .reader
                .compare_exchange(0, self.id, SeqCst, Acquire)
                .is_err()
        {
            return Ok(None);
        }
        let _read_guard = ReadGuard {
            owner: &header.reader,
            id: self.id,
        };
        let read = header.read.load(Acquire);
        let write = header.write.load(Acquire);
        if read == write {
            return Ok(None);
        }
        if read < 0 {
            return Err(Error::Corrupt);
        }
        let pending = unsafe { &mut *self.pending.get() };
        if shared
            .state(read)
            .compare_exchange(2, 1, SeqCst, Acquire)
            .is_err()
        {
            let now = now_ms();
            let previous = match *pending {
                Some(p) if p.read == read => p,
                _ => {
                    *pending = Some(Pending {
                        started: now,
                        read,
                        tail: header.write.load(Acquire),
                    });
                    return Ok(None);
                }
            };
            if now.saturating_sub(previous.started) >= RECOVERY_MS {
                shared.gate().swap(1, SeqCst);
                let _gate_guard = GateGuard(shared.gate());
                if shared.any_active() {
                    *pending = Some(Pending {
                        started: now,
                        ..previous
                    });
                    return Ok(None);
                }
                let length = previous.tail.checked_sub(read).ok_or(Error::Corrupt)?;
                if length < 0 || length as usize > shared.options.capacity {
                    return Err(Error::Corrupt);
                }
                unsafe {
                    shared.clear(read, length as usize);
                }
                header.read.swap(previous.tail, SeqCst);
                *pending = None;
            }
            return Ok(None);
        }
        *pending = None;
        let body = unsafe { shared.pointer(read).add(4).cast::<i32>().read() };
        if body < 0 || body as usize > shared.options.capacity - 8 {
            shared.state(read).store(2, Release);
            return Err(Error::Corrupt);
        }
        let length = (body as usize + 15) & !7;
        let Some(next) = read
            .checked_add(length as i64)
            .filter(|next| *next <= write)
        else {
            shared.state(read).store(2, Release);
            return Err(Error::Corrupt);
        };
        let result = copy(shared, read + 8, body as usize);
        unsafe {
            shared.clear(read, length);
        }
        header.read.swap(next, SeqCst);
        Ok(Some(result))
    }

    fn recover_reader(&self, owner: i64) {
        if owner == self.id {
            return;
        }
        let next = self.next_check.load(Acquire);
        let now = now_ms();
        if now < next
            || self
                .next_check
                .compare_exchange(next, now + RECOVERY_MS, SeqCst, Acquire)
                .is_err()
        {
            return;
        }
        let header = self.shared.header();
        if !Lease::alive(&self.shared.options, owner)
            && header
                .reader
                .compare_exchange(owner, self.id, SeqCst, Acquire)
                .is_ok()
        {
            let _guard = ReadGuard {
                owner: &header.reader,
                id: self.id,
            };
            self.shared.gate().swap(0, SeqCst);
        }
    }
}

impl std::fmt::Debug for Publisher {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Publisher")
            .field("options", &self.shared.options)
            .field("id", &self.id)
            .finish_non_exhaustive()
    }
}
impl std::fmt::Debug for Subscriber {
    fn fmt(&self, f: &mut std::fmt::Formatter<'_>) -> std::fmt::Result {
        f.debug_struct("Subscriber")
            .field("options", &self.shared.options)
            .field("id", &self.id)
            .finish_non_exhaustive()
    }
}

#[cfg(test)]
mod tests;
