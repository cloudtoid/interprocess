# Cloudtoid Interprocess for Go

Shared-memory byte queues backed by the same Rust core as the C, Python, and Node.js packages. Fully interoperable with .NET v3.

Install the [C SDK](../c/README.md), make its `pkgconfig` directory available through `PKG_CONFIG_PATH`, and, on Windows, add its DLL directory to `PATH`. On Unix, pkg-config embeds the installed library directory as a runtime search path. This package requires cgo, a C compiler, and `pkg-config`.

```go
import "github.com/cloudtoid/interprocess/src/go/v3"

options := interprocess.Options{Name: "example", Capacity: 65536}
subscriber, err := interprocess.OpenSubscriber(options)
if err != nil { panic(err) }
defer subscriber.Close()
publisher, err := interprocess.OpenPublisher(options)
if err != nil { panic(err) }
defer publisher.Close()
if sent, err := publisher.TrySend([]byte("hello")); err != nil {
    panic(err)
} else if sent {
    message, err := subscriber.TryReceive()
    _ = message
    _ = err
}
```

`TrySend` reports full/recovery without waiting. `TryReceive` returns nil when empty; empty messages return non-nil empty slices. `Receive(ctx)` accepts a `context.Context` and returns `ctx.Err()` on cancellation or deadline expiry. Use `context.Background()` to wait indefinitely, or `context.WithTimeout` for a deadline. Ready messages return immediately; otherwise Go timers back off through 1, 2, 4, 8, and 10 ms between nonblocking attempts, with context cancellation interrupting the timer. Each new receive checks immediately and resets the backoff. Idle-to-active delivery can incur that interval plus OS scheduling delay. `TryReceiveInto` reuses caller storage and truncates/consumes messages that do not fit.

Always close endpoints; do not copy them. Concurrent calls are supported. A per-handle read/write mutex makes Close wait for the current native call; outstanding blocking receives then return `ErrClosed`; it does not serialize publishers across processes. Native errors are copied before leaving cgo so goroutine migration cannot mix up thread-local error messages.

All participants must agree on name, capacity, and Unix path. This is volatile IPC with process crash recovery, not durable storage or broadcast. See [protocol v3](https://github.com/cloudtoid/interprocess/blob/main/docs/protocol.md).

Use `errors.Is` with `ErrCapacityMismatch`, `ErrPublisherLimit`, `ErrInvalidArgument`, `ErrExhausted`, `ErrCorrupt`, `ErrIO`, or `ErrClosed` to handle failures.

## Queue lifetime

The queue is transient: it stays alive while at least one publisher or subscriber is connected. Once all endpoints are closed or their processes exit, unread messages are lost. Opening the same name again creates a fresh, empty queue. Keep a subscriber connected before a short-lived publisher exits; a surviving publisher also keeps the queue alive.

Queue names must be nonempty and contain no slash or NUL. Windows also rejects backslashes; Unix permits them for compatibility. The maximum is 24 UTF-8 bytes on macOS and 245 on Linux; use at most 24 bytes for portable names.

Each pending `Receive` has its own timer and native checks while idle. Prefer one receive loop per subscriber and distribute work after receiving when practical. Backoff limits this idle CPU cost without holding OS threads in cgo.
