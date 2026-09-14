"""Exception types shared by the Python API and its native binding."""

class InterprocessError(Exception):
    """Base class for queue-specific failures."""

class CapacityMismatchError(InterprocessError, ValueError):
    """An existing queue has a different capacity."""

class PublisherLimitError(InterprocessError, RuntimeError):
    """The queue has no available publisher registration."""

class CorruptQueueError(InterprocessError, RuntimeError):
    """The shared queue contains inconsistent state."""
