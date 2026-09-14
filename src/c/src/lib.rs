//! C ABI. Handles and buffers must satisfy the ownership rules in interprocess.h.
use core_queue::{Options, Publisher, Subscriber};
use std::{
    cell::RefCell,
    ffi::{c_char, CStr, CString},
    panic::{catch_unwind, AssertUnwindSafe},
    ptr, slice,
    time::Duration,
};

thread_local! { static ERROR: RefCell<CString> = RefCell::new(CString::new("").unwrap()); }
fn fail(message: impl std::fmt::Display) -> i32 {
    ERROR.with(|slot| {
        *slot.borrow_mut() = CString::new(message.to_string().replace('\0', " ")).unwrap()
    });
    -1
}
fn call(work: impl FnOnce() -> Result<i32, String>) -> i32 {
    match catch_unwind(AssertUnwindSafe(work)) {
        Ok(Ok(status)) => status,
        Ok(Err(error)) => fail(error),
        Err(_) => fail("native queue panicked"),
    }
}
unsafe fn text<'a>(value: *const c_char) -> Result<&'a str, String> {
    if value.is_null() {
        return Err("null string".into());
    }
    CStr::from_ptr(value).to_str().map_err(|e| e.to_string())
}
unsafe fn options(
    name: *const c_char,
    path: *const c_char,
    capacity: usize,
) -> Result<Options, String> {
    let options = Options::new(text(name)?, capacity);
    Ok(if path.is_null() {
        options
    } else {
        options.with_path(text(path)?)
    })
}
unsafe fn bytes<'a>(data: *const u8, length: usize) -> Result<&'a [u8], String> {
    if length == 0 {
        return Ok(&[]);
    }
    if data.is_null() || length > isize::MAX as usize {
        return Err("invalid buffer".into());
    }
    Ok(slice::from_raw_parts(data, length))
}

#[no_mangle]
pub extern "C" fn cip_last_error() -> *const c_char {
    ERROR.with(|e| e.borrow().as_ptr())
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
            return Err("null output".into());
        }
        *output = ptr::null_mut();
        let publisher =
            Publisher::open(options(name, path, capacity)?).map_err(|e| e.to_string())?;
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
            return Err("null output".into());
        }
        *output = ptr::null_mut();
        let subscriber =
            Subscriber::open(options(name, path, capacity)?).map_err(|e| e.to_string())?;
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
            .ok_or("null publisher")?
            .try_send(bytes(data, length)?)
            .map(i32::from)
            .map_err(|e| e.to_string())
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
        let output = output.as_mut().ok_or("null output")?;
        *output = Buffer {
            data: ptr::null_mut(),
            length: 0,
        };
        if timeout_ms < -1 {
            return Err("timeout must be -1 or nonnegative".into());
        }
        let timeout = (timeout_ms >= 0).then(|| Duration::from_millis(timeout_ms as u64));
        match handle
            .as_ref()
            .ok_or("null subscriber")?
            .receive(timeout)
            .map_err(|e| e.to_string())?
        {
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
            return Err("null copied output".into());
        }
        *copied = 0;
        let buffer = if capacity == 0 {
            &mut []
        } else {
            if data.is_null() || capacity > isize::MAX as usize {
                return Err("invalid buffer".into());
            }
            slice::from_raw_parts_mut(data, capacity)
        };
        match handle
            .as_ref()
            .ok_or("null subscriber")?
            .try_receive_into(buffer)
            .map_err(|e| e.to_string())?
        {
            Some(length) => {
                *copied = length;
                Ok(1)
            }
            None => Ok(0),
        }
    })
}
