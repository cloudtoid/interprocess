#![warn(missing_docs)]
#![doc = include_str!("../README.md")]

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
#[non_exhaustive]
pub struct Options {
    /// Queue name; use at most 24 UTF-8 bytes for portability.
    pub name: String,
    /// Shared storage directory on Unix; ignored on Windows.
    pub path: PathBuf,
    /// Message buffer bytes, excluding metadata; greater than 16 and divisible by 8.
    pub capacity: usize,
}

impl Options {
    /// Uses the operating system temporary directory for shared storage.
    pub fn new(name: impl Into<String>, capacity: usize) -> Self {
        Self {
            name: name.into(),
            path: std::env::temp_dir(),
            capacity,
        }
    }

    /// Selects a shared storage directory on Unix.
    pub fn with_path(mut self, path: impl Into<PathBuf>) -> Self {
        self.path = path.into();
        self
    }

    fn validate(&self) -> Result<()> {
        if cfg!(target_os = "macos") && self.name.len() > 24 {
            return Err(Error::Invalid(
                "queue name exceeds the macOS limit of 24 UTF-8 bytes",
            ));
        }
        if cfg!(target_os = "linux") && self.name.len() > 245 {
            return Err(Error::Invalid(
                "queue name exceeds the Linux limit of 245 UTF-8 bytes",
            ));
        }
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

/// Queue failures; full queues are retryable, while corruption requires a fresh queue.
#[derive(Debug)]
#[non_exhaustive]
pub enum Error {
    /// The queue has insufficient space, or recovery temporarily closed admission.
    Full,
    /// Invalid queue configuration or message length.
    Invalid(&'static str),
    /// An existing queue has a different capacity.
    CapacityMismatch,
    /// All publisher registrations are occupied.
    PublisherLimit,
    /// A lifetime counter cannot advance without overflowing.
    Exhausted,
    /// The shared queue contains an invalid record or registration.
    Corrupt,
    /// An operating system operation failed.
    Io(io::Error),
}

impl Error {
    /// Whether space or recovery admission is temporarily unavailable.
    pub fn is_full(&self) -> bool {
        matches!(self, Self::Full)
    }
}

impl fmt::Display for Error {
    fn fmt(&self, f: &mut fmt::Formatter<'_>) -> fmt::Result {
        match self {
            Self::Full => f.write_str("queue is full or temporarily unavailable during recovery"),
            Self::Invalid(message) => f.write_str(message),
            Self::CapacityMismatch => f.write_str("capacity does not match the existing queue"),
            Self::PublisherLimit => f.write_str("the queue already has 2048 connected publishers"),
            Self::Exhausted => f.write_str("queue lifetime counter exhausted; use a fresh queue"),
            Self::Corrupt => f.write_str("corrupt or inconsistent shared queue state"),
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
/// Result of a queue operation.
pub type Result<T> = std::result::Result<T, Error>;
