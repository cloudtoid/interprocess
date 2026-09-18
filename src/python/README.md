# Cloudtoid Interprocess for Python

[API guide](https://cloudtoid.com/docs/python/) · [Queue concepts](https://cloudtoid.com/docs/concepts/) · [Website](https://cloudtoid.com)

Fast shared-memory byte queues. Exchange messages directly with Rust, C, Go, Node.js, and .NET processes using the same v3 queue.

## [Install](https://cloudtoid.com/docs/python/#install)

Requires Python 3.9 or later.

```sh
python -m pip install cloudtoid-interprocess
```

## Example

```python
from cloudtoid_interprocess import Publisher, Subscriber

with Subscriber("example", 65536) as subscriber, Publisher("example", 65536) as publisher:
    if publisher.try_send(b"hello"):
        print(subscriber.receive(timeout=1.0))
```

## Build from source

In an activated virtual environment, run `maturin develop --release` from this directory.

Queues are transient: once all publishers and subscribers are gone, unread messages are lost. Keep at least one endpoint connected throughout a handoff between processes. See the [API guide](https://cloudtoid.com/docs/python/) for waiting, errors, ownership, and limits.
