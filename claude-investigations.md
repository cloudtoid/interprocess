# PR #57 review, round 2: multi-language core (`codex/multilanguage-core`)

**Snapshot:** HEAD `fd8a536`, clean working tree. I re-checked it after the last local changes. Since my code read, only two commits landed: `cc79468` (website link) and `fd8a536` (README-only). Both are accounted for below. Round 1 reviewed `56a3835`. The implementing agent's round-1 disposition is kept verbatim at the end, and items it deliberately deferred are marked ➖ below rather than re-opened.

**What I ran (macOS arm64; build outputs went to the scratchpad):**
- `cargo fmt --check` and `cargo clippy --workspace --all-targets --release -D warnings`: clean.
- `cargo test -p cloudtoid-interprocess`: 16 passed, 1 ignored (the fault-injection helper), and the README doctest passed.
- `cargo test -p cloudtoid-interprocess-ffi`: 2 passed.
- `-W missing_docs` on the core crate: no warnings.
- C SDK via CMake: the installed dylib's install name is `@rpath/libcloudtoid_interprocess.dylib`, and the `.pc` has `-Wl,-rpath,${libdir}`.
- `go vet` clean. `go test -race` passed against that SDK with **no** `DYLD_LIBRARY_PATH`.
- Python `test_api.py`: 5 passed against the freshly built `target/wheels/*.whl` in a Python 3.9 venv. I also ran edge-case probes.
- Idle-CPU measurements of Go `Receive` and Rust `recv_timeout` (2.1).

**Not run:** Node (no `node` binary here), Windows, Linux, the interop matrix, release workflows.

---

## 1. Status of round-1 findings

Legend: ✅ fixed · 🟡 partial · ⬜ open · ➖ deferred by the implementer's decision

### Correctness (all fixed)
| # | Finding | Status | Evidence |
|---|---|---|---|
| 1.1 | Python `receive()` lost a message on a pending signal | ✅ | Signals are checked before the wait, and after it only when no message arrived (`src/python/src/lib.rs`, `receive`). No regression test for this exact race (4.3). |
| 1.2 | Relay error overwrote a delivered message | ✅ | `notify()` is infallible. Covered by `notification_errors_preserve_committed_results`. |
| 1.3 | Error after a committed send; batch count lost | ✅ | Same fix. Batches also keep the committed prefix on a mid-batch error (see 2.6 for the doc gap). |
| 1.4 | macOS long names: misleading error and stale file | ✅ | Rust and .NET validate 24 bytes on macOS and 245 on Linux. Every failure after the first opener's exclusive lock (prepare, `LOCK_SH`, `mmap`) removes the file. Documented in `protocol.md` and the READMEs. Tested in Rust and .NET. |
| 1.5 | Forked child could unlink the parent's queue or lease | ✅ | PID guards in `Mapping::drop`, `Lease::drop`, and `Publisher::drop`. Documented in the Rust, C, and Python READMEs. Python fork test. |
| 1.6 | Record length not checked against `used` | ✅ | `next <= write` check. Covered by `golden_record_and_corrupt_lengths`. |

### Release and packaging
| # | Finding | Status | Notes |
|---|---|---|---|
| 2.1 | Absolute dylib install name | ✅ | `src/c/build.rs` plus the `.pc` rpath. The interop build clears loader env vars and links the C driver through `pkg-config`. Verified locally. Linux `.so` still has no SONAME (low, ⬜). |
| 2.2 | glibc floor | ✅ | `maturin --compatibility manylinux_2_34` and `tests/interop/check_glibc.py`. npm Linux packages declare `libc: ["glibc"]`. |
| 2.3 | Runtime matrix | 🟡 | Python 3.9/3.12/3.14 × Node 18/20/24, Linux x64 only. Intel-mac execution under Rosetta ➖. |
| 2.4 | Workspace clippy | ✅ | `native.yml` also runs the ffi tests. |
| 2.5 | Crate metadata / MSRV | ✅ | `rust-version = "1.87"` plus an MSRV job. `docs.rs` target metadata ⬜ (low). |
| 2.6 | README doctest / docs | ✅ | Consider `#![warn(missing_docs)]` so docs stay complete (low). |
| 2.7 | Website | 🟡 | Examples are updated. `src/website/site.js:66` now pins guide links to commit `fd3bd38…` instead of `5f895e9…`, but that is still a pinned SHA that goes stale as the branch keeps changing. After a squash merge, the PR SHA is not on `main`. Use `tree/main/…` or a release tag. |

