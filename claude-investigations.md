# PR #57 review, round 3: simplification pass (`codex/multilanguage-core`)

**Snapshot:** HEAD `0222241`, clean tree. This replaces the round-1 and round-2 reports. Everything those rounds raised is either fixed or recorded as a decision in section 7. This round focuses on PR size, dead code, and simplification, plus a few remaining correctness items.

**What I ran (macOS arm64; build outputs went to the scratchpad):**
- `cargo fmt --check` and `cargo clippy --workspace --all-targets --release -D warnings`: clean.
- `cargo test -p cloudtoid-interprocess -p cloudtoid-interprocess-ffi`: 20 core tests passed (1 ignored helper), 2 C ABI tests passed, and 2 doctests passed.
- C SDK built and installed with CMake. Against it, `go vet` was clean and `go test -race` passed with no loader env vars.
- Python `test_api.py` against the current wheel on Python 3.9: 6 passed, 1 skipped (the 3.12-only PEP 688 test).
- Go idle CPU with the new backoff:

  | Waiters | CPU in 2 s, before | CPU in 2 s, now |
  |---|---|---|
  | 1 goroutine | ~60 ms | **~14 ms** |
  | 64 goroutines | ~380 ms | **~70 ms** |

  This confirms the implementer's numbers.

**Not run:** Node (no `node` here), Windows, Linux, the interop scripts, release workflows.

> **Before merge:** delete `claude-investigations.md` from the branch. It is review scaffolding (currently 247 added lines in the PR) and should not ship in the repository.

---

## 1. PR size

`main...HEAD` touches **205 files, +8,231 / −1,246** (renames counted separately). Roughly:

| Area | Added lines |
|---|---:|
| `src/rust` (core, tests, examples) | 2,035 |
| `src/website` (incl. 1,244-line vendored `highlight.min.js`) | 1,879 |
| `src/go`, `src/node`, `src/c`, `src/python` | 2,133 |
| `tests/interop` | 551 |
| `Cargo.lock` | 410 |
| workflows (`interop`, `native`, `release-native`, `website`) | 362 |
| root `README.md` rewrite, `docs/protocol.md`, `docs/releasing.md` | 413 |
| .NET move to `src/dotnet` (82 pure renames) + `.gitignore`/`.vscode` | ~0 net |
| `docs/benchmarks` deletion | −731 (see 4.3) |

The removals and simplifications below are the main ways to shrink it.

---

## 2. Dead or removable code

### 2.1 Node: the Windows-only receive-buffer branch is now pure complexity (medium)
`src/node/src/lib.rs:93-111` copies received messages into Node-owned buffers on Windows (`BufferSlice::copy_from`) and hands over the Rust `Vec` on Unix (`BufferSlice::from_data`). The implementer's own note at `0222241` reports the copying strategy measured **14–17% faster** on macOS at 3 B, 50 B, and 1 KiB. Keeping two ownership models therefore buys nothing on Unix, while doubling the teardown surface to reason about (the `UV_HANDLE_CLOSING` failure was tied to external-buffer finalizers).
**Fix:** use `copy_from` on every platform and delete the `#[cfg]` split and its comment. The explicit-close and empty-message teardown regression tests keep guarding it.

### 2.2 Python: redundant exception re-exports from `_native` (low)
The exception types are defined in `cloudtoid_interprocess/_exceptions.py` and imported into Rust via `import_exception!` (`src/python/src/lib.rs:13-15`). `_native` then re-adds them as module attributes (`lib.rs:191-199`). `__init__.py` imports them from `_exceptions`, not `_native`, so nothing uses the `_native` attributes. Delete the three `m.add(...)` calls.

### 2.3 Rust: redundant length pre-check in `try_send_batch` (low)
`queue.rs:254-256` scans the whole batch for messages longer than `i32::MAX` before admission. `send_admitted` performs the same check (`queue.rs:278-280`), and since batch errors after a committed prefix are now deliberately deferred to the next call (`queue.rs:268-271`), the pre-scan only changes *when* `Invalid` surfaces. Remove it, so that there is one validation site and one documented rule ("errors after a commit surface on the next call").

### 2.4 Node test: diagnostic matrix in the teardown test (low)
`src/node/test.js:112-157` spawns up to 36 child Node processes (3 cases × 12 runs, 5,000 round trips each) plus 4 workers. The investigation that needed several variants is closed (`0222241`). Keep one repeated case that sends a nonempty message and exits with endpoints still open, plus the worker case. Also reformat `test.js:159-175`: it is written in minified style (`let attempts=0;`, no spaces) unlike the rest of the file.

