# Cloudtoid Interprocess C SDK

The C ABI exposes the shared Rust engine to C/C++ and other native callers. It interoperates with .NET protocol v3.

From the repository root, with Rust and CMake installed:

```sh
cmake -S src/c -B target/c-sdk -DCMAKE_INSTALL_PREFIX="$HOME/.local"
cmake --build target/c-sdk --config Release
cmake --install target/c-sdk --config Release
```

Link with `pkg-config --cflags --libs cloudtoid-interprocess`. On Unix, these flags embed the installed library directory as a runtime search path. Windows callers use the DLL and import library; add the DLL directory to `PATH` or place the DLL beside the executable. The C header documents status codes, timeout units, ownership, buffer truncation, and concurrent-close requirements.

```c
#include <interprocess.h>
cip_subscriber *subscriber = NULL;
cip_publisher *publisher = NULL;
if (cip_subscriber_open("example", NULL, 65536, &subscriber) != CIP_OK)
    return 1;
if (cip_publisher_open("example", NULL, 65536, &publisher) == CIP_OK) {
    const uint8_t message[] = "hello";
    int32_t status = cip_try_send(publisher, message, sizeof(message) - 1);
    /* CIP_UNAVAILABLE: full/recovering; CIP_ERROR: inspect cip_last_error_kind(). */
    if (status == CIP_OK) {
        cip_buffer received;
        if (cip_receive(subscriber, 1000, &received) == CIP_OK)
            cip_buffer_free(received);
    }
    cip_publisher_close(publisher);
}
cip_subscriber_close(subscriber);
```

`cip_receive` returns owned bytes; free successful results with `cip_buffer_free`. Finish every call before closing its handle. See [protocol v3](https://github.com/cloudtoid/interprocess/blob/main/docs/protocol.md).

Use `CIP_OK`, `CIP_UNAVAILABLE`, and `CIP_ERROR` to interpret status results. On error, `cip_last_error_kind()` gives a stable `cip_error_kind`; `cip_last_error()` provides diagnostic text on the same thread. The prebuilt SDK contains shared libraries. Static linking is source-build only (`cargo build --release -p cloudtoid-interprocess-ffi`); define `CIP_STATIC` when using the resulting static library on Windows.

Handles must not be closed concurrently with an operation. Use bounded `cip_receive` timeouts when shutdown is needed. Open handles after `fork()`; do not use inherited handles in the child. Closing an inherited handle leaves the parent's registration intact.

## Queue lifetime

The queue is transient: it stays alive while at least one publisher or subscriber is connected. Once all endpoints are closed or their processes exit, unread messages are lost. Opening the same name again creates a fresh, empty queue. Keep a subscriber connected before a short-lived publisher exits; a surviving publisher also keeps the queue alive.

Queue names must be nonempty and contain no slash, backslash, or NUL. The maximum is 24 UTF-8 bytes on macOS and 245 on Linux; use at most 24 bytes for portable names.
