use crate::{queue::BUFFER_OFFSET, Error, Options, Result};
use std::{
    io,
    ptr::{null, null_mut, NonNull},
    time::Duration,
};
use windows_sys::Win32::{
    Foundation::*,
    System::{Memory::*, Threading::*},
};

fn wide(name: &str) -> Vec<u16> {
    name.encode_utf16().chain(Some(0)).collect()
}
struct Handle(HANDLE);
unsafe impl Send for Handle {}
unsafe impl Sync for Handle {}
impl Handle {
    fn new(handle: HANDLE) -> io::Result<Self> {
        if handle.is_null() {
            Err(io::Error::last_os_error())
        } else {
            Ok(Self(handle))
        }
    }
}
impl Drop for Handle {
    fn drop(&mut self) {
        unsafe {
            CloseHandle(self.0);
        }
    }
}

struct View(NonNull<u8>);
impl View {
    fn new(handle: &Handle, length: usize, access: FILE_MAP) -> io::Result<Self> {
        let view = unsafe { MapViewOfFile(handle.0, access, 0, 0, length) };
        NonNull::new(view.Value.cast())
            .map(Self)
            .ok_or_else(io::Error::last_os_error)
    }
}
impl Drop for View {
    fn drop(&mut self) {
        unsafe {
            UnmapViewOfFile(MEMORY_MAPPED_VIEW_ADDRESS {
                Value: self.0.as_ptr().cast(),
            });
        }
    }
}

struct Coordination(Handle);
impl Coordination {
    fn new(name: &str) -> io::Result<Self> {
        let mutex = Handle::new(unsafe {
            CreateMutexW(null(), 0, wide(&format!("CT3_INIT_{name}")).as_ptr())
        })?;
        match unsafe { WaitForSingleObject(mutex.0, INFINITE) } {
            WAIT_OBJECT_0 | WAIT_ABANDONED => Ok(Self(mutex)),
            _ => Err(io::Error::last_os_error()),
        }
    }
}
impl Drop for Coordination {
    fn drop(&mut self) {
        unsafe {
            ReleaseMutex(self.0 .0);
        }
    }
}

fn create_mapping(name: &str, length: usize) -> io::Result<Handle> {
    Handle::new(unsafe {
        CreateFileMappingW(
            INVALID_HANDLE_VALUE,
            null(),
            PAGE_READWRITE,
            ((length as u64) >> 32) as u32,
            length as u32,
            wide(name).as_ptr(),
        )
    })
}

pub(crate) struct Mapping {
    pub ptr: NonNull<u8>,
    _view: View,
    _handle: Handle,
}
unsafe impl Send for Mapping {}
unsafe impl Sync for Mapping {}
impl Mapping {
    pub fn open(options: &Options) -> Result<Self> {
        let _coordination = Coordination::new(&options.name)?;
        let length = BUFFER_OFFSET + options.capacity;
        let handle = create_mapping(&format!("CT3_IP_{}", options.name), length)?;
        {
            let header = View::new(&handle, 40, FILE_MAP_ALL_ACCESS)?;
            let capacity = unsafe { header.0.as_ptr().add(32).cast::<i64>() };
            let existing = unsafe { capacity.read() };
            if existing == 0 {
                unsafe {
                    capacity.write(options.capacity as i64);
                }
            } else if existing != options.capacity as i64 {
                return Err(Error::CapacityMismatch);
            }
        }
        let view = View::new(&handle, length, FILE_MAP_ALL_ACCESS)?;
        Ok(Self {
            ptr: view.0,
            _view: view,
            _handle: handle,
        })
    }
}

fn started(process: HANDLE) -> io::Result<i64> {
    let mut creation = FILETIME::default();
    let mut exit = FILETIME::default();
    let mut kernel = FILETIME::default();
    let mut user = FILETIME::default();
    if unsafe { GetProcessTimes(process, &mut creation, &mut exit, &mut kernel, &mut user) } == 0 {
        return Err(io::Error::last_os_error());
    }
    // FILETIME starts in 1601; .NET DateTime UTC ticks start in year 1.
    Ok(
        (((creation.dwHighDateTime as u64) << 32) | creation.dwLowDateTime as u64) as i64
            + 504_911_232_000_000_000,
    )
}
fn lease_name(options: &Options, id: i64) -> String {
    format!("CT3_READER_{}.{id}", options.name)
}

pub(crate) struct Lease {
    _handle: Handle,
}
impl Lease {
    pub fn new(options: &Options, id: i64) -> Result<Self> {
        let handle = create_mapping(&lease_name(options, id), 16)?;
        if unsafe { GetLastError() } == ERROR_ALREADY_EXISTS {
            return Err(Error::Invalid("participant registration is already in use"));
        }
        let view = View::new(&handle, 16, FILE_MAP_ALL_ACCESS)?;
        let ticks = started(unsafe { GetCurrentProcess() })?;
        unsafe {
            view.0.as_ptr().cast::<u32>().write(GetCurrentProcessId());
            view.0.as_ptr().add(8).cast::<i64>().write(ticks);
        }
        Ok(Self { _handle: handle })
    }
    pub fn alive(options: &Options, id: i64) -> bool {
        let result = (|| -> io::Result<bool> {
            let handle = Handle::new(unsafe {
                OpenFileMappingW(FILE_MAP_READ, 0, wide(&lease_name(options, id)).as_ptr())
            })?;
            let view = View::new(&handle, 16, FILE_MAP_READ)?;
            let pid = unsafe { view.0.as_ptr().cast::<u32>().read() };
            let expected = unsafe { view.0.as_ptr().add(8).cast::<i64>().read() };
            let process = match Handle::new(unsafe {
                OpenProcess(
                    PROCESS_QUERY_LIMITED_INFORMATION | PROCESS_SYNCHRONIZE,
                    0,
                    pid,
                )
            }) {
                Err(e) if e.raw_os_error() == Some(ERROR_INVALID_PARAMETER as i32) => {
                    return Ok(false)
                }
                other => other?,
            };
            match unsafe { WaitForSingleObject(process.0, 0) } {
                WAIT_OBJECT_0 => Ok(false),
                WAIT_TIMEOUT => Ok(started(process.0)? == expected),
                _ => Err(io::Error::last_os_error()),
            }
        })();
        match result {
            Ok(alive) => alive,
            Err(e) if e.kind() == io::ErrorKind::NotFound => false,
            Err(_) => true,
        }
    }
}

pub(crate) struct Signal(Handle);
impl Signal {
    pub fn open(name: &str) -> Result<Self> {
        Ok(Self(Handle::new(unsafe {
            CreateSemaphoreW(
                null(),
                0,
                i32::MAX,
                wide(&format!("Global\\CT3.IP.{name}")).as_ptr(),
            )
        })?))
    }
    pub fn post(&self) -> Result<()> {
        if unsafe { ReleaseSemaphore(self.0 .0, 1, null_mut()) } != 0 {
            return Ok(());
        }
        let error = io::Error::last_os_error();
        if error.raw_os_error() == Some(ERROR_TOO_MANY_POSTS as i32) {
            Ok(())
        } else {
            Err(error.into())
        }
    }
    pub fn wait(&self, duration: Duration) -> Result<bool> {
        let millis = duration
            .as_nanos()
            .div_ceil(1_000_000)
            .min((u32::MAX - 1) as u128) as u32;
        match unsafe { WaitForSingleObject(self.0 .0, millis) } {
            WAIT_OBJECT_0 => Ok(true),
            WAIT_TIMEOUT => Ok(false),
            _ => Err(io::Error::last_os_error().into()),
        }
    }
}
