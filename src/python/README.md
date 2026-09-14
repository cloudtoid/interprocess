# Cloudtoid Interprocess for Python

Fast shared-memory byte queues. Exchange messages directly with Rust, C, Go, Node.js, and .NET processes using the same v3 queue.

```python
from cloudtoid_interprocess import Publisher, Subscriber

with Subscriber("example", 65536) as subscriber, Publisher("example", 65536) as publisher:
    if publisher.try_send(b"hello"):
        print(subscriber.receive(timeout=1.0))
```

`try_send` returns false when full or recovering. `try_send_batch` accepts a list of bytes and returns the number sent from its prefix. `try_receive` returns bytes or `None`. Empty bytes are a message. `receive(timeout=None)` waits indefinitely, releases the GIL while waiting, and checks Python signals periodically. Timeouts are seconds.

Use context managers or `close()` to release registrations promptly. Finish calls before closing an endpoint; an overlapping close is rejected by Python's native borrow guard. Messages are bytes; serialization is your application's choice. A subscriber consumes each message exclusively; this is a competing-consumer queue, not broadcast or durable storage.

Every participant must agree on name, capacity, and Unix path. Pass `path="/shared/directory"` when different runtimes have different temp directories. Capacity is bytes, excludes metadata, exceeds 16, and is divisible by 8.

Build from this repository with `maturin develop --release` in this directory. See [protocol v3](https://github.com/cloudtoid/interprocess/blob/main/docs/protocol.md) for layout, memory ordering, resource lifetime, and crash recovery.
