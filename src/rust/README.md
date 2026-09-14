# Cloudtoid Interprocess for Rust

[API guide](https://cloudtoid.com/docs/rust/) · [Queue concepts](https://cloudtoid.com/docs/concepts/) · [Website](https://cloudtoid.com)

Fast shared-memory byte queues for processes on the same machine. Exchange messages with Rust, C, Python, Node.js, Go, and .NET using the open v3 protocol.

## [Install](https://cloudtoid.com/docs/rust/#install)

Run in your Cargo project. Requires Rust 1.87 or later.

```sh
cargo add cloudtoid-interprocess
```

## Example

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

Queues are transient: once all publishers and subscribers are gone, unread messages are lost. Keep at least one endpoint connected throughout a handoff between processes. See the [API guide](https://cloudtoid.com/docs/rust/) for waiting, errors, ownership, and limits.
