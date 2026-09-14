# Cloudtoid Interprocess for Rust

Fast shared-memory byte queues for processes on the same machine. Exchange messages with Rust, C, Python, Node.js, Go, and .NET using the open v3 protocol.

```rust
use cloudtoid_interprocess::{Options, Publisher, Subscriber};
fn main() -> Result<(), cloudtoid_interprocess::Error> {
let options = Options::new("example", 65536);
let subscriber = Subscriber::open(&options)?;
let publisher = Publisher::open(&options)?;
publisher.try_send(b"hello")?;
assert_eq!(subscriber.try_recv()?.unwrap(), b"hello");
Ok(())
}
```

Reuse receive storage with `try_recv_into`; it truncates and consumes oversized messages, matching .NET. `try_send_batch` amortizes admission across a prefix of messages. Use `recv_timeout(duration)` for a bounded wait (`None` means timeout), or `recv()` to wait indefinitely and return a message. Process crashes do not destroy queues with surviving participants.

Publishers reserve with native 64-bit atomics. Readers serialize consumption. A paused live participant retains ownership; abandoned work is recovered only after checking process liveness. Queues are volatile and messages can be discarded during crash recovery. See the [v3 protocol specification](https://github.com/cloudtoid/interprocess/blob/main/docs/protocol.md) for the complete contract.

Supports little-endian 64-bit Linux, macOS, and Windows. Use an explicit shared path on Unix if runtime temp directories differ. Every participant must use the same name and capacity. Capacity is bytes, excludes metadata, exceeds 16, and is divisible by 8.

## Queue lifetime

The queue is transient: it stays alive while at least one publisher or subscriber is connected. Once all endpoints are closed or their processes exit, unread messages are lost. Opening the same name again creates a fresh, empty queue. Keep a subscriber connected before a short-lived publisher exits; a surviving publisher also keeps the queue alive.