### API fit
| # | Finding | Status |
|---|---|---|
| 3.1 | Typed errors: C kinds, Go sentinels, Python classes, Node codes | ✅ (Python base class missing, 2.4) |
| 3.2.1–6 | Rust: `Error::Full`, `recv*` naming, `#[non_exhaustive]`, `&Options`, `Debug` | ✅ |
| 3.2.7 | Rust blocking `recv_into` | ➖ |
| 3.2.8 | Rust async guidance (`spawn_blocking`) | ✅ |
| 3.3.1 | C `CIP_STATIC` guard | 🟡 The guard exists, but the SDK installs only the shared library, and the static SDK is ➖. Remove the `staticlib` crate type or document that static linking is source-build only. |
| 3.3.2 / 3.3.6 / 3.3.7 | C named enums; bounded-timeout guidance; relocatable `.pc` | ✅ |
| 3.3.3–5 | C CMake package config, version API, `cip_try_send_batch` | ➖ |
| 3.4.1 / 3.4.3–5 | Go sentinels, docs and `Example`, no import alias, `internal/interop` | ✅ |
| 3.4.2 | Go thread pinning | 🟡 Fixed, but it moved the cost to idle CPU (2.1). |
| 3.4.6 | Go module path `…/src/go/v3` | ⬜ Still a decision to make before tagging. |
| 3.4.7–8 | Go finalizer, `TrySendBatch`, `ReceiveInto` | ➖ |
| 3.5.1–2 / 3.5.6 | Python buffer protocol, `PathLike`, stubs | ✅ |
| 3.5.4 | Close interrupts receive | 🟡 `Subscriber` ✅ (measured ~46 ms from close to raise). `Publisher` not converted (2.3). |
| 3.5.5 | Python `try_receive_into` | ➖ |
| 3.5.7 | Python `closed` / `__repr__` | ⬜ (low) |
| 3.6.1 / 3.6.3–4 / 3.6.7–9 | Node `Uint8Array`, codes, dispose, safe-integer capacity, loader error, `exports` | ✅ (not executed by me) |
| 3.6.2 | Node 1 ms polling | ➖ (see 2.1 for the measured cost) |
| 3.6.5 | Node async iterator | ➖ |

### Simplifications
| # | Status |
|---|---|
| S1 `#[repr(C)]` layout structs, S2 ring-split helper, S3 shared admission | ➖ The implementer chose to keep offsets and admission as-is; golden bytes guard compatibility. **Gap:** the gate offset (128), slot size (128), `active` offset (+8), and Windows capacity offset (32) are still not pinned by any test (4.1). |
| S4 infallible `notify` | ✅ |
| S5 Unix `Mapping` clones a full `Options` alongside `directory`/`pathname` (`unix.rs:50-58`) | ⬜ (low) |
| S6 Go: `IndexByte` done. Duplicate capacity check (`interprocess.go:97-99`) and manual `make`+`copy` instead of `C.GoBytes` (`:214-215`) remain. | 🟡 |
| S7 `options(name, capacity, path)` helper duplicated in C/Python/Node | ⬜ (low) |
| S9 core tests run in both `native.yml` and `tests/interop/build.py` | ⬜ (low) |
| S10 duplicated lifetime docs | ➖ |

---

## 2. New findings in this round

