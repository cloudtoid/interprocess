use crate::{queue::BUFFER_OFFSET, Error, Options, Result};
#[cfg(target_os = "macos")]
use std::time::Instant;
use std::{
    ffi::CString,
    fs::{self, File, OpenOptions},
    io,
    os::fd::AsRawFd,
    path::PathBuf,
    ptr::NonNull,
    time::Duration,
};

fn lock(file: &File, operation: i32) -> io::Result<bool> {
    loop {
        if unsafe { libc::flock(file.as_raw_fd(), operation) } == 0 {
            return Ok(true);
        }
        let error = io::Error::last_os_error();
        if error.kind() == io::ErrorKind::Interrupted {
            continue;
        }
        if error.kind() == io::ErrorKind::WouldBlock && operation & libc::LOCK_NB != 0 {
            return Ok(false);
        }
        return Err(error);
    }
}

fn coordinate(directory: &PathBuf) -> io::Result<File> {
    let file = File::open(directory)?;
    lock(&file, libc::LOCK_EX)?;
    Ok(file)
}

fn lease_directory(options: &Options) -> PathBuf {
    options
        .path
        .join(".cloudtoid/interprocess/v3/readers")
        .join(&options.name)
}

fn clean_leases(options: &Options) -> io::Result<()> {
    match fs::remove_dir_all(lease_directory(options)) {
        Err(e) if e.kind() != io::ErrorKind::NotFound => Err(e),
        _ => Ok(()),
    }
}

pub(crate) struct Mapping {
    process_id: libc::pid_t,
    pub ptr: NonNull<u8>,
    length: usize,
    file: File,
    directory: PathBuf,
    pathname: PathBuf,
    options: Options,
}

// The queue protocol synchronizes every shared-memory access; the mapping stays
// alive until all endpoints and their Rust borrows have been dropped.
unsafe impl Send for Mapping {}
unsafe impl Sync for Mapping {}

impl Mapping {
    pub fn open(options: &Options) -> Result<Self> {
        let directory = options.path.join(".cloudtoid/interprocess/v3/mmf");
        fs::create_dir_all(&directory)?;
        let _coordination = coordinate(&directory)?;
        let pathname = directory.join(format!("{}.qu", options.name));
        let file = OpenOptions::new()
            .read(true)
            .write(true)
            .create(true)
            .truncate(false)
            .open(&pathname)?;
        let length = BUFFER_OFFSET + options.capacity;
        let first = lock(&file, libc::LOCK_EX | libc::LOCK_NB)?;
        if first {
            if let Err(error) = (|| -> Result<()> {
                Signal::unlink(&options.name)?;
                clean_leases(options)?;
                file.set_len(0)?;
                file.set_len(length as u64)?;
                Ok(())
            })() {
                let _ = fs::remove_file(&pathname);
                return Err(error);
            }
        } else if file.metadata()?.len() != length as u64 {
            return Err(Error::CapacityMismatch);
        }
        if let Err(error) = lock(&file, libc::LOCK_SH) {
            if first {
                let _ = fs::remove_file(&pathname);
            }
            return Err(error);
        }
        let pointer = unsafe {
            libc::mmap(
                std::ptr::null_mut(),
                length,
                libc::PROT_READ | libc::PROT_WRITE,
                libc::MAP_SHARED,
                file.as_raw_fd(),
                0,
            )
        };
        if pointer == libc::MAP_FAILED {
            let error = io::Error::last_os_error();
            if first {
                let _ = fs::remove_file(&pathname);
            }
            return Err(error.into());
        }
        let ptr = NonNull::new(pointer.cast()).expect("mmap returned address zero");
        Ok(Self {
            process_id: unsafe { libc::getpid() },
            ptr,
            length,
            file,
            directory,
            pathname,
            options: options.clone(),
        })
    }
}

impl Drop for Mapping {
    fn drop(&mut self) {
        // Inherited descriptors share the parent's flock. Only release the child's
        // mapping/descriptor; never upgrade that lock or unlink the parent's queue.
        if unsafe { libc::getpid() } != self.process_id {
            unsafe {
                libc::munmap(self.ptr.as_ptr().cast(), self.length);
            }
            return;
        }
        let coordination = coordinate(&self.directory);
        unsafe {
            libc::munmap(self.ptr.as_ptr().cast(), self.length);
        }
        if coordination.is_ok() && lock(&self.file, libc::LOCK_EX | libc::LOCK_NB).unwrap_or(false)
        {
            let _ = Signal::unlink(&self.options.name);
            let _ = clean_leases(&self.options);
            let _ = fs::remove_file(&self.pathname);
        }
        // File destruction releases the lifetime lock after cleanup completes.
    }
}

pub(crate) struct Lease {
    process_id: libc::pid_t,
    file: Option<File>,
    pathname: PathBuf,
}
impl Lease {
    pub fn is_current_process(&self) -> bool {
        unsafe { libc::getpid() == self.process_id }
    }

