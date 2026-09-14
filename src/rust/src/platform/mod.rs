#[cfg(unix)]
mod unix;
#[cfg(unix)]
pub(crate) use unix::*;

#[cfg(windows)]
mod windows;
#[cfg(windows)]
pub(crate) use windows::*;

#[cfg(not(any(windows, target_os = "linux", target_os = "macos")))]
compile_error!("Supported platforms are Windows, Linux, and macOS");
