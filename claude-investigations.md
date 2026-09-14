# PR #57 review: multi-language core (`codex/multilanguage-core`)

Review of `main...HEAD` at `56a3835`. I read the Rust core, platform layers, C ABI, Python/Node/Go bindings, interop harness, workflows, packaging script, protocol/release docs, and the website examples. I compared the Rust core with the .NET `Publisher`, `Subscriber`, `PublisherRegistry`, `ReaderLease`, `MemoryFileUnix`, and `MemoryFileWindows`.

**What I ran (macOS arm64):**
- `cargo clippy --workspace --all-targets --release -D warnings`: clean.
- `cargo test --release -p cloudtoid-interprocess`: 12 passed, 1 ignored (fault-injection helper), 0 doctests.
- `go vet ./...` in `src/go`: clean.
- A few Python probes against the locally built `_native.abi3.so` (results below).

**Not run:** Node (no `node` on PATH), Windows, Linux, and the interop matrix. Items marked **(verify)** are reasoned from the code but not reproduced.

**Order of work.** Every package publishes as `3.0.0`. After the first release, any API change below becomes a semver-major break in Rust, Python, npm, and Go. Settle section 3 (API fit) **before** the first registry publish, even if other fixes land later.

---

## 1. Correctness bugs

### 1.1 Python `Subscriber.receive()` drops a consumed message when a signal is pending (high)
`src/python/src/lib.rs:111-117`
```rust
let message = py.detach(|| subscriber.receive_timeout(wait)).map_err(error)?;
py.check_signals()?;                 // raises KeyboardInterrupt etc.
if let Some(message) = message { return Ok(Some(...)); }
```
If a message was dequeued in this slice and a signal arrives (Ctrl+C, SIGTERM handler raising), `check_signals()?` returns the error. The dequeued message is dropped, and it has already been removed from shared memory, so it is lost.
**Fix:** return the message first. Call `check_signals()` only when `message` is `None`.
**Test:** in a thread, `signal.pthread_kill(main, SIGINT)` (or `_thread.interrupt_main()`) while a publisher sends. Assert that the message is either returned or still in the queue afterwards.

### 1.2 Rust `receive_wait`: a relay-notification error overwrites a delivered message (medium; data loss)
`src/rust/src/queue.rs:407-410`
```rust
if relay && !self.shared.empty() { self.shared.notify()?; }
result
```
If `result` is `Ok(Some(message))` and `sem_post`/`ReleaseSemaphore` fails, the function returns `Err`. The consumed message is dropped. This affects C, Go, and Python, which all go through `receive_timeout`.
**Fix:** make the relay best-effort (ignore or log the error). Returning `result` must always win. See also 1.3 and S4.

### 1.3 A notification error after commit is reported as a send failure; batches lose their committed count (medium)
`src/rust/src/queue.rs:283-285` (`self.shared.notify()?` after `state.store(2)`) and `queue.rs:239-245`.
- `try_send` can return `Err` for a message that **was** published. The C header admits this at `interprocess.h:24-25`, but Python, Node, and Go do not document it. A retrying caller creates a duplicate.
- `try_send_batch` returns `Err` after committing `sent` messages. The count is lost, so the caller cannot know which prefix went through.

The protocol already treats the semaphore as a hint, and every blocking reader retries at least every 5 ms. A failed post therefore cannot stall delivery.
**Fix:** after commit, ignore `notify()` errors (or log them in debug builds). This also makes `notify()` infallible and removes the doc caveat from every binding. Alternatively, return `Ok(sent)` and surface the error on the next call, but ignoring is simpler and matches the protocol's "wakeup hint" semantics.

### 1.4 A long queue name on macOS gives a misleading error and leaves a stale backing file (medium)
`src/rust/src/platform/unix.rs:70-79`, `Options::validate` in `src/rust/src/lib.rs:45-64`.
macOS limits POSIX semaphore names to 31 bytes (`PSEMNAMLEN`). `/ct3ip.` is 7 bytes, so a queue name longer than **24 bytes** fails. Reproduced:
```
Publisher('x'*40, 64)  -> RuntimeError: File name too long (os error 63)
$TMPDIR/.cloudtoid/interprocess/v3/mmf/xxxx…xxxx.qu   # left behind, 0 bytes
```
The failure happens at `Signal::unlink(&options.name)?` inside the first-opener branch. By then the `.qu` file has been created but not sized or removed. Users will look for a path-length problem, not a name-length problem.
**Fix:**
- (a) Validate name length in `Options::validate` per platform, with a clear `Error::Invalid`. Suggested limits: macOS ≤ 24 bytes; Linux ≤ 251 − 6 for `sem.ct3ip.` and ≤ 252 for `N.qu`; Windows object name limits.
- (b) On any error after `OpenOptions::open` in the first-opener branch, remove the file while still holding coordination.
- (c) Document the 24-byte macOS limit in the protocol doc and every README. The protocol doc only says "must fit platform object-name limits". Check .NET too: `QueueOptions` has the same gap.

