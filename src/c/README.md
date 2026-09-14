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
cip_publisher *publisher = NULL;
if (cip_publisher_open("example", NULL, 65536, &publisher) == 1) {
    const uint8_t message[] = "hello";
    int32_t status = cip_try_send(publisher, message, sizeof(message) - 1);
    /* 1: sent; 0: full/recovering; -1: inspect cip_last_error(). */
    cip_publisher_close(publisher);
}
```

A subscriber must remain connected when a short-lived publisher exits if its messages are to remain available. `cip_receive` returns owned bytes; free successful results with `cip_buffer_free`. Finish every call before closing its handle. See [protocol v3](https://github.com/cloudtoid/interprocess/blob/main/docs/protocol.md).