### 2.5 `src/c/build.rs` reads `CARGO_CFG_TARGET_OS` twice (nit)
Use a single `match std::env::var("CARGO_CFG_TARGET_OS").as_deref() { Ok("macos") => …, Ok("linux") => …, _ => {} }`.

### 2.6 Website hosting is duplicated (low)
The site is deployed two ways: GitHub Pages (`website.yml`) and "Sites" hosting (`src/website/.openai/hosting.json`, `package.json` whose only script runs `build.py`). `build.py` exists only to copy files into `dist/` for those pipelines. The page also vendors the full 127 KB `highlight.min.js` to color six static snippets.
**Fix:** pick one host and delete the other's scaffolding. Either pre-render the highlighted HTML at build time, or load a language-limited highlight.js build. The site.js guide link is still pinned to a PR commit (`site.js:66`, `fd3bd38…`); switch it to `main` after merge. This is already an agreed follow-up.

---

## 3. Simplifications (no behavior change)

### 3.1 Rust: let `send_admitted` return `Error::Full` directly
`send_admitted -> Result<bool>` predates `Error::Full`. Returning `Err(Error::Full)` for "no space" makes:
- `try_send` → `admit()?; self.send_admitted(message)` (no `.then_some(()).ok_or(Error::Full)`, `queue.rs:244-246`);
- `try_send_batch` → `match … { Ok(()) => sent += 1, Err(Error::Full) => break, Err(_) if sent > 0 => break, Err(e) => return Err(e) }`.

The public API stays the same, and the `bool` meaning "full" disappears internally.

### 3.2 Rust: move the `Debug` impls above `#[cfg(test)] mod tests;`
`queue.rs:576-591` sits after the test module declaration. Place it next to the types.

### 3.3 Bindings: one "full → false" helper per binding
Python (`lib.rs:74-78`) and Node (`lib.rs:56-60`) both spell out `.map(|()| true).or_else(|e| match e { Error::Full => Ok(false), e => Err(e) })`. With `Error::is_full` available, write it as `match r { Ok(()) => Ok(true), Err(e) if e.is_full() => Ok(false), Err(e) => Err(error(e)) }`. The C ABI already centralizes this in `call()`.

### 3.4 Docs: fix the README structure that accreted during review (low, but visible)
Every package README now ends with `## Queue lifetime` followed by unrelated paragraphs appended during fixes: name limits, batch semantics, TypeScript minimum, per-waiter cost. They render as if they were part of "Queue lifetime".
- `src/rust/README.md:30-40`: the retry-loop snippet's `while` block is not indented inside `fn send`.
- `src/python/README.md`: says closing a subscriber interrupts receive twice (the "Use context managers" paragraph and the later paragraph).
- `src/dotnet/README.md`: the name-limit paragraph sits after the footer link line.
- `src/go/README.md`: the "Native errors are copied before leaving cgo…" sentence is an implementation detail, not user guidance.

The implementer decided to keep each package's lifetime docs self-contained, so don't consolidate them across files. Within each file, order the sections: usage → errors → waiting/cancellation → limits (names, batch) → queue lifetime.

### 3.5 CI: the runtime matrix runs after a failed build (low)
`interop.yml` `runtimes` uses `if: ${{ always() && !cancelled() }}`, but it downloads `packages-Linux-X64`, which is uploaded only when the Linux x64 `interop` job reaches its final step. When that job fails earlier, all three runtime jobs fail with "artifact not found" and hide the real failure. Either use the default `needs` gating, or upload the artifact with `if: always()` directly after the wheel and `.node` builds.

### 3.6 `docs/releasing.md:17` is stale
It says the Native core workflow checks "crash recovery … on all three operating systems". `native.yml` no longer runs the core tests on stable: crash recovery runs in `interop.yml` (4 OSes) and the MSRV job. Update the sentence.

---

## 4. Remaining correctness and behavior items