### 1.5 Fork safety: a forked child can destroy the parent's queue or leases (medium; mainly Python and C)
`src/rust/src/platform/unix.rs:111-125` (`Mapping::drop`) and `unix.rs:168-173` (`Lease::drop`).
After `fork()`, the child shares the parent's open file descriptions, so it also shares the `flock` locks. If the child drops an inherited endpoint (Python GC or `close()`, a C `close`):
- `Mapping::drop` tries `LOCK_EX|LOCK_NB` on the **shared** open file description. With no other process attached, the upgrade succeeds, and the child unlinks the semaphore, lease directory, and backing file while the parent still uses them. The next opener then creates a separate queue (split brain).
- `Lease::drop` deletes the parent's lease file. Missing file means dead, so another subscriber can "repair" a live reader's ownership mid-read and corrupt data.

Python's `multiprocessing` used `fork` by default on Linux before 3.14, so this is realistic.
**Fix:** record `getpid()` at open. In the `Drop` impls, skip all shared cleanup (and in debug builds, panic or refuse calls) when `getpid()` differs. Document "endpoints are not fork-safe; open them after forking" in the Python, C, and Rust docs.
**Test:** Python `os.fork()`: the child closes the inherited subscriber and exits. The parent must still send and receive, and the `.qu` file must still exist.

### 1.6 Corrupt-header hardening: validate record length against `used` before clearing (low)
`src/rust/src/queue.rs:483-495`. `body` is checked only against `capacity - 8`. A corrupted length larger than `write - read` makes `clear(read, length)` zero bytes that belong to another publisher's live reservation, and advances `read` past `write`. Also check `length as i64 <= write - read`, loading `write` under the reader lock. Low priority because it only matters when memory is already corrupt. Note that the `Corrupt` path restores state 2, so the queue returns `Corrupt` forever with no recovery. Document that.

---

## 2. Release and packaging

### 2.1 The macOS C SDK dylib has an absolute build-machine install name (high for the macOS C/Go SDK)
```
$ otool -D target/sdk/lib/libcloudtoid_interprocess.dylib
/Users/pedram/repos/interprocess/target/c-sdk/cargo/release/deps/libcloudtoid_interprocess.dylib
```
Any C or Go program linked against the released SDK records the CI runner's path. At runtime it works only through `DYLD_*` env vars or `/usr/local/lib` fallback. Both `src/c/CMakeLists.txt` and the `mac-intel` job (`release-native.yml:62`) ship this.
**Fix:** link with `-Wl,-install_name,@rpath/libcloudtoid_interprocess.dylib` (for example via `RUSTFLAGS`/`cargo rustc -- -C link-arg=...` for the ffi crate on macOS). Add `-Wl,-rpath,${libdir}` to the `.pc` `Libs:` on macOS and Linux so `pkg-config` users (including Go) run without env vars. On Linux, consider setting a SONAME (`-Wl,-soname,libcloudtoid_interprocess.so.3`).
**Test (CI):** after install, build `c_driver.c` with only `pkg-config --libs`, **unset** `DYLD_LIBRARY_PATH`/`LD_LIBRARY_PATH`, and run it. Today `tests/interop/build.py:13-15` sets those variables and hides the problem.

### 2.2 The glibc floor is claimed but not enforced (medium) (verify)
`docs/releasing.md` says "Linux x64 and ARM64 (glibc 2.34+)". Wheels, `.node` files, and `.so` files are built on `ubuntu-latest` / `ubuntu-24.04-arm` (glibc 2.39) with plain `maturin build`, and nothing checks symbol versions.
**Fix:** use `maturin build --compatibility manylinux_2_34` (it fails if violated), or build in a manylinux_2_34 container or with `--zig`. Add a CI step that runs `objdump -T` and asserts no `GLIBC_2.3[5-9]` symbols in the `.node` and `.so` files.

