# Cloudtoid Interprocess protocol v3

This document specifies the shared-memory protocol used by the .NET implementation and the Rust core behind the C, Python, Node.js, and Go packages. Package API versions and the protocol namespace are separate: an API release can remain compatible with v3.

## Scope and guarantees

A queue connects processes on one machine. Publishers reserve space concurrently. Subscribers compete for messages; one shared reader owner serializes consumption. Delivery follows reservation order, even if a later reservation becomes ready first. Concurrent calls from one publisher have no defined relative order. Successful sequential calls preserve order. An accepted batch is a prefix of the input, not a transaction; other publishers can interleave reservations.

Ordinary operation delivers each message to one subscriber. This is volatile IPC, not durable storage, broadcast, or an acknowledged work queue. A reader can die after copying a message but before advancing the queue. Recovery can discard an abandoned reservation and completed messages behind it. Applications that need durable processing or exactly-once effects must implement those guarantees separately.

The enqueue reservation path uses native atomics, without a per-message OS mutex. The entire queue is not formally lock-free: consumption has a reader ownership word, an indefinitely paused live reader can block readers, and recovery waits for admitted live writers. Resource creation and destruction use OS locks. A blocking receive also uses an OS semaphore. Language wrappers may synchronize their own handle lifetime.

Supported participants must run on a little-endian, 64-bit architecture with naturally aligned, process-shared 32-bit and 64-bit atomics (x86-64 or ARM64). No 128-bit compare/exchange or `CMPXCHG16B` is required. Files containing this layout cannot be treated as durable queues across reboot, copied between machines as live queues, or shared with v1/v2 participants.

## Identity and storage lifetime

A queue is transient and exists only while at least one publisher or subscriber remains attached. After the last endpoint closes or its process exits, its messages cannot be resumed. Reopening the same name creates a fresh, empty queue. Keep participant lifetimes overlapping across process handoffs; either a publisher or a subscriber is sufficient to retain the queue.

Participants must agree on the queue name, logical capacity, and, on Unix, the same backing directory. Use an explicit absolute directory for cross-language applications: runtime defaults for the temporary directory can differ. The name must fit platform object-name limits; short ASCII names without slashes or NUL work on all platforms. A distinct Unix path does **not** create a distinct semaphore name: use names unique across paths.

Capacity `C` is the size of the circular message buffer only. It must exceed 16 and be divisible by 8. Total mapped bytes are `262400 + C`. All additions must be checked against the implementation's addressable range.

### Linux and macOS

For base directory `P` and name `N`:

| Resource | Name |
|---|---|
| Backing file | `P/.cloudtoid/interprocess/v3/mmf/N.qu` |
| Coordination lock | `flock` on the `P/.cloudtoid/interprocess/v3/mmf` directory inode |
| Participant lease | `P/.cloudtoid/interprocess/v3/readers/N/ID` |
| Named semaphore | `/ct3ip.N` |

Opening and closing coordinate under an exclusive directory `flock`. Every attached endpoint retains a shared `flock` on the backing file. A new opener tries an exclusive nonblocking backing-file lock. Success proves there are no attached participants: unlink any old named semaphore, remove stale lease files, truncate the backing file to zero, and resize it to the total mapped length. Otherwise the existing file length must match. Convert to a shared lock before releasing coordination.

Close the semaphore and unmap before releasing the participant's backing-file lifetime lock. While holding coordination, try to convert that shared lock to an exclusive nonblocking lock. Only success permits unlinking the semaphore, lease directory, and backing file. Otherwise retain the queue for remaining participants. A process crash releases its OS locks. After the last participant crashes, stale names can remain until the next opener performs cleanup; they must never be interpreted as a durable saved queue.

A participant lease is an open file with an exclusive `flock`, held throughout all uses of its registration. Liveness probes open that file and try an exclusive nonblocking lock: contention means alive; acquisition or a missing file means dead. Delete stale lease files after acquiring their lock. Permission and other inspection failures mean **unknown/alive**, never proof of death. Participant IDs are never reused in a live queue, so a stale probe cannot target a later owner of that ID.

POSIX semaphores are created with initial count zero and requested mode `0777`, subject to the process umask. macOS requires the platform's real variadic `sem_open` ABI. The implementations use `sem_timedwait` on Linux and bounded `sem_trywait` retries on macOS.

### Windows

| Resource | Name |
|---|---|
| Page-file-backed mapping | `CT3_IP_N` |
| Initialization mutex | `CT3_INIT_N` |
| Participant lease mapping | `CT3_READER_N.ID` |
| Semaphore | `Global\CT3.IP.N` |

Mapping and lease names are session-local; participants must share the Windows session and have compatible object permissions. The semaphore uses the existing global namespace. The path option has no effect on Windows.

