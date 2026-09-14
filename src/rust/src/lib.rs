//! Shared-memory byte queues compatible with Cloudtoid.Interprocess protocol v3.
//!
//! Publishers reserve concurrently. Readers serialize consumption. A paused live
//! participant retains ownership; recovery only reclaims proven-abandoned work.
//! The queue is transient: after the last endpoint closes or exits, unread
//! messages are lost. Reopening the same name creates a fresh, empty queue.

#[cfg(not(all(
    target_pointer_width = "64",
    target_endian = "little",
    target_has_atomic = "64"
)))]
compile_error!("Protocol v3 requires a little-endian 64-bit target with native 64-bit atomics");

mod platform;
mod queue;

use std::{fmt, io, path::PathBuf};

pub use queue::{Publisher, Subscriber, MAX_PUBLISHERS};

/// Queue identity and message-buffer capacity. Every participant must agree.
#[derive(Clone, Debug)]
pub struct Options {
    pub name: String,
    pub path: PathBuf,
    pub capacity: usize,
}

impl Options {
    pub fn new(name: impl Into<String>, capacity: usize) -> Self {
        Self {
            name: name.into(),
            path: std::env::temp_dir(),
            capacity,
        }
    }

    pub fn with_path(mut self, path: impl Into<PathBuf>) -> Self {
        self.path = path.into();
        self
    }

    fn validate(&self) -> Result<()> {
        if self.name.is_empty()
            || self.name.contains(['\0', '/', '\\'])
            || self.name == "."
            || self.name == ".."
        {
            return Err(Error::Invalid("queue name must be a nonempty file name"));
        }
        if self.capacity <= 16 || !self.capacity.is_multiple_of(8) {
            return Err(Error::Invalid(
                "capacity must exceed 16 bytes and be a multiple of 8",
            ));
        }
        if self
            .capacity
            .checked_add(queue::BUFFER_OFFSET)
            .is_none_or(|n| n > isize::MAX as usize)
        {
            return Err(Error::Invalid("queue mapping is too large"));
        }
        Ok(())
    }
}

#[derive(Debug)]
pub enum Error {
    Invalid(&'static str),
    CapacityMismatch,
    PublisherLimit,
    Exhausted,
    Corrupt,
    Io(io::Error),
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Invalid(message) => f.write_str(message),
            Self::CapacityMismatch => f.write_str("capacity does not match the existing queue"),
            Self::PublisherLimit => f.write_str("the queue already has 2048 connected publishers"),
            Self::Exhausted => f.write_str("queue lifetime counter exhausted; use a fresh queue"),
            Self::Corrupt => f.write_str("invalid shared-memory message header"),
            Self::Io(error) => error.fmt(f),
        }
    }
}

impl std::error::Error for Error {
    fn source(&self) -> Option<&(dyn std::error::Error + 'static)> {
        match self {
            Self::Io(error) => Some(error),
            _ => None,
        }
    }
}
impl From<io::Error> for Error {
    fn from(error: io::Error) -> Self {
        Self::Io(error)
    }
}
pub type Result<T> = std::result::Result<T, Error>;
