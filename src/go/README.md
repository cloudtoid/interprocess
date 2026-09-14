# Cloudtoid Interprocess for Go

[API guide](https://cloudtoid.com/docs/go/) · [Queue concepts](https://cloudtoid.com/docs/concepts/) · [Website](https://cloudtoid.com)

Shared-memory byte queues backed by the same Rust core as the C, Python, and Node.js packages. Fully interoperable with .NET v3.

Install the [C SDK](https://cloudtoid.com/docs/c/), make its `pkgconfig` directory available through `PKG_CONFIG_PATH`, and, on Windows, add its DLL directory to `PATH`. On Unix, pkg-config embeds the installed library directory as a runtime search path. This package requires cgo, a C compiler, and `pkg-config`.

## [Install](https://cloudtoid.com/docs/go/#install)

Install the C SDK first (see the C guide), then run in your Go module. Requires Go 1.24+, cgo enabled, a C compiler, and pkg-config.

```sh
go get github.com/cloudtoid/interprocess/src/go/v3@latest
```

## Example

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
    if err != nil { panic(err) }
    println(string(message))
}
```

Queues are transient: once all publishers and subscribers are gone, unread messages are lost. Keep at least one endpoint connected throughout a handoff between processes. See the [API guide](https://cloudtoid.com/docs/go/) for waiting, errors, ownership, and limits.