### 2.1 Idle polling CPU grows with the number of waiters (medium; measured). Worth reconsidering the deferral.
Go `Receive` (`src/go/interprocess.go:176-198`) loops a nonblocking cgo `TryReceive` and a 1 ms `time.Timer` **per waiting goroutine**. The Node wrapper has the same shape (a 1 ms timer per pending `receive()`). Measured on this M-series Mac, queue idle for 2 s:

| Waiters | CPU in 2 s |
|---|---|
| Go, 1 goroutine in `Receive` | ~60 ms (≈3% of a core) |
| Go, 64 goroutines in `Receive` | ~380 ms (≈19% of a core) |
| Rust `recv_timeout`, same machine | ~15 ms (≈0.8%) |

On macOS the native wait also polls `sem_trywait` about every 1 ms. Linux uses `sem_timedwait` **(verify)**.

The round-1 disposition deferred "native waiter threads" before this cost was measured. A long-lived idle worker pool pays it continuously, and it scales with concurrency.

**Cheap mitigations that add no API surface:**
- **Adaptive backoff:** 1 ms rising to ~10–20 ms while idle, resetting on delivery. About ten lines in Go `Receive` and in Node `receive`.
- **Or** one internal waiter per `Subscriber` (goroutine or native thread) that fans out to pending receivers. This is internal only; the public API stays the same.

If this stays deferred, finish documenting it. `src/node/README.md:25` now says pending timers add idle CPU work. `src/go/README.md:26` describes the 1 ms timer but not its CPU cost; add the per-waiter cost there and recommend one receive loop per subscriber. **Verify** Go timer granularity on Windows as well.

### 2.2 ~~Stale README statements~~ ✅ fixed in `fd8a536`
The Go README now describes the 1 ms Go timer instead of "native waits". The Python README says `try_send_batch` takes buffer objects and that closing a subscriber interrupts receive. Remaining nit: the Python README doesn't mention that `Publisher.close()` can still hit PyO3's "Already borrowed" error in the PEP 688 case (2.3).

### 2.3 Python `Publisher` still uses `&mut self` for close (low) (verify)
`Subscriber` is `#[pyclass(frozen)]` with `RwLock<Option<…>>`, but `Publisher` keeps `close(&mut self)` and `__exit__(&mut self)`. `try_send` normally holds the GIL throughout. The exception is a Python 3.12+ object implementing `__buffer__` (PEP 688): `memoryview(obj).tobytes()` runs Python code that can switch threads, and a concurrent `close()` then raises PyO3's "Already borrowed" `RuntimeError` instead of `ValueError: endpoint is closed`. Not testable here (Python 3.9 only).
**Fix:** use the same frozen + `RwLock` pattern for consistency.

### 2.4 Python exceptions share no library base class (low; cheap now, breaking later)
`CapacityMismatchError(ValueError)`, `PublisherLimitError(RuntimeError)`, and `CorruptQueueError(RuntimeError)` cannot be caught together. Add `InterprocessError(Exception)` and inherit from it as well, for example `class CapacityMismatchError(InterprocessError, ValueError)`, in both `lib.rs` and `__init__.pyi`.

### 2.5 Rust `Error::Full` retry loops are verbose (low, ergonomics)
`examples/interop.rs:39-46` and `queue/tests.rs:126-133` need:
```rust
while publisher.try_send(&data).map(|()| false).unwrap_or_else(|e| match e { Error::Full => true, e => panic!("{e}") }) { … }
```
The shorter `while matches!(p.try_send(&d), Err(Error::Full))` that 2356b8d used silently treats every other error as success, and that trap is easy for users to fall into.
**Fix:** add `impl Error { pub fn is_full(&self) -> bool }`, and show the correct retry loop in the Rust README.

### 2.6 `try_send_batch` docs don't describe its error semantics (low)
`queue.rs:249-275`:
- A mid-batch error is swallowed when `sent > 0` (`Err(_) if sent > 0 => break`) and surfaces on the next call.
- A closed gate returns `Ok(0)` rather than `Err(Full)`.

