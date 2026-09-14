# Cross-language interoperability

The matrix runs Rust, C, Python, Node.js, Go, and .NET publishers against each of the six subscriber implementations, in separate processes. Each pair transfers 2,000 messages with deterministic content, increasing sequence numbers, and payload sizes from 8 to 258 bytes through a 4 KiB circular buffer. Every receiver checks complete payload equality and order. Process deadlines catch stalls. The subscriber holds the queue open before its publisher connects.

Run from the repository root with Rust, CMake, a C compiler, pkg-config, Go 1.24+, Node.js 18+, Python 3.9+, and .NET 10 on PATH:

```sh
python -m venv .venv
# Activate .venv using your platform's activation command.
python tests/interop/build.py
```

On Windows, use MSVC for Rust and install MinGW-w64 GCC/pkgconf for the C and Go callers. The workflow shows the setup. The script builds and installs the C SDK into `target/sdk`, builds Python/Node packages and drivers, runs their API checks, and executes every pair. It sets library search paths for its subprocesses.

After building, `run.py` can rerun the matrix. Set the C SDK loader path and use the Python environment containing the built wheel. `INTEROP_LANGUAGES=rust,dotnet` limits a local diagnostic run; CI leaves it unset. `INTEROP_COUNT` increases the message count for stress runs. The full build runs native process-crash tests too; the pair matrix itself covers ordinary data delivery and lifetime, not fault injection in every language pair.


`mixed.py` adds a shared queue with six concurrent publishers and six competing subscribers, one of each language. It checks 24,000 unique IDs and complete payloads across two traffic phases, while killing an extra registered publisher and subscriber. The killed endpoints do not own in-flight messages; the Rust fault-injection suite covers unfinished publisher reservations and reader ownership. Set `INTEROP_MIXED_COUNT` to change each publisher's per-phase count. The full build and CI run both scenarios.
