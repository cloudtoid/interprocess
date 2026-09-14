//! C ABI. Handles and buffers must satisfy the ownership rules in interprocess.h.
use core_queue::{Error, Options, Publisher, Subscriber};
use std::{
    cell::RefCell,
    ffi::{c_char, CStr, CString},
    panic::{catch_unwind, AssertUnwindSafe},
    ptr, slice,
    time::Duration,
};

thread_local! { static ERROR: RefCell<(i32, CString)> = RefCell::new((0, CString::new("").unwrap())); }
fn fail(kind: i32, message: impl std::fmt::Display) -> i32 {
    ERROR.with(|slot| {
        *slot.borrow_mut() = (
            kind,
            CString::new(message.to_string().replace('\0', " ")).unwrap(),
        )
    });
    -1
}
fn call(work: impl FnOnce() -> Result<i32, Error>) -> i32 {
    match catch_unwind(AssertUnwindSafe(work)) {
        Ok(Ok(status)) => status,
        Ok(Err(error)) => {
            let kind = match error {
                Error::Invalid(_) => 1,
                Error::CapacityMismatch => 2,
                Error::PublisherLimit => 3,
                Error::Exhausted => 4,
                Error::Corrupt => 5,
                Error::Io(_) => 6,
            };
            fail(kind, error)
        }
        Err(_) => fail(7, "native queue panicked"),
    }
}
unsafe fn text<'a>(value: *const c_char) -> Result<&'a str, Error> {
    if value.is_null() {
        return Err(Error::Invalid("null string"));
    }
    CStr::from_ptr(value)
        .to_str()
        .map_err(|_| Error::Invalid("string must be UTF-8"))
}
unsafe fn options(
    name: *const c_char,
    path: *const c_char,
    capacity: usize,
) -> Result<Options, Error> {
    let options = Options::new(text(name)?, capacity);
    Ok(if path.is_null() {
        options
    } else {
        options.with_path(text(path)?)
    })
}
unsafe fn bytes<'a>(data: *const u8, length: usize) -> Result<&'a [u8], Error> {
    if length == 0 {
        return Ok(&[]);
    }
    if data.is_null() || length > isize::MAX as usize {
        return Err(Error::Invalid("invalid buffer"));
    }
    Ok(slice::from_raw_parts(data, length))
}

/// Machine-readable kind of the last error on the calling thread (see the C header).
#[no_mangle]
pub extern "C" fn cip_last_error_kind() -> i32 {
    ERROR.with(|e| e.borrow().0)
}

#[no_mangle]
pub extern "C" fn cip_last_error() -> *const c_char {
    ERROR.with(|e| e.borrow().1.as_ptr())
}

/// # Safety
/// name/path must be readable NUL-terminated strings and output must be writable.
/// A successful handle must be closed exactly once after its calls finish.
#[no_mangle]
pub unsafe extern "C" fn cip_publisher_open(
    name: *const c_char,
    path: *const c_char,
    capacity: usize,
    output: *mut *mut Publisher,
) -> i32 {
    call(|| {
        if output.is_null() {
            return Err(Error::Invalid("null output"));
        }
        *output = ptr::null_mut();
        let publisher = Publisher::open(&options(name, path, capacity)?)?;
        *output = Box::into_raw(Box::new(publisher));
        Ok(1)
    })
}
/// # Safety
/// name/path must be readable NUL-terminated strings and output must be writable.
/// A successful handle must be closed exactly once after its calls finish.
#[no_mangle]
pub unsafe extern "C" fn cip_subscriber_open(
    name: *const c_char,
    path: *const c_char,
    capacity: usize,
    output: *mut *mut Subscriber,
) -> i32 {
    call(|| {
        if output.is_null() {
            return Err(Error::Invalid("null output"));
        }
        *output = ptr::null_mut();
        let subscriber = Subscriber::open(&options(name, path, capacity)?)?;
        *output = Box::into_raw(Box::new(subscriber));
        Ok(1)
    })
}
/// # Safety
/// A nonnull handle must be an unclosed publisher returned by cip_publisher_open.
/// All calls must have finished and no subsequent call may use the handle.
#[no_mangle]
pub unsafe extern "C" fn cip_publisher_close(handle: *mut Publisher) {
    if !handle.is_null() {
        drop(Box::from_raw(handle));
    }
}
/// # Safety
/// A nonnull handle must be an unclosed subscriber returned by cip_subscriber_open.
/// All calls must have finished and no subsequent call may use the handle.
#[no_mangle]
pub unsafe extern "C" fn cip_subscriber_close(handle: *mut Subscriber) {
    if !handle.is_null() {
        drop(Box::from_raw(handle));
    }
}
/// # Safety
/// The handle must remain live and data must be readable and unchanged for length
/// bytes throughout this call. A null data pointer is allowed only for length zero.
#[no_mangle]
pub unsafe extern "C" fn cip_try_send(
    handle: *const Publisher,
    data: *const u8,
    length: usize,
) -> i32 {
    call(|| {
        handle
            .as_ref()
            .ok_or(Error::Invalid("null publisher"))?
            .try_send(bytes(data, length)?)
            .map(i32::from)
    })
}

