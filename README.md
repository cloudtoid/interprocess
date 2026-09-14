[<img src="https://raw.githubusercontent.com/cloudtoid/assets/master/logos/cloudtoid-blue.svg" width="100px">][Cloudtoid]

# Interprocess

[Website](https://cloudtoid.com) · [Languages and packages](#languages) · [Quick start](#quick-start) · [Performance](#performance) · [Protocol v3](docs/protocol.md)

[![NuGet](https://img.shields.io/nuget/v/Cloudtoid.Interprocess?label=NuGet)](https://www.nuget.org/packages/Cloudtoid.Interprocess)
[![Rust](https://img.shields.io/crates/v/cloudtoid-interprocess?label=Rust)](https://crates.io/crates/cloudtoid-interprocess)
[![C FFI](https://img.shields.io/crates/v/cloudtoid-interprocess-ffi?label=C%20FFI)](https://crates.io/crates/cloudtoid-interprocess-ffi)
[![npm](https://img.shields.io/npm/v/@cloudtoid/interprocess?label=npm)](https://www.npmjs.com/package/@cloudtoid/interprocess)
[![Go](https://img.shields.io/github/v/tag/cloudtoid/interprocess?filter=src%2Fgo%2Fv*&label=Go&color=blue)](https://pkg.go.dev/github.com/cloudtoid/interprocess/src/go/v3)
[![C SDK](https://img.shields.io/github/v/release/cloudtoid/interprocess?filter=native-v*&label=C%20SDK)](https://github.com/cloudtoid/interprocess/releases/latest)

[![Native core](https://github.com/cloudtoid/interprocess/actions/workflows/native.yml/badge.svg)](https://github.com/cloudtoid/interprocess/actions/workflows/native.yml)
[![Interoperability](https://github.com/cloudtoid/interprocess/actions/workflows/interop.yml/badge.svg)](https://github.com/cloudtoid/interprocess/actions/workflows/interop.yml)
[![License: MIT][LicenseBadge]][License]

**Fast, lightweight queues across processes and languages.** Cloudtoid Interprocess connects **Rust, C/C++, Python, Node.js, Go, and .NET** processes through a shared circular buffer on the same machine. Multiple publishers send bytes; competing subscribers receive them. No broker service or network hop is required.

- **Fast:** Measured Rust send + receive in **12.98 ns** for a 3-byte message on Apple M5 Max. See the [in-process benchmarks](#performance).
- **Low overhead:** Bounded shared memory, reusable receive buffers, and coalesced notifications. The measured .NET path with a reused buffer allocates **0 B per operation**.
- **Cross-language:** One documented v3 protocol, with tests across every publisher/subscriber language pair.
- **Concurrent:** Up to **2,048 publisher objects** per queue, with multiple subscribers sharing the work.
- **Cross-platform:** Windows, Linux (glibc), and macOS on supported little-endian 64-bit architectures.

Interprocess is used internally by Microsoft.

## Languages

.NET, Rust, Node.js, Go, and the C SDK are released and available to install. Python is available from source; publishing to PyPI is pending.

| Language | Package | Setup and API guide |
| --- | --- | --- |
| Rust | [`cloudtoid-interprocess`](https://crates.io/crates/cloudtoid-interprocess) (Cargo) | [Rust core](src/rust/README.md) |
| C / C++ | [C SDK](https://github.com/cloudtoid/interprocess/releases/latest); [`cloudtoid-interprocess-ffi`](https://crates.io/crates/cloudtoid-interprocess-ffi) (Cargo) | [C ABI, headers, and shared library](src/c/README.md) |
| Python | Source build (PyPI pending); import `cloudtoid_interprocess` | [Python 3.9+](src/python/README.md) |
| Node.js | [`@cloudtoid/interprocess`](https://www.npmjs.com/package/@cloudtoid/interprocess) (npm) | [Node.js 18+, JavaScript and TypeScript](src/node/README.md) |
| Go | [`github.com/cloudtoid/interprocess/src/go/v3`](https://pkg.go.dev/github.com/cloudtoid/interprocess/src/go/v3) | [Go 1.24+, cgo, and the C SDK](src/go/README.md) |
| .NET | [`Cloudtoid.Interprocess`][NuGet] (NuGet) | [.NET 10+, C# and dependency injection](src/dotnet/README.md) |

Rust supplies the native engine; C, Python, Node.js, and Go use that engine. .NET has its own managed implementation of the same protocol. Node's platform binaries are companion `@cloudtoid/interprocess-*` packages; applications use the main package.

## Quick start

These examples send and receive in one process so both endpoints remain connected. In separate programs, use the same queue name, capacity, and shared directory, and keep at least one endpoint connected throughout the handoff. Each message goes to one subscriber.

<details open>
<summary>Rust</summary>

Run in your Cargo project. Requires Rust 1.87 or later.

```sh
cargo add cloudtoid-interprocess
```

```rust
use cloudtoid_interprocess::{Options, Publisher, Subscriber};

let options = Options::new("example", 65536);
let subscriber = Subscriber::open(&options)?;
let publisher = Publisher::open(&options)?;
publisher.try_send(b"hello")?;
let message = subscriber.try_recv()?;
println!("{message:?}");
```

Endpoints close when dropped. Use `recv()` to wait indefinitely or `recv_timeout(Duration)` for a bounded wait.

</details>

<details>
<summary>C / C++</summary>

macOS Apple Silicon example, using the GitHub CLI. For other platforms, choose darwin-x64, linux-arm64, linux-x64, or win32-x64 in both archive names. See the C guide for Windows setup.

```sh
gh release download native-v3.0.1 --repo cloudtoid/interprocess --pattern "*-darwin-arm64.tar.gz"
mkdir -p cloudtoid-sdk
tar -xzf cloudtoid-interprocess-3.0.1-darwin-arm64.tar.gz -C cloudtoid-sdk --strip-components=1
export PKG_CONFIG_PATH="$PWD/cloudtoid-sdk/lib/pkgconfig:$PKG_CONFIG_PATH"
```

[Complete C SDK setup](src/c/README.md).

```c
#include <interprocess.h>

int main(void) {
    cip_subscriber *subscriber = NULL;
    cip_publisher *publisher = NULL;
    if (cip_subscriber_open("example", NULL, 65536, &subscriber) != 1)
        return 1;
    if (cip_publisher_open("example", NULL, 65536, &publisher) != 1) {
        cip_subscriber_close(subscriber);
        return 1;
    }
    if (cip_try_send(publisher, (const uint8_t *)"hello", 5) == 1) {
        cip_buffer message;
        if (cip_receive(subscriber, 1000, &message) == 1)
            cip_buffer_free(message);
    }
    cip_publisher_close(publisher);
    cip_subscriber_close(subscriber);
    return 0;
}
```

Timeouts are milliseconds; free returned buffers with `cip_buffer_free`.

</details>

<details>
<summary>Python</summary>

PyPI publishing is pending. Run in an activated Python 3.9+ virtual environment with Git, Rust, and a native linker installed.

```sh
python -m pip install "git+https://github.com/cloudtoid/interprocess.git@native-v3.0.1#subdirectory=src/python"
```

```python
from cloudtoid_interprocess import Publisher, Subscriber

with Subscriber("example", 65536) as subscriber, Publisher("example", 65536) as publisher:
    if publisher.try_send(b"hello"):
        print(subscriber.receive(timeout=1.0))
```

Context managers close endpoints. Timeouts are seconds; `receive()` waits indefinitely and permits Python signal handling.

</details>

<details>
<summary>Node.js / TypeScript</summary>

Run in your Node.js project. Requires Node.js 18 or later; platform binaries install automatically.

```sh
npm install @cloudtoid/interprocess
```

```js
import { Publisher, Subscriber } from '@cloudtoid/interprocess';

const subscriber = new Subscriber('example', 65536);
const publisher = new Publisher('example', 65536);
try {
  if (publisher.trySend(Buffer.from('hello'))) {
    const message = await subscriber.receive({
      signal: AbortSignal.timeout(1000)
    });
    console.log(message.toString());
  }
} finally {
  publisher.close();
  subscriber.close();
}
```

CommonJS `require` is also supported. Async receive accepts an optional `AbortSignal`; empty queues wait on a one-millisecond timer without occupying libuv workers.

</details>

<details>
<summary>Go</summary>

Install the C SDK first ([C guide](src/c/README.md)), then run in your Go module. Requires Go 1.24+, cgo enabled, a C compiler, and pkg-config.

```sh
go get github.com/cloudtoid/interprocess/src/go/v3@latest
```

```go
package main

import (
	"context"
	"fmt"
	"github.com/cloudtoid/interprocess/src/go/v3"
	"time"
)

func main() {
	options := interprocess.Options{Name: "example", Capacity: 65536}
	subscriber, err := interprocess.OpenSubscriber(options)
	if err != nil {
		panic(err)
	}
	defer subscriber.Close()
	publisher, err := interprocess.OpenPublisher(options)
	if err != nil {
		panic(err)
	}
	defer publisher.Close()

	ctx, cancel := context.WithTimeout(context.Background(), time.Second)
	defer cancel()
	if sent, err := publisher.TrySend([]byte("hello")); err != nil {
		panic(err)
	} else if sent {
		message, err := subscriber.Receive(ctx)
		if err != nil {
			panic(err)
		}
		fmt.Println(string(message))
	}
}
```

Use `context.Context` for cancellation and deadlines. Install the C SDK before building the Go package.

</details>

<details>
<summary>.NET / C#</summary>

```sh
dotnet add package Cloudtoid.Interprocess
```

```csharp
using Cloudtoid.Interprocess;

var factory = new QueueFactory();
var options = new QueueOptions("example", 65536);
using var subscriber = factory.CreateSubscriber(options);
using var publisher = factory.CreatePublisher(options);
using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1));

if (publisher.TryEnqueue("hello"u8)) {
    var message = subscriber.Dequeue(cancellation.Token);
    Console.WriteLine(System.Text.Encoding.UTF8.GetString(message.Span));
}
```

Blocking receives accept a `CancellationToken`. For dependency injection, register `services.AddInterprocessQueue()` and resolve `IQueueFactory`. See the [.NET package guide](src/dotnet/README.md) and [publisher/subscriber samples](src/dotnet/Sample/).

</details>

## Faster with v3

The .NET v3 benchmarks show **12.0× faster round trips and 2.3× the concurrent throughput of v2** on the same native Mac. Version 3 coalesces notifications, avoiding repeated operating-system calls while readers are active.

| .NET version | 8-byte send + receive | 4 publishers / 4 subscribers |
| --- | ---: | ---: |
| Latest v1 (`1.0.175`) | — | — |
| Latest v2 (`2.1.204`) | 214.6 ns | 1.04 million messages/s |
| v3 | **17.82 ns** | **2.42 million messages/s** |

Measured with .NET 10. [Original results and comparison harness](https://github.com/cloudtoid/interprocess/tree/95c512672d580dd836ba2554cd1f78c0c3826f6c/docs/benchmarks/2026-09-13). See [benchmark details](#on-macos). The new language libraries start with protocol v3.

Upgrade existing v1/v2 applications together: drain the queue, stop all participants, and reopen a fresh queue using v3. All participants sharing a queue must use the same protocol.

## Queue behavior

- Use the same name, capacity, and storage path in every participant. Queue names must be unique even across different paths; Windows uses the name and ignores the path.
- Each queue supports **2,048 connected publisher objects**. Slots are reused after disposal or confirmed process exit. The publisher table adds **256 KiB** plus 256 bytes of header/alignment storage; `Capacity` is the message-buffer size.
- On Windows, use the same user session for all participants. Cross-account connections are not supported.
- The queue is transient and remains available while any publisher or subscriber is connected. Once all are gone, unread messages are lost. Reopening the same name creates a fresh, empty queue. Keep a subscriber connected before a short-lived publisher exits. Dispose participants when finished.
- A paused live participant keeps ownership. Recovery can reclaim abandoned work after a process exits, but may discard queued messages, including completed messages behind an unfinished reservation. This is an IPC queue, not durable storage.
- Supply a destination buffer large enough for the message. A smaller buffer consumes the message and returns only the bytes that fit.

## Performance

Send means enqueue; receive means dequeue. A send + receive operation includes both operations; a send-only measurement excludes receiving. All benchmarks keep publishers and subscribers connected throughout measurement, with queue creation and cleanup outside the timed work.

### On macOS

Measured on an **Apple M5 Max**, macOS 26.6.2, Release builds. Rust: September 14, 2026, Rust 1.98.1 ([raw samples](src/website/benchmarks/rust-macos.txt)). .NET: September 13, 2026, .NET 10.0.12 ([source `f990ba3`](https://github.com/cloudtoid/interprocess/commit/f990ba3)).

| Implementation | Workload | Mean (ns) | StdDev (ns) | Allocated |
| --- | --- | ---: | ---: | ---: |
| Rust | Send + receive, 3 bytes, reused buffer | 12.98 | 0.18 | — |
| Rust | Send + receive, 50 bytes, reused buffer | 16.05 | 0.32 | — |
| Rust | Send + receive, 1,024 bytes, reused buffer | 56.33 | 0.83 | — |
| .NET | Send, 3 bytes | 5.74 | 0.13 | 0 B |
| .NET | Send + receive, 3 bytes, reused buffer | 17.37 | 0.43 | 0 B |
| .NET | Send + receive, 3 bytes, new result array | 20.00 | 0.15 | 32 B |
| .NET | Send + receive, 50 bytes, reused buffer | 18.81 | 0.23 | 0 B |
| .NET | Send + receive, 50 bytes, ring-wrap workload | 21.35 | 0.08 | 0 B |
| .NET | Concurrent delivery, 8 bytes, 1 publisher / 1 subscriber | 113.40 | 0.95 | — |
| .NET | Concurrent delivery, 8 bytes, 1 publisher / 4 subscribers | 155.30 | 11.19 | — |

In-process microbenchmarks, not latency between applications. Rust uses one million operations per sample, four warmups, eight measured samples, a 1 MiB queue, and reused receive storage; allocations were not separately instrumented. The language harnesses differ, so these are not a controlled language comparison. Rust numbers exclude binding overhead for C, Python, Node.js, and Go. Concurrent rows show amortized time per message, including worker startup and completion; their allocations were not measured. Send-only drains outside the timed batch. [BenchmarkDotNet][BenchmarkOrg]: two launches, eight measured iterations, 20 warmups (200 for send-only; three for concurrent delivery).

[.NET benchmark source](src/dotnet/Interprocess.Benchmark/) · [Original reports](https://github.com/cloudtoid/interprocess/tree/95c512672d580dd836ba2554cd1f78c0c3826f6c/docs/benchmarks/2026-09-13). Run the Mac suites from the repository root:

```sh
cargo run --release --locked -p cloudtoid-interprocess --example benchmark
dotnet run --project src/dotnet/Interprocess.Benchmark -c Release -- --filter '*QueueBenchmark*' '*QueueExtendedBenchmark*' --warmupCount 20 --iterationCount 8 --launchCount 2 --iterationTime 250
dotnet run --project src/dotnet/Interprocess.Benchmark -c Release -- --filter '*EnqueueBenchmark*' --warmupCount 200 --iterationCount 8 --launchCount 2
dotnet run --project src/dotnet/Interprocess.Benchmark -c Release -- --filter '*SubscriberBenchmark*' --warmupCount 3 --iterationCount 8 --launchCount 2 --iterationTime 250
```

### On Windows

Measured September 13, 2026, on an **Apple M5 Max**, Windows 11 Pro 25H2 ARM64 VM (UTM, 4 vCPUs, 12 GiB RAM), .NET 10.0.12, Release build. V3 source: [`f990ba3`](https://github.com/cloudtoid/interprocess/commit/f990ba3).

| Workload | Mean (ns) | StdDev (ns) | Allocated |
| --- | ---: | ---: | ---: |
| Send, 3 bytes | 6.26 | 0.26 | 0 B |
| Send + receive, 3 bytes, reused buffer | 17.96 | 0.43 | 0 B |
| Send + receive, 3 bytes, new result array | 19.78 | 0.38 | 32 B |
| Send + receive, 50 bytes, reused buffer | 17.98 | 0.28 | 0 B |
| Send + receive, 50 bytes, ring-wrap workload | 21.81 | 0.30 | 0 B |
| Concurrent delivery, 8 bytes, 1 publisher / 1 subscriber | 101.30 | 1.68 | — |
| Concurrent delivery, 8 bytes, 1 publisher / 4 subscribers | 85.62 | 12.50 | — |

Same .NET workloads and allocation conventions as the Mac suite. Two launches and eight measured iterations; single-thread runs used one pinned vCPU and 20 warmups (200 for send-only), while concurrent runs used all four vCPUs and three warmups. [.NET benchmark source](src/dotnet/Interprocess.Benchmark/) · [Original reports](https://github.com/cloudtoid/interprocess/tree/95c512672d580dd836ba2554cd1f78c0c3826f6c/docs/benchmarks/2026-09-13).

### On Linux

Measured September 13, 2026, on an **Apple M5 Max**, Ubuntu 24.04 ARM64 VM (Lima/QEMU, 4 vCPUs, 8 GiB RAM), Linux 6.12.94 with 16 KiB pages, .NET 10.0.12, Release build. V3 source: [`f990ba3`](https://github.com/cloudtoid/interprocess/commit/f990ba3).

| Workload | Mean (ns) | StdDev (ns) | Allocated |
| --- | ---: | ---: | ---: |
| Send, 3 bytes | 6.00 | 0.07 | 0 B |
| Send + receive, 3 bytes, reused buffer | 16.68 | 0.18 | 0 B |
| Send + receive, 3 bytes, new result array | 19.78 | 0.20 | 32 B |
| Send + receive, 50 bytes, reused buffer | 17.64 | 0.21 | 0 B |
| Send + receive, 50 bytes, ring-wrap workload | 20.82 | 0.11 | 0 B |
| Concurrent delivery, 8 bytes, 1 publisher / 1 subscriber | 112.90 | 1.10 | — |
| Concurrent delivery, 8 bytes, 1 publisher / 4 subscribers | 158.60 | 13.64 | — |

Same .NET workloads and allocation conventions as the Mac suite. Two launches and eight measured iterations; single-thread runs used one pinned vCPU and 20 warmups (200 for send-only), while concurrent runs used all four vCPUs and three warmups. [.NET benchmark source](src/dotnet/Interprocess.Benchmark/) · [Original reports](https://github.com/cloudtoid/interprocess/tree/95c512672d580dd836ba2554cd1f78c0c3826f6c/docs/benchmarks/2026-09-13).

[Protocol v3](docs/protocol.md) documents the complete shared-memory format and synchronization rules. [Interoperability tests](tests/interop/README.md) exercise every publisher/subscriber language pair and mixed-language concurrent delivery across participant crashes.

## Implementation Notes

Messages travel through a shared, circular memory-mapped buffer. Coalesced notifications reduce operating-system calls while keeping blocked subscribers responsive. Rust and .NET blocking readers also retry after five-millisecond waits when notifications are missed; bindings follow their documented waiting and cancellation behavior. Cross-process wakeups use named semaphores, with POSIX implementations on [Linux](src/dotnet/Interprocess/Semaphore/Linux/Interop.cs) and [macOS](src/dotnet/Interprocess/Semaphore/MacOS/Interop.cs).

Positions advance monotonically while the physical buffer wraps. Before the queue reaches `INT64_MAX` bytes reserved or `INT32_MAX` participant registrations over its lifetime, drain it and move all participants to a fresh queue.

## How to Contribute

- Create a branch from `main`.
- Ensure that all tests pass on Windows, Linux, and macOS.
- Keep the code coverage number above 80% by adding new tests or modifying the existing tests.
- Send a pull request.

## Author

[**Pedram Rezaei**][PedramLinkedIn] is a software architect at Microsoft with years of experience building highly scalable and reliable cloud-native applications for Microsoft.

[Cloudtoid]:https://github.com/cloudtoid
[License]:https://github.com/cloudtoid/interprocess/blob/main/LICENSE
[LicenseBadge]:https://img.shields.io/badge/License-MIT-blue.svg
[NuGet]:https://www.nuget.org/packages/Cloudtoid.Interprocess/
[BenchmarkOrg]:https://benchmarkdotnet.org/
[PedramLinkedIn]:https://www.linkedin.com/in/pedramrezaei/