Neither behavior is documented on the method. In C, Go, and Node the short count also looks exactly like "full". Add a doc sentence: "Returns the committed prefix length; a short count means full, recovery, or an error that the next call reports." Mirror it in the Python and Node docs.

### 2.7 Node typings require TypeScript ≥ 5.2 (low) (verify)
`src/node/index.d.ts:1` has `/// <reference lib="esnext.disposable" />`. With TypeScript < 5.2 that lib doesn't exist, so type-checking fails even for users who never write `using`. Either document `typescript >= 5.2`, or declare the symbol through a small `declare global` shim.

### 2.8 `Error::Corrupt` message is misleading for lease collisions (low)
`Lease::new` returns `Error::Corrupt` when a lease already exists (`unix.rs`, `windows.rs`), but `Display` says "invalid shared-memory message header" (`lib.rs:114`). The variant doc already says "invalid record or registration"; make the message generic too ("corrupt or inconsistent shared queue state").

### 2.9 Go nits (low)
- `stringsFor` (`interprocess.go:96-102`) returns bare `ErrInvalidArgument` with no detail. Wrap it with a message, or drop the capacity check and let the core's descriptive error through.
- The cgo shim calls `cip_last_error_kind()` on every successful hot-path call (`interprocess.go:14`). Move it inside `if (status < 0)`.
- `example_test.go` uses the fixed queue name `"go-example"`. Concurrent `go test` runs on one machine share it, so the `// Output:` check can flake.
- `interprocess_test.go:56` still uses `err != ErrClosed`; use `errors.Is`.

### 2.10 Python copies contiguous non-`bytes` buffers (low, performance)
`bytes()` in `src/python/src/lib.rs` always calls `tobytes()`. For C-contiguous `bytearray`, `memoryview`, and numpy inputs, `pyo3::buffer::PyBuffer<u8>` can pass the slice directly while the GIL is held. Keep `tobytes()` for noncontiguous views.

---

## 3. Remaining decisions before the first release
1. **Idle-wait design** for Go and Node (2.1): backoff or an internal waiter, or document the cost.
2. **Go module path** `…/src/go/v3` (3.4.6). It can't change cheaply after tagging.
3. **Website guide link:** use `main` or a release tag instead of a PR commit SHA (1 / 2.7).
4. **Doc fix:** 2.6 (batch error semantics), plus the Go idle-cost note in 2.1.
5. **Cheap API polish** that would be breaking later: Python base exception (2.4), Rust `Error::is_full` (2.5; additive, so optional before release).

Cross-binding table (current):

| Capability | Rust | C | Python | Node | Go | .NET |
|---|---|---|---|---|---|---|
| batch send | ✓ | ✗ ➖ | ✓ | ✓ | ✗ ➖ | ✗ |
| try-receive into caller buffer | ✓ | ✓ | ✗ ➖ | ✗ | ✓ | ✓ |
| blocking receive into buffer | ✗ ➖ | ✗ | ✗ | ✗ | ✗ ➖ | ✓ |
| close interrupts a pending receive | n/a (borrow) | use timeouts | ✓ Subscriber | ✓ | ✓ | ✓ |
| typed error kinds | ✓ | ✓ | ✓ (no base) | ✓ | ✓ | exceptions |
| idle waiter cost | native wait | native wait | native wait, 100 ms slices | 1 ms JS timer per waiter | 1 ms timer + cgo call per waiter | native wait |

---

## 4. Tests still worth adding

