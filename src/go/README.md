# Cloudtoid Interprocess for Go

Shared-memory byte queues backed by the same Rust core as the C, Python, and Node.js packages. Fully interoperable with .NET v3.

Install the [C SDK](../c/README.md), make its `pkgconfig` directory available through `PKG_CONFIG_PATH`, and include its library directory in the OS loader search path. This package requires cgo, a C compiler, and `pkg-config`.

```go
import queue "github.com/cloudtoid/interprocess/src/go/v3"

options := queue.Options{Name: "example", Capacity: 65536}
subscriber, err := queue.OpenSubscriber(options)
if err != nil { panic(err) }
defer subscriber.Close()
publisher, err := queue.OpenPublisher(options)
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

`TrySend` reports full/recovery without waiting. `TryReceive` returns nil when empty; empty messages return non-nil empty slices. `Receive(timeout)` takes a finite `time.Duration`. `TryReceiveInto` reuses caller storage and truncates/consumes messages that do not fit.

Always close endpoints; do not copy them. Concurrent calls are supported. A per-handle read/write mutex makes Close wait for outstanding calls; it does not serialize publishers across processes. Native errors are copied before leaving cgo so goroutine migration cannot mix up thread-local error messages.

All participants must agree on name, capacity, and Unix path. This is volatile IPC with process crash recovery, not durable storage or broadcast. See [protocol v3](https://github.com/cloudtoid/interprocess/blob/main/docs/protocol.md).