### 2.3 Untested published matrix (medium)
- Python is tested only on 3.12 (`interop.yml:25`), but the wheel is `abi3-py39`. Add 3.9 and the newest CPython. Also note that abi3 wheels do not install on free-threaded builds (3.13t/3.14t), which fall back to the sdist and need Rust.
- Node is tested only on 24 (`interop.yml:28`), but `engines` says `>=18`. Test 18 and 20, or raise `engines` to `>=20` (18 is EOL).
- Intel macOS artifacts are cross-built and never executed. This is documented; running them under Rosetta on the arm runner (`arch -x86_64`) would be cheap coverage.

### 2.4 CI lint covers only the core crate (low)
`native.yml:22` runs `cargo clippy -p cloudtoid-interprocess` only. The ffi, python, and node crates are unchecked, though `--workspace` is clean today. Switch to `--workspace`.

### 2.5 Rust crate metadata (low)
- No `rust-version`. The core uses `usize::is_multiple_of` (stable in 1.87) and `Option::is_none_or` (1.82). Set `rust-version = "1.87"` (confirm with `cargo msrv`) and add an MSRV CI job.
- Missing `keywords`, `categories`, and `[package.metadata.docs.rs]` targets (docs.rs builds Linux only, so Windows-only items are invisible).

### 2.6 Rust README is not doctested and shows a hidden line (low)
`src/rust/README.md:12` contains `# Ok::<(), cloudtoid_interprocess::Error>(())`. GitHub and crates.io render that line literally. The crate has **0 doctests**.
**Fix:** add `#![doc = include_str!("../README.md")]` in `lib.rs` so the README compiles as a doctest (hidden lines then work on docs.rs), or drop the `#` line. Add `#![warn(missing_docs)]`: 16 public items are undocumented today, and none of the `Result` functions has an `# Errors` section.

### 2.7 Website (low)
- `src/website/site.js:68` pins guide links to commit `5f895e9…`. Point them at `main` (or a release tag) before merge.
- The website Node example (`site.js:15`) uses `import queue from …; const {Publisher, Subscriber} = queue`, but `src/node/README.md` documents named ESM imports. Use the named form in both.
- Confirm `src/website/.openai/hosting.json` (a hosting project id) is meant to be public.

---

## 3. API fit per ecosystem

### 3.1 Cross-cutting: typed errors in every binding (high, API)
Rust has `Error::{Invalid, CapacityMismatch, PublisherLimit, Exhausted, Corrupt, Io}`. Every binding flattens it to a string:
- C: `cip_last_error()` text only (`src/c/src/lib.rs:11-24`).
- Go: `errors.New(C.GoString(...))` (`src/go/interprocess.go:57-66`), so callers cannot use `errors.Is`.
- Python: always `RuntimeError` (`src/python/src/lib.rs:9-11`).
- Node: plain `Error` with no `code` (`src/node/src/lib.rs:5-7`).

Callers need at least "capacity mismatch" and "publisher limit" to handle them programmatically.
**Proposal:**
- **C:** a `cip_error_kind` enum, via negative status codes or `int32_t cip_last_error_kind(void)`.
- **Go:** sentinels `ErrCapacityMismatch`, `ErrPublisherLimit`, `ErrInvalidOptions`, `ErrExhausted`, `ErrCorrupt`, wrapped with `fmt.Errorf("interprocess: %s: %w", msg, ErrX)`. Carry the kind through the `cip_result` shim.
- **Python:** `class InterprocessError(Exception)`, with subclasses `CapacityMismatchError(InterprocessError, ValueError)` and `PublisherLimitError`. Raise `ValueError` for invalid options. Raise `ValueError("I/O operation on closed …")` for a closed endpoint, like file objects do.
- **Node:** `err.code = 'ERR_CAPACITY_MISMATCH' | 'ERR_PUBLISHER_LIMIT' | 'ERR_INVALID_OPTIONS' | 'ERR_CLOSED' | …`, and `TypeError`/`RangeError` for bad arguments.

### 3.2 Rust
1. **`try_send(...) -> Result<bool>` is a footgun with `?`.** `publisher.try_send(msg)?;` compiles without a warning and silently ignores "full". `#[must_use]` does not help, because `?` consumes the `Result`. Options:
   - (a) Idiomatic, like std/crossbeam/tokio: `try_send(&self, &[u8]) -> Result<(), TrySendError>` with `TrySendError::{Full, Error(Error)}`.
   - (b) Keep `bool`, add `#[must_use]`, and state the pitfall in the docs.

   (a) is preferable before 3.0.0 ships.