/// C owns a returned buffer until cip_buffer_free is called exactly once.
#[repr(C)]
pub struct Buffer {
    pub data: *mut u8,
    pub length: usize,
}
/// # Safety
/// The handle must remain live. output must be writable and must not alias queue
/// memory. Free a successful output exactly once with cip_buffer_free.
#[no_mangle]
pub unsafe extern "C" fn cip_receive(
    handle: *const Subscriber,
    timeout_ms: i64,
    output: *mut Buffer,
) -> i32 {
    call(|| {
        let output = output.as_mut().ok_or(Error::Invalid("null output"))?;
        *output = Buffer {
            data: ptr::null_mut(),
            length: 0,
        };
        if timeout_ms < -1 {
            return Err(Error::Invalid("timeout must be -1 or nonnegative"));
        }
        let subscriber = handle.as_ref().ok_or(Error::Invalid("null subscriber"))?;
        let message = if timeout_ms == -1 {
            subscriber.recv().map(Some)
        } else {
            subscriber.recv_timeout(Duration::from_millis(timeout_ms as u64))
        };
        match message? {
            Some(message) => {
                let message = message.into_boxed_slice();
                output.length = message.len();
                output.data = Box::into_raw(message).cast();
                Ok(1)
            }
            None => Ok(0),
        }
    })
}
/// # Safety
/// A nonnull buffer must be an unchanged, not-yet-freed successful cip_receive
/// result. No access to its bytes may occur after this call.
#[no_mangle]
pub unsafe extern "C" fn cip_buffer_free(buffer: Buffer) {
    if !buffer.data.is_null() {
        drop(Box::from_raw(ptr::slice_from_raw_parts_mut(
            buffer.data,
            buffer.length,
        )));
    }
}
/// # Safety
/// The handle must remain live. data must be exclusively writable for capacity
/// bytes, and copied must be writable and must not overlap data or the handle.
#[no_mangle]
pub unsafe extern "C" fn cip_try_receive_into(
    handle: *const Subscriber,
    data: *mut u8,
    capacity: usize,
    copied: *mut usize,
) -> i32 {
    call(|| {
        if copied.is_null() {
            return Err(Error::Invalid("null copied output"));
        }
        *copied = 0;
        let buffer = if capacity == 0 {
            &mut []
        } else {
            if data.is_null() || capacity > isize::MAX as usize {
                return Err(Error::Invalid("invalid buffer"));
            }
            slice::from_raw_parts_mut(data, capacity)
        };
        match handle
            .as_ref()
            .ok_or(Error::Invalid("null subscriber"))?
            .try_recv_into(buffer)?
        {
            Some(length) => {
                *copied = length;
                Ok(1)
            }
            None => Ok(0),
        }
    })
}
