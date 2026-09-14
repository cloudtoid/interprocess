# Cloudtoid Interprocess for Python

Fast shared-memory byte queues. Exchange messages directly with Rust, C, Go, Node.js, and .NET processes using the same v3 queue.

```python
from cloudtoid_interprocess import Publisher, Subscriber

with Subscriber("example", 65536) as subscriber, Publisher("example", 65536) as publisher:
    if publisher.try_send(b"hello"):
        print(subscriber.receive(timeout=1.0))
```

`try_send` returns false when full or recovering. `try_send_batch` returns the committed prefix length. `try_receive` returns bytes or `None`; empty bytes are a message. Use context managers or `close()` to release registrations promptly.

`try_send` and `try_send_batch` accept buffer objects such as `bytes`, `bytearray`, and `memoryview`. `bytes` uses the direct path; other buffers are copied into a snapshot before sending. `path` also accepts `os.PathLike` objects.

The prebuilt wheel supports standard CPython 3.9 and later. Free-threaded Python is not covered by the ABI3 wheel and requires a source build. Type hints are included.

Build from this repository with `maturin develop --release` in this directory. See [protocol v3](https://github.com/cloudtoid/interprocess/blob/main/docs/protocol.md) for layout, memory ordering, resource lifetime, and crash recovery.

## Errors

Queue-specific exceptions inherit from `InterprocessError`, so callers can catch them together. Invalid arguments and closed endpoints raise `ValueError`; capacity mismatches raise `CapacityMismatchError`, publisher limits raise `PublisherLimitError`, corrupt records raise `CorruptQueueError`, and OS failures raise `OSError`.

## Waiting and cancellation

`receive(timeout=None)` waits indefinitely, releases the GIL while waiting, and checks Python signals periodically. Timeouts are seconds.

Closing a publisher during a Python buffer-export callback is safe; the interrupted send raises `ValueError`. Closing a subscriber from another thread or a signal handler interrupts an idle receive within its 100 ms wait slice. For asyncio, use `await asyncio.to_thread(subscriber.receive, timeout=...)` with a finite timeout: cancelling an asyncio task does not stop its worker thread. Open endpoints after `fork()` and do not use inherited endpoints in the child; closing inherited endpoints does not release the parent's registrations.

## Limits

Every participant must agree on name, capacity, and Unix path. Pass `path="/shared/directory"` when different runtimes have different temp directories. Capacity is bytes, excludes metadata, exceeds 16, and is divisible by 8.

Queue names must be nonempty and contain no slash or NUL. Windows also rejects backslashes; Unix permits them for compatibility. The maximum is 24 UTF-8 bytes on macOS and 245 on Linux; use at most 24 bytes for portable names.

Batch sends return the committed prefix length. A short count, including zero, can mean a full queue, recovery, or a mid-batch error. Retry the unsent suffix to observe a persistent error; errors before any commit are raised immediately.

## Queue lifetime

The queue is transient: it stays alive while at least one publisher or subscriber is connected. Once all endpoints are closed or their processes exit, unread messages are lost. Opening the same name again creates a fresh, empty queue. Keep a subscriber connected before a short-lived publisher exits; a surviving publisher also keeps the queue alive.

Subscribers compete for messages. Recovery after a process crash can discard queued messages; this is transient IPC, not broadcast or durable storage.