2. **Naming.** std `mpsc`, crossbeam, and tokio all use `send`/`try_send`/`recv`/`try_recv`/`recv_timeout`. `receive`/`try_receive`/`receive_timeout`/`try_receive_into` will feel foreign. Consider `recv`, `try_recv`, `recv_timeout`, `try_recv_into`. This is a judgment call because the other bindings use "receive"; at minimum, decide deliberately.
3. **`Error` should be `#[non_exhaustive]`.** Otherwise adding a variant (for example a name-length error, 1.4) is breaking.
4. **`Options`** has all-public fields plus a builder (`src/rust/src/lib.rs:23-43`). Adding a field later breaks struct literals. Either make the fields private with getters, or mark it `#[non_exhaustive]` and keep `new` + `with_path`.
5. **`Publisher::open(options: Options)` takes ownership**, so every example does `options.clone()`. Take `&Options` instead (the core clones internally anyway).
6. **Implement `Debug`** for `Publisher` and `Subscriber` (API guideline C-DEBUG): name, capacity, id.
7. There is no blocking `recv_into(&mut buf)` or `recv_into_timeout`. The .NET API has `Dequeue(Memory<byte>, ct)`, and C/Go have only `try_receive_into`. Consider adding one for parity with the zero-allocation story.
8. There is no async story. That is fine for 3.0, but document that `receive()`/`receive_timeout()` block the thread, so tokio users should use `spawn_blocking`.

### 3.3 C
1. **`CIP_API` is always `__declspec(dllimport)` on Windows** (`src/c/include/interprocess.h:6`). The crate also builds a `staticlib`, and static consumers then get unresolved `__imp_` symbols. Add a `CIP_STATIC` guard: `#if defined(_WIN32) && !defined(CIP_STATIC)`.
2. **Status values are magic numbers** (`interprocess.h:18`). Provide named constants, for example `typedef enum { CIP_ERROR = -1, CIP_EMPTY = 0, CIP_OK = 1 } cip_status;` (plus error kinds, 3.1). Many C users expect `0 == success`, so named constants prevent `if (!cip_try_send(...))` bugs.
3. **CMake consumers expect `find_package(cloudtoid_interprocess CONFIG)`** and an imported target such as `cloudtoid::interprocess`. Only pkg-config is installed today. Also consider installing the `.a`/`.lib` static library that is already built.
4. Add a version function or macros (`CIP_VERSION_MAJOR`…, `cip_version()`).
5. Missing `cip_try_send_batch`, which Rust, Python, and Node have.
6. There is no way to interrupt `cip_receive(h, -1, …)`, and closing the handle while it waits is UB. Either add `cip_subscriber_interrupt`, or document "use bounded timeouts if you need to shut down" next to `timeout_ms`.
7. `cloudtoid-interprocess.pc.in` uses `prefix=${pcfiledir}/../..`, which is wrong when `CMAKE_INSTALL_LIBDIR` is multi-level (Debian `lib/x86_64-linux-gnu`). Compute the relative path in CMake.

### 3.4 Go
1. Typed errors (3.1).
2. **Each blocked `Receive` pins an OS thread.** It loops over cgo calls that block up to 5 ms (`interprocess.go:145-165`). With many goroutines in `Receive`, the runtime creates one M per blocked cgo call and can hit the 10 000-thread limit, which crashes the process. Go developers expect blocked goroutines to be cheap.
   - Minimum: document it.
   - Better: one internal waiter goroutine per `Subscriber` does the cgo waiting and hands messages to receivers over a channel (fan-out), or do non-blocking `TryReceive` plus `time.Timer` backoff in Go, with no blocking cgo call.
3. Missing doc comments on exported identifiers: `ErrClosed`, `OpenPublisher`, `TrySend`, `Close`, `OpenSubscriber`, `Subscriber.Close`. pkg.go.dev and `revive`/`golint` expect them. Add `Example…` functions (`example_test.go`) so pkg.go.dev shows runnable usage.
4. README and website import the package as `queue "github.com/cloudtoid/interprocess/src/go/v3"`, but the package name is `interprocess`. Go convention is to use the package name without an alias: `interprocess.OpenPublisher(...)`.
5. `cmd/interop` ships inside the public module and is `go install`-able. Move it under `internal/`, or into `tests/interop/go`.
6. Module path `…/src/go/v3`: the `src/` segment and a first release at `v3` are unusual for Go. They are acceptable in a monorepo, but moving the module to `/go` before the first tag is the only cheap time to change it.
7. Consider a finalizer safety net (`runtime.AddCleanup` in Go 1.24) that closes a leaked handle, as `os.File` does. `Close` remains the documented path.
8. Parity gaps: `TrySendBatch`, and a blocking `ReceiveInto(ctx, buf)`.

