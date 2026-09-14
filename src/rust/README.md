# Cloudtoid Interprocess for Rust

Fast shared-memory byte queues for processes on the same machine. Exchange messages with Rust, C, Python, Node.js, Go, and .NET using the open v3 protocol.

```rust
use cloudtoid_interprocess::{Options, Publisher, Subscriber};
let options = Options::new("example", 65536);
let subscriber = Subscriber::open(options.clone())?;
let publisher = Publisher::open(options)?;
assert!(publisher.try_send(b"hello")?);
assert_eq!(subscriber.try_receive()?.unwrap(), b"hello");
# Ok::<(), cloudtoid_interprocess::Error>(())
```

Reuse receive storage with `try_receive_into`; it truncates and consumes oversized messages, matching .NET. `try_send_batch` amortizes admission across a prefix of messages. Use `receive(Some(timeout))` to wait, or `receive(None)` for an indefinite wait. Dropping the last endpoint releases queue resources; process crashes do not destroy queues with surviving participants.

Publishers reserve with native 64-bit atomics. Readers serialize consumption. A paused live participant retains ownership; abandoned work is recovered only after checking process liveness. Queues are volatile and messages can be discarded during crash recovery. See the [v3 protocol specification](https://github.com/cloudtoid/interprocess/blob/main/docs/protocol.md) for the complete contract.

Supports little-endian 64-bit Linux, macOS, and Windows. Use an explicit shared path on Unix if runtime temp directories differ. Every participant must use the same name and capacity. Capacity is bytes, excludes metadata, exceeds 16, and is divisible by 8.