Acquire the initialization mutex before opening the mapping. An abandoned mutex still grants ownership. Create/open the total mapped length, then inspect the signed 64-bit logical capacity at offset 32 through a 40-byte view. Zero means this mapping needs initialization; write `C`. Any other value must equal `C`. Release the initialization mutex only after this is complete. No participant opens the mapping before owning that mutex, so a creator that crashes before initialization cannot leave an uninitialized mapping held by a waiting joiner.

Windows retains mapping and semaphore objects while handles/views remain. Close all handles/views for an endpoint after its calls stop; other publishers and subscribers retain their own handles. Never destroy a queue because one reader crashed.

A lease is a separate 16-byte mapping, created before its ID becomes visible as a queue owner:

| Offset | Type | Meaning |
|---|---|---|
| 0 | signed 32-bit integer | Process ID |
| 4 | 4 bytes | Zero padding |
| 8 | signed 64-bit integer | Process creation time, UTC .NET ticks since 0001-01-01 |

Read both fields to probe liveness. The PID must identify a running process whose creation time matches exactly, avoiding PID reuse. Convert Windows FILETIME to .NET ticks by adding `504911232000000000`; both count 100 ns intervals, with different epochs. Missing mappings, a nonexistent/exited process, or a creation-time mismatch mean dead. Inspection/access failures mean unknown/alive. Do not retain the probe mapping longer than the probe itself.

## Shared memory layout

All offsets are bytes from the mapping's first byte. All integers are little-endian. Initial memory is zero. Fields identified as atomic must always be accessed atomically at their natural alignment while shared. Padding remains reserved and zero; a port must not repurpose it without a protocol change.

| Offset | Size | Field |
|---|---:|---|
| 0 | 8 | Atomic signed `ReadOffset` |
| 8 | 8 | Atomic signed `WriteOffset` |
| 16 | 8 | Atomic signed `ReadLockOwner`; 0 means unowned |
| 24 | 4 | Atomic signed `NotificationPending`; 0 or 1 |
| 28 | 4 | Atomic signed `LastParticipantId` |
| 32 | 8 | Windows logical capacity; initialization mutex protects it; unused on Unix |
| 40–127 | 88 | Reserved |
| 128 | 4 | Atomic signed recovery admission gate; 0 open, 1 closed |
| 132–255 | 124 | Reserved |
| 256 | 262144 | 2048 publisher slots, each 128 bytes |
| 262400 | `C` | Circular message buffer |

Each publisher slot contains:

| Relative offset | Size | Field |
|---|---:|---|
| 0 | 8 | Atomic signed participant ID; 0 means unused |
| 8 | 4 | Atomic signed number of admitted/in-flight enqueue calls |
| 12–127 | 116 | Reserved |

Allocate participant IDs with a checked atomic increment of `LastParticipantId`, starting at 1. Publishers and subscribers share this allocator. Establish the liveness lease before installing the ID in a reader owner word or publisher slot. IDs never wrap: fail registration at `INT32_MAX` and use a fresh queue after its participants finish.

A publisher claims an empty slot with compare/exchange, then resets its active count to zero before admitting calls. If all slots are occupied, it may reclaim a slot whose owner is proven dead, again with compare/exchange against the observed ID. Reject a 2049th live publisher. After all calls stop, release the slot only if it still contains the publisher's ID; release its lease afterward.

## Circular records and position arithmetic

`ReadOffset` and `WriteOffset` are monotonic signed 64-bit byte positions. Physical position is `262400 + (position % C)`. Logical positions never wrap, although the physical buffer wraps continually. Require checked arithmetic and reject reservation beyond `INT64_MAX`. Do not silently reset counters in a live queue. At 1 GiB/s of reserved bytes, exhaustion takes about 272 years; it is still an explicit error, not an assumption about runtime length.

A record consists of:

| Relative offset | Size | Meaning |
|---|---:|---|
| 0 | 4 | Atomic state: 0 unfinished/free, 1 reader claimed, 2 ready |
| 4 | 4 | Signed nonnegative payload byte length `L` |
| 8 | `L` | Opaque payload bytes |
| `8 + L` | 0–7 | Alignment padding |

The reserved record length is `(L + 15) & ~7`, using checked/wide arithmetic. Empty payloads are valid and reserve 8 bytes. Every record starts at an 8-byte boundary; because capacity also divides by 8, its header never straddles the physical end. Payload and padding can straddle, requiring at most two copies. The complete cleared record, including padding, must be zero before its space becomes reusable.

## Memory ordering and publication

Acquire loads, release stores, and sequentially consistent read/modify/write operations describe the ordering used by the Rust implementation. .NET `Volatile` and `Interlocked` provide the corresponding ordering. A port must use real process-shared native atomics, not an implementation that substitutes process-private locks.

For each enqueue (or admitted batch):