### 3.5 Python
Probed on the local build:
```
p.try_send(bytearray(b'ab'))  -> TypeError: 'bytearray' object is not an instance of 'bytes'
p.try_send(memoryview(b'ab')) -> TypeError
Publisher(n, 64, path=pathlib.Path('/tmp')) -> TypeError: 'PosixPath' object is not an instance of 'str'
Publisher(n, 128)  (mismatch) -> RuntimeError
```
1. **Accept the buffer protocol** (`bytes`, `bytearray`, `memoryview`, numpy arrays) for `try_send` and `try_send_batch`. Use `pyo3::buffer::PyBuffer<u8>` (copy under the GIL) instead of `&Bound<PyBytes>` (`lib.rs:33,40`). Python developers expect "bytes-like object".
2. **Accept `os.PathLike`** for `path` (extract `PathBuf`, `lib.rs:12,28,72`). Consider making `path` keyword-only.
3. Typed exceptions (3.1).
4. **Closing from another thread.** `close()` during a blocking `receive()` raises PyO3's "Already borrowed" `RuntimeError` (`lib.rs:123-125` needs `&mut self`). `__exit__` then masks the original exception. Go and Node interrupt pending receives on close, and Python users expect the same for shutdown from a signal handler or another thread.
   **Fix:** make the classes `frozen` and hold `inner` in a lock or `ArcSwapOption`-style cell. Set a `closed` flag that the 100 ms `receive` loop checks, and return or raise `ClosedError` there. Drop the core handle only once no call holds it.
5. Add a `try_receive_into(buffer) -> int | None` (writable buffer protocol) for the zero-allocation path that Rust, C, Go, and .NET expose.
6. Ship type information: `py.typed` plus a `_native.pyi` or `__init__.pyi` stub. Right now mypy and IDEs see nothing.
7. Nice to have: a `closed` property, `__repr__`, and a documented asyncio pattern (`await asyncio.to_thread(sub.receive, timeout)`).
8. Fork safety (1.5).

### 3.6 Node.js
1. **Types say `Buffer` only** (`index.d.ts:5-6`). Idiomatic modern Node APIs accept `Uint8Array` (and Buffer is a subclass). **(verify)** whether napi-rs v3 `Buffer` accepts a plain `Uint8Array` on Node 18/20/22. Either way, type the parameters as `Uint8Array` and use a napi type that accepts typed arrays.
2. **The 1 ms polling timer** (`index.js:20-37`) means each pending `receive()` wakes the event loop about 1000 times per second while idle. That is measurable CPU and battery use for a long-lived idle worker, and servers commonly `await receive()` forever.
   **Alternatives:**
   - A dedicated native thread per `Subscriber` (not the libuv pool) that loops `receive_timeout(≤5 ms)` and resolves through a `ThreadsafeFunction`, interrupted on close or abort.
   - Adaptive backoff (1 ms rising to about 10–20 ms when idle), which trades latency.

   At minimum, benchmark idle CPU and document it.
3. Error `code`s (3.1).
4. Add `[Symbol.dispose]()` (and, optionally, `[Symbol.asyncDispose]`) so `using sub = new Subscriber(...)` works in TypeScript 5.2+ and Node 22+. Guard with `Symbol.dispose ?? Symbol.for('nodejs.dispose')`.
5. Consider an async iterator (`for await (const msg of subscriber.messages({ signal }))`), the usual Node pattern for consumers.
6. `Publisher` is exported raw from native while `Subscriber` is JS-wrapped. Wrap both for consistent validation, error codes, and dispose support.
7. `capacity` is `u32` in napi (`src/node/src/lib.rs:8`), so capacities of 4 GiB or more cannot be expressed, though Rust and .NET allow them. Use `f64` or `BigInt` with validation, or document the limit.
8. The loader (`index.js:4-7`) throws a bare "Cannot find module @cloudtoid/interprocess-linux-x64" on unsupported platforms (for example musl/Alpine, win32-arm64). Wrap it in a clear "unsupported platform" error. Consider npm's `libc: ["glibc"]` field on the Linux packages.
9. Add a package `exports` map with a `types` condition. It is optional but standard now.

---

## 4. Cross-binding consistency

| Capability | Rust | C | Python | Node | Go | .NET |
|---|---|---|---|---|---|---|
| batch send | ✓ | ✗ | ✓ | ✓ | ✗ | ✗ |
| try-receive into caller buffer | ✓ | ✓ | ✗ | ✗ | ✓ | ✓ |
| blocking receive into buffer | ✗ | ✗ | ✗ | ✗ | ✗ | ✓ |
| close interrupts a pending receive | n/a (borrow) | UB | raises "Already borrowed" | ✓ | ✓ | ✓ |
| typed error kinds | ✓ | ✗ | ✗ | ✗ | ✗ | exceptions |
| bytes-like input beyond native type | n/a | n/a | ✗ | Buffer only | n/a | span |