### 4.1 .NET `QueueOptions` validation is a behavior change for a published package (medium)
`src/dotnet/Interprocess/Contracts/QueueOptions.cs` now rejects `.`/`..`, `/`, `\`, NUL, and names over 24 bytes (macOS) or 245 bytes (Linux).
- Most of these could never work: slashes break the file path, and long names fail in `sem_open`.
- A backslash is a valid Unix file and semaphore name, so .NET v3 users on Linux/macOS with `\` in a queue name **will now get `ArgumentException`** where they previously worked. Such names also can't interoperate with the Rust core, which rejects `\` on every platform.

Cloudtoid.Interprocess v3 is already on NuGet. **Fix:** ship this as a noted behavior change in the .NET release notes and version bump, or relax the backslash rule on Unix in .NET. Relaxing would keep compatibility but make those names .NET-only.

### 4.2 Node idle cost scales per pending receive (informational)
Node and Go now back off 1→10 ms (Go measured above). The remaining cost is proportional to concurrent waiters, and both READMEs now recommend one receive loop per subscriber. No action needed unless many-waiter workloads matter. The notification-driven per-subscriber waiter prototype stays a future option.

### 4.3 Root README performance claims lost their provenance (medium, docs)
The PR deletes `docs/benchmarks/**`, including the v1/v2/v3 comparison harness (`VersionBenchmarks.cs`, `Comparison.csproj`) and the per-platform reports. The root README still states "**12.0× faster round trips and 2.3× the concurrent throughput of v2**" (`README.md:198-208`), and still lists Windows and Linux VM results. Nothing in the repo reproduces or backs them now: main linked `docs/benchmarks/2026-09-13/` three times, and those links were removed rather than replaced. The v1 row is also just "— | —".
**Fix:** keep the archive (or at least `VersionBenchmarks.cs` plus the reports), or drop the version-comparison claims. Separately, the README cites Rust raw samples from `src/website/benchmarks/rust-macos.txt`, which couples the README to the website directory. Consider moving that file under `docs/`.

---

## 5. Tests: gaps that are still worth closing

These are coverage gaps only. Round 3 found no new correctness gaps.

- **Relay wakeup without the 5 ms poll.** The implementer noted this is hard to make timing-independent. One option: after sending two messages to two receivers blocked in `recv_timeout(Duration::from_secs(5))`, assert that `NotificationPending` goes 1 → 0 → 1 → 0 by inspecting the header between steps, rather than measuring latency.
- **Intel macOS artifacts are never executed.** This was decided (no permanent runner). Running the cross-built `.node`/wheel/dylib smoke test under `arch -x86_64` on the existing Apple Silicon runner in `release-native.yml` would cost minutes, not a runner.
- **Python PEP 688 close race** runs only on ≥3.12. The CI matrix includes 3.14, so it is covered there; no action.

---

## 6. Verified in this round
- Round-2 fixes are present and tested:
  - Python `Publisher` uses a frozen class plus `RwLock` and converts buffers before locking (PEP 688 test).
  - `InterprocessError` base class.
  - `Error::is_full`, and the batch prefix semantics are documented in the Rust, Python, and Node docs.
  - TypeScript 5.2 minimum is documented and checked in CI (`tests/interop/typescript.ts`).
  - Generic `Corrupt` message; Go error wrapping; `cip_last_error_kind` read only on the error path; PID-unique Go example name.
  - Linux SONAME; docs.rs targets; `#![warn(missing_docs)]`.
- New Rust tests exist and pass:
  - layout address pinning (gate, slots, `active`, Windows capacity);
  - captured-tail discard;
  - claimed-record reader recovery;
  - full-table dead-publisher reclamation;
  - unknown-lease liveness;
  - last-close resource removal;
  - per-publisher ordering;
  - batch exhaustion.
- Interop coverage added:
  - 4,088-byte payloads in all 36 pairs;
  - `lifetime.py` (capacity mismatch through all six bindings, Rust and .NET creators and final closers, reopening with a different capacity);
  - `recovery.py` (claimed-record repair across Rust ↔ .NET using real leases).

  The implementer reports all of these passing on all four CI platforms at `8a1f8f8`/`0222241`; I did not run them.
- Go backoff reduces idle CPU as claimed (see header).

---

## 7. Settled decisions (do not reopen without new evidence)
Recorded by the implementer across rounds 1–2:
- **Deferred API additions:**
  - native waiter threads and async runtimes;
  - async iterators;
  - receive-into and batch parity across bindings (C/Go batch, Python/Node receive-into, Rust blocking `recv_into`);
  - Go finalizers;
  - CMake package config, static SDK, and version API;
  - Python `closed`/`__repr__`.
- **Kept as-is:**
  - protocol offsets and admission logic (no `#[repr(C)]` layout rewrite, ring-split helper, or shared admission refactor), with golden bytes and address tests guarding them;
  - per-language options helpers;
  - Go `make`+`copy` instead of `C.GoBytes`;
  - the Unix `Mapping` options clone.
- **Go module path:** `github.com/cloudtoid/interprocess/src/go/v3`, tagged `src/go/v3.x.y`.
- **Python non-`bytes` buffers** are copied. `PyBuffer` is unavailable under `abi3-py39`, and unchecked buffer FFI is not acceptable.
- **Static C linking** is documented as source-build only.
- **Website guide links** stay pinned during the PR and switch to `main` after merge.
- **Lifetime documentation** stays self-contained in each package README.
- **Intel Mac artifacts** stay cross-built with no permanent Intel runner.
- **Python signals:** no test demands atomicity between a native receive returning and Python binding its result; the synchronous API cannot provide it.