Added since round 1 (no action needed):
- **Rust:** golden record bytes, `Send`/`Sync`, name validation with no leftovers, notification-failure injection, batch exhaustion, `try_recv_into(&mut [])`, corrupt lengths.
- **C ABI:** null arguments, per-thread errors, empty buffers, `Full` → 0.
- **Python:** buffers and paths, exception types, cross-thread close, fork, a signal handler closing a waiting subscriber.
- **Node:** typed arrays, codes, dispose, invalid capacities.
- **Go:** error kinds, 64 competing receivers, empty message, `ErrClosed`, `Example`.
- **.NET:** name validation.
- **Cross-language:** .NET and Rust victims killed in `mixed.py`, glibc symbol check, drivers linked with no loader env vars.

The implementer framed further fault-injection and platform tests as follow-ups, not claimed coverage. In priority order:

### 4.1 Rust core
- **Pin the remaining protocol offsets:** gate at 128, slot size 128, `active` at slot+8, Windows capacity at 32. These are raw pointer arithmetic with no test, and golden record bytes don't cover them. This is the cheap substitute for S1.
- **Recovery discards completed messages between the abandoned head and the captured tail**, and keeps messages beyond the tail.
- **Reader killed holding a claimed record** (state 1 at head): repair, then recovery.
- **Full-table reclamation of a dead publisher:** 2047 live publishers plus 1 killed child, then `Publisher::open` succeeds.
- **Lease probe "unknown means alive":** `chmod 000` a live lease file; `Lease::alive` stays true and does not delete it.
- **Relay wakes a second blocked receiver** without relying on the 5 ms poll.
- **Last-close cleanup:** `.qu` file, `readers/<name>`, and semaphore removed.
- **Per-publisher ordering** with one subscriber.
- **A Rust-level fork test** (the guard lives in Rust; C and Rust users rely on it too).

### 4.2 Cross-language
- **Dead reader-owner repair across implementations.** The current victims never own the reader lock or a reservation.
- **Capacity mismatch across implementations** in both directions. This covers the Unix file length and the Windows offset 32.
- **Last-participant cleanup across implementations**, then reopening with a different capacity.
- **Maximum payload** (`capacity − 8` = 4088 bytes) in the pair matrix.

### 4.3 Bindings
- **Python:** a 1.1 regression test, where a message arrives and a signal handler raises in the same wait slice; assert the message is returned or still queued. Also a PEP 688 `__buffer__` exporter on 3.12+ racing `close()` (2.3).
- **Node / Go:** an idle-CPU or wakeup-count guard once 2.1 is decided. Node `worker_threads` usage. TypeScript < 5.2 type-check if 2.7 stays.

---

## 5. Verified OK this round
- Every post-commit notification path ignores post failures, and `receive_wait` always returns its result. The injected failures cover both the publisher and relay sides.
- Fork handling:
  - In a forked child, `Publisher::drop` skips slot release.
  - `Mapping::drop` only unmaps and closes the descriptor.
  - `Lease::drop` keeps the lease file.
  - Closing an inherited descriptor does not release the parent's `flock`, because the parent's file description stays open.
- First-opener failure paths (prepare, `LOCK_SH`, `mmap`) remove the `.qu` file they created while still exclusive.
- `try_recv` loads `write` under reader ownership and rejects `read + length > write` without clearing; state stays at 2.
- The C ABI maps `Full` to 0 and other errors to kinds 1–7; the kind is per-thread (tested).
- Python `Subscriber.close()` takes the write lock between the receiver's 100 ms slices. A signal handler calling `close()` runs outside the locked region, so there is no deadlock (tested; ~46 ms measured).
- Go `Receive` no longer blocks in cgo, so waiting goroutines do not pin OS threads.
- .NET `QueueOptions` name rules and limits match Rust's.
- The runtime-matrix job restores the tested Linux x64 artifact layout (`target/wheels`, `src/node/*.node`) at the repo root.

---

## Disposition after code review (September 14), recorded by the implementing agent after round 1 (kept verbatim)

Implemented the correctness fixes in 1.1–1.6: notification failures no longer replace committed results; Python handles signals between receive attempts and permits concurrent close; names are validated before opening resources; forked-child cleanup preserves parent registrations; corrupt lengths are checked against reserved bytes. Batch send also preserves a committed prefix if a lifetime counter exhausts mid-batch.