Decide which gaps are intentional and document them. The close semantics row matters most.

---

## 5. Simplifications

- **S1. Model the shared header as `#[repr(C)]` structs** instead of raw offset arithmetic. `queue.rs:59-83` uses `.add(128)` for the gate and `.add(TABLE_OFFSET + slot*SLOT_SIZE + 8)` for `active`. Extend `Header` with `capacity: i64` at 32, reserved padding, and `gate: AtomicI32` at 128. Add `struct Slot { owner: AtomicI64, active: AtomicI32, _reserved: [u8; 116] }`, with `offset_of!` and `size_of::<Slot>() == 128` tests. This removes most of the unsafe pointer math and pins every protocol offset in `protocol_layout` (today the gate, slot, and Windows capacity offsets are untested).
- **S2. Extract one ring-split helper** (`fn split(&self, offset, len) -> (usize, usize)`) shared by `write`, `read`, and `clear` (`queue.rs:145-174`).
- **S3. Share admission** between `try_send` and `try_send_batch` (`queue.rs:217-246`). Either write `fn admit(&self) -> Option<Active<'_>>`, or implement `try_send` as a one-element batch. Check the length once, in the public entry points, not again in `send_admitted`.
- **S4. Make `notify()` infallible** by ignoring post errors after commit (fixes 1.2 and 1.3). This removes `Result` plumbing from `notify`, `send_admitted`'s tail, and the relay path.
- **S5. Unix `Mapping` stores a full `Options` clone plus `directory` and `pathname`** (`unix.rs:50-57`), and `Shared` stores `options` too. Keep only the name and lease directory in `Mapping`, or pass what `Drop` needs from `Shared`.
- **S6. Go:** remove the duplicate capacity validation (`interprocess.go:68-70`; Rust validates, and the messages differ). Replace the rune loop with `strings.IndexByte(s, 0) >= 0`. Replace `make` + `copy(unsafe.Slice…)` with `C.GoBytes(unsafe.Pointer(buffer.data), C.int(buffer.length))` (`:185-186`). Once the C API exposes error kinds (3.1), the five shims can share one pattern that returns `{status, kind, error}`.
- **S7.** The `options(name, capacity, path)` helper is duplicated in the C, Python, and Node wrappers (`src/c/src/lib.rs:31-42`, `src/python/src/lib.rs:12-18`, `src/node/src/lib.rs:8-14`). A core `Options::with_path_opt(Option<impl Into<PathBuf>>)`, or accepting `Option` in `with_path`, removes all three.
- **S8.** `Lease::new` returns `Error::Invalid("participant registration is already in use")` (`unix.rs:143`, `windows.rs:145`). This is not caller input: use `Corrupt` or a dedicated variant.
- **S9. CI duplication:** `cargo test -p cloudtoid-interprocess` runs in `native.yml` (3 OS) and again inside `tests/interop/build.py` (4 OS, including the 20 s crash tests). Pass a flag to `build.py` to skip it in the interop workflow, or drop it from `native.yml`.
- **S10.** The "Queue lifetime" paragraph is copied verbatim into 5 package READMEs, the root README, PACKAGE_README, and the website. Consider one canonical paragraph in `docs/protocol.md` and short links elsewhere, or accept the duplication for package pages but add a CI grep that keeps the copies identical.

---

## 6. Tests to add