    pub fn new(options: &Options, id: i64) -> Result<Self> {
        let directory = lease_directory(options);
        fs::create_dir_all(&directory)?;
        let pathname = directory.join(id.to_string());
        let file = OpenOptions::new()
            .read(true)
            .write(true)
            .create(true)
            .truncate(false)
            .open(&pathname)?;
        if !lock(&file, libc::LOCK_EX | libc::LOCK_NB)? {
            return Err(Error::Corrupt);
        }
        Ok(Self {
            process_id: unsafe { libc::getpid() },
            file: Some(file),
            pathname,
        })
    }

    pub fn alive(options: &Options, id: i64) -> bool {
        let pathname = lease_directory(options).join(id.to_string());
        let result = (|| -> io::Result<bool> {
            let file = OpenOptions::new().read(true).write(true).open(&pathname)?;
            if !lock(&file, libc::LOCK_EX | libc::LOCK_NB)? {
                return Ok(true);
            }
            fs::remove_file(pathname)?;
            Ok(false)
        })();
        match result {
            Ok(alive) => alive,
            Err(e) if e.kind() == io::ErrorKind::NotFound => false,
            Err(_) => true, // An inspection failure is not proof of death.
        }
    }
}
impl Drop for Lease {
    fn drop(&mut self) {
        drop(self.file.take());
        if self.is_current_process() {
            let _ = fs::remove_file(&self.pathname);
        }
    }
}

pub(crate) struct Signal(*mut libc::sem_t);
unsafe impl Send for Signal {}
unsafe impl Sync for Signal {}
impl Signal {
    fn name(name: &str) -> Result<CString> {
        CString::new(format!("/ct3ip.{name}"))
            .map_err(|_| Error::Invalid("queue name contains NUL"))
    }

    pub fn open(name: &str) -> Result<Self> {
        let name = Self::name(name)?;
        // libc declares the real variadic ABI, including Apple's ARM64 calling convention.
        let handle = unsafe {
            libc::sem_open(
                name.as_ptr(),
                libc::O_CREAT,
                0o777 as libc::c_uint,
                0 as libc::c_uint,
            )
        };
        if handle == libc::SEM_FAILED {
            return Err(io::Error::last_os_error().into());
        }
        Ok(Self(handle))
    }

    fn unlink(name: &str) -> Result<()> {
        if unsafe { libc::sem_unlink(Self::name(name)?.as_ptr()) } == 0 {
            return Ok(());
        }
        let error = io::Error::last_os_error();
        if error.kind() == io::ErrorKind::NotFound {
            Ok(())
        } else {
            Err(error.into())
        }
    }

    pub fn post(&self) -> Result<()> {
        if unsafe { libc::sem_post(self.0) } == 0 {
            return Ok(());
        }
        let error = io::Error::last_os_error();
        if error.raw_os_error() == Some(libc::EOVERFLOW) {
            Ok(())
        } else {
            Err(error.into())
        }
    }

    #[cfg(target_os = "linux")]
    pub fn wait(&self, duration: Duration) -> Result<bool> {
        let mut until = libc::timespec {
            tv_sec: 0,
            tv_nsec: 0,
        };
        if unsafe { libc::clock_gettime(libc::CLOCK_REALTIME, &mut until) } != 0 {
            return Err(io::Error::last_os_error().into());
        }
        let nanos = until.tv_nsec as u64 + duration.subsec_nanos() as u64;
        until.tv_sec +=
            duration.as_secs() as libc::time_t + (nanos / 1_000_000_000) as libc::time_t;
        until.tv_nsec = (nanos % 1_000_000_000) as libc::c_long;
        loop {
            if unsafe { libc::sem_timedwait(self.0, &until) } == 0 {
                return Ok(true);
            }
            let error = io::Error::last_os_error();
            if error.kind() == io::ErrorKind::Interrupted {
                continue;
            }
            if error.raw_os_error() == Some(libc::ETIMEDOUT) {
                return Ok(false);
            }
            return Err(error.into());
        }
    }

    #[cfg(target_os = "macos")]
    pub fn wait(&self, duration: Duration) -> Result<bool> {
        let started = Instant::now();
        loop {
            if unsafe { libc::sem_trywait(self.0) } == 0 {
                return Ok(true);
            }
            let error = io::Error::last_os_error();
            if error.kind() != io::ErrorKind::WouldBlock
                && error.kind() != io::ErrorKind::Interrupted
            {
                return Err(error.into());
            }
            let elapsed = started.elapsed();
            if elapsed >= duration {
                return Ok(false);
            }
            std::thread::sleep((duration - elapsed).min(Duration::from_millis(1)));
        }
    }
}
impl Drop for Signal {
    fn drop(&mut self) {
        unsafe {
            libc::sem_close(self.0);
        }
    }
}