Settled the unpublished APIs: Rust uses borrowed options, `recv` names, `try_send -> Result<()>` with `Error::Full`, non-exhaustive options/errors, and endpoint `Debug`. C exposes named statuses and thread-local error kinds. Go exposes `errors.Is` sentinels and waits with Go timers instead of blocking cgo threads. Python accepts buffer objects and PathLike, includes typing, and raises specific exceptions. Node accepts Uint8Array, validates safe-integer capacity, exposes error codes and disposal, and has an exports map. The Go test driver is internal. NuGet's README is now `src/dotnet/README.md`.

Packaging now gives the macOS dylib an `@rpath` install name and the SDK relocatable pkg-config metadata, including multi-level lib directories. Interop builds run without Unix loader environment variables. Linux publishing enforces glibc 2.34 for wheels and native binaries. CI lints the workspace, checks Rust 1.87, and exercises the built bindings on Python 3.9/3.14 and Node 18/20/24. README examples compile as a Rust doctest. Website examples and package guides follow the new APIs.

Added targeted tests for notification failures, corrupt records/golden bytes, invalid names, batch exhaustion, C errors/empty buffers, Python buffers/paths/close/signals/fork, Node typed inputs/errors/disposal, Go competing receives/error kinds, and .NET name validation. Mixed-language stress now kills .NET as well as Rust participants. The macOS semaphore test helper now handles raw semaphore names before constructing queue options.

Intentionally deferred the optional larger API additions: async runtimes/native waiter threads, async iterators, new receive-into/batch methods across all bindings, finalizers, and new CMake/static-SDK distribution surfaces. Explicit close and existing buffer APIs remain the documented contract. Keep protocol offsets and admission logic as they are rather than broad layout/algorithm rewrites; golden-byte tests protect compatibility. Keep self-contained package lifetime documentation. Intel Mac artifacts remain cross-built without a permanent Intel CI runner; execution under Rosetta is not added in this pass. Platform and additional fault-injection tests beyond those listed above remain possible follow-ups, not claims of completed coverage.

## Implementer follow-up: round 2 (September 14)

The findings above describe their stated snapshot. The following records the later decisions and evidence rather than rewriting the reviewer's observations.

- **2.1, idle receives:** implemented the user's chosen adaptive backoff (1, 2, 4, 8, then 10 ms) in Go and Node, documented the latency tradeoff, and added retry-count guards. Local Node measurements with 100 pending receives fell from about 429 ms to 59 ms CPU over two seconds. Go measured about 12 ms with one waiter and 67 ms with 64. These are diagnostic samples, not published throughput benchmarks. A separate Go prototype with one native waiter per subscriber passed race tests and reduced median idle wakeup from roughly 9 ms to 0.8 ms on this Mac; it costs an OS thread per actively waiting subscriber and adds request/cancellation lifecycle state. It is not integrated. macOS's bounded native semaphore wait still polls; Linux and Windows use OS waits. The approved backoff remains the current implementation while notification-driven alternatives are evaluated.
- **2.3–2.9:** fixed Python publisher locking (convert user buffers before acquiring the lock), added the common `InterprocessError` base while preserving built-in exception categories, added Rust `Error::is_full`, documented committed batch-prefix semantics, documented TypeScript 5.2 minimum, broadened the corrupt-state message, and fixed the Go error-path/argument/example nits. A Python 3.12 PEP 688 exporter racing close now has a regression test. TypeScript 5.2.2 compiles the public API including `using`; CI now checks that minimum version.
- **2.10:** retain the direct `bytes` path and copy other buffer objects. PyO3's `PyBuffer` module is unavailable under our `abi3-py39` configuration (its stable-ABI support requires Python 3.11). Requiring newer Python or producing separate interpreter wheels would expand the compatibility/packaging contract. Do not introduce unchecked buffer FFI to avoid this copy.
- **Packaging and documentation nits:** added Linux SONAME, docs.rs targets, and `warn(missing_docs)`; documented static C linking as source-build only. Removed the duplicated stable core test invocation from `native.yml`; the platform interop jobs and separate MSRV job retain core execution. NuGet's guide remains in `src/dotnet/README.md`.
- **Go module path:** keep `github.com/cloudtoid/interprocess/src/go/v3`. It follows the existing monorepo source directory and uses the required major-version suffix; changing directory layout solely to shorten an import is not necessary. Tags must retain the `src/go/v3.0.0` module prefix already used by release automation.
- **S5–S7:** retain the small cold-path Options clone for lifetime cleanup and language-local options conversion helpers. Sharing those would couple language-specific validation to a new common API. Retain Go's `make` plus `copy`: it avoids narrowing a buffer length to the C `int` accepted by `C.GoBytes`. These are deliberate simplicity decisions, not unreviewed findings.
- **Website guide links:** the public site must not link to main paths that do not exist until merge. Keep reviewed-source links during the PR; switch to main after merge. This remains a release follow-up. Optional Python representation properties, iterators, finalizers, additional batch/receive-into APIs, CMake config packages, and layout rewrites remain deferred as in round 1.