### Rust core (`src/rust/src/queue/tests.rs`)
- **Full layout pinning (with S1):** gate at 128, slot size 128, `active` at slot+8, Windows capacity at 32, record header (state at 0, length at 4).
- **Golden bytes:** send `b"abc"` into a capacity-64 queue. Assert the raw bytes at `BUFFER_OFFSET` (`state=2`, `len=3`, payload, zero padding) and that all 16 bytes are zero after receive. This protects .NET compatibility beyond struct offsets.
- **`Send + Sync` static assertions** for `Publisher` and `Subscriber` (`fn assert_sync<T: Sync>() {}`).
- **Recovery discards completed messages between the abandoned head and the captured tail**, but keeps messages beyond the tail. The protocol claims both; only the second is covered.
- **Reader killed while holding a claimed record** (state 1 at head, owner = dead id): repair, then recovery after 10 s.
- **Full-table reclamation of a dead publisher:** 2047 live publishers plus 1 killed child publisher, then a new `Publisher::open` succeeds (pass 2 of `slot()`). The current test only drops a live publisher.
- **Lease probe "unknown means alive":** `chmod 000` a live lease file (skip if root), and assert that `Lease::alive` returns true and never deletes the file.
- **Two blocked receivers and two messages:** both wake well before the 5 ms polling would explain it. This exercises the relay path. Also: one receiver consumes a permit, gets cancelled by timeout, and the other still wakes.
- **`try_receive_into(&mut [])` consumes a message** and returns `Some(0)`.
- **Name validation:** `""`, `"."`, `".."`, `"a/b"`, `"a\\b"`, NUL, and long names (1.4), including "no stale `.qu` left behind".
- **Last-close cleanup:** after the final drop, the `.qu` file, `readers/N`, and the semaphore are gone (`sem_open` without `O_CREAT` fails with ENOENT).
- **Corrupt length** (negative, and larger than `used`) returns `Error::Corrupt` without clearing neighbouring records (after 1.6).
- **Ordering:** 1 subscriber, 4 publishers, per-publisher sequence numbers strictly increasing. `concurrent_exactly_once` checks uniqueness only.
- **Relay error does not lose a message (1.2).** Needs a signal injection seam, for example a `#[cfg(test)]` failing `Signal`.
- **Fork (1.5)**, Unix only.
- **Optional:** `cargo +nightly test -Zsanitizer=thread` in a scheduled job.

### C ABI (new Rust integration test in `src/c`, or a small C test built by CMake/ctest)
- Null `name`, `output`, `handle`, and `copied` each return -1 with a non-empty `cip_last_error()`.
- `timeout_ms < -1` returns -1; `data == NULL && length > 0` returns -1; a zero-length send with `NULL` data succeeds.
- `cip_buffer_free` on an empty successful message and on a zeroed `cip_buffer` is safe.
- `cip_last_error` is per-thread: an error on thread A does not change thread B's message.
- Static-link build on Windows once `CIP_STATIC` exists (3.3.1).
- Install-tree smoke test with no loader env vars (2.1).

### Python
- Signal during `receive()` does not lose a message (1.1).
- `bytearray`, `memoryview`, and numpy inputs; `pathlib.Path` for `path` (after 3.5).
- Exception types for capacity mismatch, invalid options, and a closed endpoint (after 3.1).
- `close()` from another thread while `receive(timeout=None)` blocks (after 3.5.4).
- GIL release: a second Python thread makes progress while `receive()` waits.
- Fork (1.5).
- A run on the minimum (3.9) and newest Python in CI (2.3).

### Node
- `Uint8Array` input to `trySend` and `trySendBatch` (verify 3.6.1).
- Capacity-mismatch error and `code` (after 3.1).
- `using` / `Symbol.dispose` (after 3.6.4).
- Idle CPU guard: 100 pending receives for 2 s stay under an agreed CPU budget (after deciding 3.6.2).
- Use from `worker_threads` (separate endpoints per worker on the same queue).
- CI on Node 18/20 or a raised `engines` (2.3).

### Go
- Many goroutines (for example 64) in `Receive` concurrently on one `Subscriber` under `-race`, with exactly-once delivery.
- `errors.Is(err, ErrCapacityMismatch)` and the other sentinels (after 3.1).
- `TryReceiveInto` and `TrySend` after `Close` return `ErrClosed`. Use `errors.Is` in the existing test, not `!=`.
- An empty message through `Receive(ctx)` returns a non-nil empty slice.
- `Example` tests (3.4.3).
- Thread-count guard: 500 goroutines blocked in `Receive` for 1 s must not create about 500 OS threads (after deciding 3.4.2).

### Cross-language (`tests/interop`)
- **Crash and recovery across implementations.** Today only Rust processes are killed (`mixed.py`), so a native subscriber never probes a **.NET-written lease**. Add `hold-publisher` and `hold-subscriber` modes to `tests/interop/dotnet/Program.cs` and kill .NET victims as well. On Windows this validates the PID plus .NET-ticks lease format in the Rust→.NET direction (`windows.rs:120-133` vs `ReaderLease.cs`).
- **Dead reader-owner repair across languages:** a Rust `crash_child`-style process sets itself as reader owner, closes the gate, and is killed. Each other language's subscriber must reopen the gate. Also do the reverse with a .NET test hook.
- **Capacity mismatch across languages:** .NET opens 4096, each native binding opens 8192 and must fail cleanly, and the reverse. This covers the Unix file-length check and the Windows offset-32 check.
- **Last-participant cleanup across languages:** .NET opens, a native endpoint joins, .NET closes, the native endpoint closes last. Verify the file, lease directory, and semaphore are removed. Then reopen with a *different* capacity and expect success.
- **Payload boundaries in the pair matrix:** add a 0-byte message and a `capacity − 8` (4088-byte) message to the sequence. The current range is 8–258 bytes.
- **Name-length error parity:** a 25-byte name on macOS gives a clear validation error in every binding and .NET, with no stale file.