1. Atomically increment the publisher slot's active count with a full-fence operation **before** observing the recovery gate. If the gate is closed, decrement and report no admission.
2. Load `ReadOffset` with acquire ordering, then `WriteOffset` with acquire ordering. Compute `used = write - read`. If the proposed padded length exceeds `C - used`, return full. A stale read position is conservative. Keep this order of loads.
3. Checked-add the record length to `write`; compare/exchange `WriteOffset` from that exact `write` to the new value. Retry the capacity calculation after a failed CAS. A successful CAS reserves one exclusive physical range. Never split the capacity check and reservation into unrelated atomic updates.
4. Copy the payload and store its length. Publish state 2 with release ordering **last**. Readers must acquire this state before accessing the length or payload.
5. Notify as described below. Decrement the active count in all exits, after writes and notification complete.

Keeping one admission across a batch is allowed, provided the active count remains nonzero throughout, each message reserves independently, and recovery can observe the entire batch as active. No batch may continue writing after decrementing its active count.

The active increment and gate closure/scan need the full-fence ordering: either a writer observes the closed gate and does not write, or the recovery scan observes its active count. Recovery never clears memory while a live admitted writer might still touch it.

## Consumption and crash recovery

A subscriber owns one unique participant ID and lease. Each receive attempt:

1. Inspect `ReadLockOwner` before checking whether the queue is empty. If a different owner is present, periodically probe its lease. A live owner is never expired, however long it pauses.
2. For a proven-dead owner, CAS that exact ID to the repairing subscriber's own ID. Only the successful repairer can reopen a stranded recovery gate, then release ownership. Do not first clear ownership and later open the gate: a delayed repair could corrupt a newer recovery attempt.
3. If empty, return no message. Otherwise CAS owner 0 to the subscriber's own ID; fail without waiting if another reader owns it. Recheck emptiness under ownership.
4. CAS the head record state from ready (2) to claimed (1), with acquire/full-fence ordering. After success, validate/read its length, copy the payload, clear the entire padded record, then atomically advance `ReadOffset`. Releasing the read position only after clearing prevents a writer from reusing uncleared bytes.
5. Release ownership before returning, including unsuccessful attempts. Compare/exchange only the caller's own ID back to zero.

For an unfinished or abandoned claimed head, retain a local observation containing the current read position, the then-current write tail, and a monotonic timestamp. A changed head starts a new observation. After at least ten seconds at the same blocked head:

1. While holding reader ownership, atomically close the admission gate.
2. Scan every publisher slot. If any slot has a nonzero active count and an owner that is alive or cannot be proven dead, reopen admission and postpone recovery. Keep the originally captured tail.
3. Otherwise clear the range from the blocked head to the captured tail, then advance `ReadOffset` to that tail. Validate that this range is between zero and `C`. New reservations beyond the captured tail remain intact.
4. Reopen admission and release reader ownership.

This waits out a slow live publisher and recovers from a killed publisher without erasing the whole queue. It may discard completed messages behind an abandoned head within the captured range. Dead-reader repair occurs even when `read == write`, because a reader can die after advancing the position but before reopening the gate. The ten-second observation interval is a recovery delay, not permission to expire a live process.

## Notifications and blocking receives

The semaphore is a wakeup hint, not a message count. On publication, atomically exchange `NotificationPending` to 1. Only a previous value of 0 posts a semaphore permit. A full semaphore is already signaled and does not turn a committed publication into an error.

A blocking receiver attempts to read before sleeping. Only after actually consuming a semaphore permit may it clear `NotificationPending` to 0. If that receiver exits while data remains after consuming a permit, it relays notification using the same exchange/post sequence. An empty check must not clear another receiver's pending notification.

Wait/retry in bounded intervals of at most 5 ms (subject to OS scheduling). This retains progress if a publisher crashes after setting the pending flag but before posting, or another receiver consumes a permit and then exits. Language-level timeout/cancellation affects the wait, not the shared protocol. Nonblocking receive does not require cancellation or a semaphore wait.

## Interoperability and implementation obligations

Use raw bytes across language boundaries. Managed objects, native pointers, language string layouts, and Arrow C Data pointers are not cross-process payload formats. Agree separately on encoding/schema when needed. Receive buffers belong to the caller; no wrapper should expose borrowed queue memory after advancing `ReadOffset`.

Ports must test every publisher/subscriber direction, small capacities and repeated wraparound, empty payloads, full queues, concurrent participants, capacity mismatch, last-participant cleanup, process death, and paused live owners. `tests/interop/run.py` runs the six-language pair matrix. Rust tests pin the binary offsets and exercise the native implementation; .NET retains its own regression tests.

Resource cleanup must happen only after all calls using that endpoint have stopped. Rust borrowing provides that lifetime rule. C callers must obey it explicitly. Other bindings enforce their documented close behavior. A queue protocol does not make use-after-close of a language handle valid.