### Added coverage and remaining test limits

Added actual layout-address assertions for the gate, slot stride, active count, and Windows capacity; captured-tail discard/preservation; claimed-record reader recovery; reclamation with 2047 live publishers and one dead publisher; unknown lease liveness; last-close resource cleanup; and per-publisher ordering. The fork regression now closes a single inherited endpoint and checks the parent's real lease remains, exercising the Rust cleanup code through Python.

All 36 ordered language pairs now include a 4088-byte payload. New lifecycle tests reject mismatched capacity through all six bindings, exercise Rust and .NET creators and both implementations as the last participant, and reopen an empty queue with a different capacity. These tests passed on macOS, Linux x64, and Linux ARM64 at `96f6810`. Python 3.9/3.12/3.14 and Node 18/20/24 compatibility jobs also passed on Linux.

Windows teardown investigation: external received buffers intermittently triggered `UV_HANDLE_CLOSING` in Node 24.20.0, including after explicit endpoint close. Plain Node, addon loading, and empty messages passed; disabling concurrent array-buffer sweeping also passed. Node 24.20.0 already included upstream nodejs/node#61999. The fix at `8a1f8f8` uses Node-owned receive buffers on Windows, avoiding external-buffer finalizers; Unix retains external buffers. Full CI passed on all four platforms, including repeated process/worker teardown, 36 language pairs, mixed stress, lifecycle checks, and cross-implementation reader recovery. The Linux Python/Node runtime matrix also passed. Temporary plain-Node/engine-flag diagnostic controls were removed; empty-message, explicit-close, and ordinary teardown regression cases remain.

A separate local Mac comparison of the two buffer strategies measured Node-owned buffers about 14–17% faster at 3 B, 50 B, and 1 KiB despite the copy. This does not establish Windows throughput, and no published benchmark table was changed.

The mixed-language kill test still kills registered endpoints outside an in-flight operation. The new `recovery.py` additionally injects a claimed record under a real, quiescent victim lease, kills the victim, and verifies repair plus subsequent delivery in both Rust-to-.NET and .NET-to-Rust directions. Both directions passed on macOS, Linux x64/ARM64, and Windows CI. A timing-independent relay-wakeup test remains a coverage gap; the Rust core has direct fault-injection coverage. A Python handler can raise after the native call returns but before Python assigns its result, so a test demanding atomicity across both steps would promise semantics this synchronous API cannot provide. The native implementation no longer explicitly checks signals after consuming a message, and the existing signal-handler-close test covers reentrant cleanup. Intel Mac execution remains unverified; no permanent Intel CI job was added.