---

## 7. Verified OK (no action needed)

- Header offsets (0/8/16/24/28), `BUFFER_OFFSET = 262400`, record padding `(L+15)&~7`, and the capacity check `used > C - len` match .NET `Publisher.TryEnqueueCore`.
- Publisher registration order (lease → slot CAS → reset active) and release order (slot CAS → lease) match .NET `PublisherRegistry` and `PublisherLease`. The Rust `Drop` runs before field drops, so the slot is released before the lease.
- Subscriber empty/owner/CAS ordering, pending-observation keying on `read`, gate close → `any_active` → clear through the captured tail, and dead-reader repair (CAS dead→self, then open the gate) match .NET `Subscriber.TryDequeueImpl` and `TryRecoverReader`.
- `Shared` drops `signal` before `mapping`, satisfying "close semaphore and unmap before releasing the lifetime lock". Unix open and close coordination under the directory `flock` matches `MemoryFileUnix`.
- On Linux a failed nonblocking `LOCK_SH→LOCK_EX` upgrade drops the shared lock. This only happens in `Mapping::drop` under coordination, right before the file closes, so it is harmless.
- Windows init-mutex flow and capacity-at-offset-32 handling match `MemoryFileWindows`.
- `Subscriber: Sync` is sound: `pending` is only touched while `reader == self.id`, and a second thread on the same subscriber cannot CAS `0→id` while the first holds it.
- Rust std opens files `O_CLOEXEC`, so exec'd children do not inherit locks. Only raw `fork` without exec is a problem (1.5).
- The Go shim copies the thread-local error before returning across cgo, which correctly handles goroutine migration.
- Node abort semantics (pre-aborted signal never consumes, reason preserved, close rejects pending receives) are well tested.


## Disposition after code review (September 14)

Implemented the correctness fixes in 1.1–1.6: notification failures no longer replace committed results; Python handles signals between receive attempts and permits concurrent close; names are validated before opening resources; forked-child cleanup preserves parent registrations; corrupt lengths are checked against reserved bytes. Batch send also preserves a committed prefix if a lifetime counter exhausts mid-batch.

Settled the unpublished APIs: Rust uses borrowed options, `recv` names, `try_send -> Result<()>` with `Error::Full`, non-exhaustive options/errors, and endpoint `Debug`. C exposes named statuses and thread-local error kinds. Go exposes `errors.Is` sentinels and waits with Go timers instead of blocking cgo threads. Python accepts buffer objects and PathLike, includes typing, and raises specific exceptions. Node accepts Uint8Array, validates safe-integer capacity, exposes error codes and disposal, and has an exports map. The Go test driver is internal. NuGet's README is now `src/dotnet/README.md`.

Packaging now gives the macOS dylib an `@rpath` install name and the SDK relocatable pkg-config metadata, including multi-level lib directories. Interop builds run without Unix loader environment variables. Linux publishing enforces glibc 2.34 for wheels and native binaries. CI lints the workspace, checks Rust 1.87, and exercises the built bindings on Python 3.9/3.14 and Node 18/20/24. README examples compile as a Rust doctest. Website examples and package guides follow the new APIs.

Added targeted tests for notification failures, corrupt records/golden bytes, invalid names, batch exhaustion, C errors/empty buffers, Python buffers/paths/close/signals/fork, Node typed inputs/errors/disposal, Go competing receives/error kinds, and .NET name validation. Mixed-language stress now kills .NET as well as Rust participants. The macOS semaphore test helper now handles raw semaphore names before constructing queue options.

Intentionally deferred the optional larger API additions: async runtimes/native waiter threads, async iterators, new receive-into/batch methods across all bindings, finalizers, and new CMake/static-SDK distribution surfaces. Explicit close and existing buffer APIs remain the documented contract. Keep protocol offsets and admission logic as they are rather than broad layout/algorithm rewrites; golden-byte tests protect compatibility. Keep self-contained package lifetime documentation. Intel Mac artifacts remain cross-built without a permanent Intel CI runner; execution under Rosetta is not added in this pass. Platform and additional fault-injection tests beyond those listed above remain possible follow-ups, not claims of completed coverage.
