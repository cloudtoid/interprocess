# Cloudtoid Interprocess C SDK

[API guide](https://cloudtoid.com/docs/c/) · [Queue concepts](https://cloudtoid.com/docs/concepts/) · [Website](https://cloudtoid.com)

The C ABI exposes the shared Rust engine to C/C++ and other native callers. It interoperates with .NET protocol v3.

## [Install the prebuilt SDK](https://cloudtoid.com/docs/c/#install)

macOS Apple Silicon example, using the GitHub CLI. For other platforms, choose darwin-x64, linux-arm64, linux-x64, or win32-x64 in both archive names. See the [C guide](https://cloudtoid.com/docs/c/) for Windows setup.

```sh
gh release download native-v3.0.1 --repo cloudtoid/interprocess --pattern "*-darwin-arm64.tar.gz"
mkdir -p cloudtoid-sdk
tar -xzf cloudtoid-interprocess-3.0.1-darwin-arm64.tar.gz -C cloudtoid-sdk --strip-components=1
export PKG_CONFIG_PATH="$PWD/cloudtoid-sdk/lib/pkgconfig:$PKG_CONFIG_PATH"
```

On Windows, extract the `win32-x64` archive and set `PKG_CONFIG_PATH` to its `lib/pkgconfig` directory. Add its `lib` directory to `PATH` for the DLL. Go on Windows also requires a cgo-compatible C compiler and pkg-config.

[Build the SDK from source](https://cloudtoid.com/docs/c/#build-from-source).

## Example

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

`cip_receive` returns owned bytes; free successful results with `cip_buffer_free`. Finish every call before closing its handle.

Queues are transient: once all publishers and subscribers are gone, unread messages are lost. Keep at least one endpoint connected throughout a handoff between processes. See the [API guide](https://cloudtoid.com/docs/c/) for waiting, errors, ownership, and limits.
