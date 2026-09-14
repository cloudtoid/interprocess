# Cloudtoid Interprocess C SDK

The C ABI exposes the shared Rust engine to C/C++ and other native callers. It interoperates with .NET protocol v3.

From the repository root, with Rust and CMake installed:

```sh
cmake -S src/c -B target/c-sdk -DCMAKE_INSTALL_PREFIX="$HOME/.local"
cmake --build target/c-sdk --config Release
cmake --install target/c-sdk --config Release
```

Link with `pkg-config --cflags --libs cloudtoid-interprocess`. Add the installed `lib` directory to your platform's library search path when it is outside a system location. Windows callers use the DLL and import library. The C header documents status codes, timeout units, ownership, buffer truncation, and concurrent-close requirements.

```c
#include <interprocess.h>
cip_subscriber *subscriber = NULL;
cip_publisher *publisher = NULL;
if (cip_subscriber_open("example", NULL, 65536, &subscriber) != 1)
    return 1;
if (cip_publisher_open("example", NULL, 65536, &publisher) == 1) {
    const uint8_t message[] = "hello";
    int32_t status = cip_try_send(publisher, message, sizeof(message) - 1);
    /* 1: sent; 0: full/recovering; -1: inspect cip_last_error(). */
    if (status == 1) {
        cip_buffer received;
        if (cip_receive(subscriber, 1000, &received) == 1)
            cip_buffer_free(received);
    }
    cip_publisher_close(publisher);
}
cip_subscriber_close(subscriber);
```

`cip_receive` returns owned bytes; free successful results with `cip_buffer_free`. Finish every call before closing its handle. See [protocol v3](https://github.com/cloudtoid/interprocess/blob/main/docs/protocol.md).

## Queue lifetime

The queue is transient: it stays alive while at least one publisher or subscriber is connected. Once all endpoints are closed or their processes exit, unread messages are lost. Opening the same name again creates a fresh, empty queue. Keep a subscriber connected before a short-lived publisher exits; a surviving publisher also keeps the queue alive.
