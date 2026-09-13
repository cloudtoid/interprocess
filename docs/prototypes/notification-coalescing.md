# Notification coalescing prototype (#37)

This prototype is based on v3 alpha commit `3ed007c` (PR #53). It changes the notification protocol;
all publishers and subscribers must use the same version and start with a fresh queue. The queue header
remains 32 bytes. A 32-bit `NotificationPending` flag at offset 24 stores 0 or 1; the 32-bit `Reserved` field at offset 28 remains unused.

## Algorithm

1. After publishing a ready message, exchange `NotificationPending` with 1. Post to the semaphore only
   if the old value was 0. Keep the existing handling of a full semaphore after publication.
2. A blocking reader clears the flag only after successfully consuming a permit, then retries dequeue.
   A timed-out wait must not reset it: another participant could still be paused before its post.
3. A reader that consumed a permit passes on a notification if messages remain when its dequeue call
   ends. This runs on success, cancellation, disposal, and destination failure. Immediate readers do
   not need to consume permits or pass them on.

An unconditional interlocked exchange is intentional. It orders message publication before observing
notification state, even when the old value is already 1. A volatile-read shortcut would lose that
ordering. Publication before acknowledgement is observed by the reader's subsequent queue check;
publication after acknowledgement claims the cleared flag and posts a new permit.

With a fresh queue and matching participants, at most one notification is posted, in flight, or awaiting
acknowledgement. The flag cannot be cleared until the previous permit is consumed. This prevents a
backlog proportional to the number of messages while readers are active.

## Limits and recovery

The existing 5 ms polling fallback remains necessary. Anonymous semaphore wakeups do not select the
subscriber retaining an unfinished read, and a woken reader can pause or die before passing on its
notification. The normal ready-message wakeup tests disable the short fallback so it cannot hide a
missed handoff; this is not a claim of immediate wakeup under every failure or contention schedule.

A participant that dies after claiming the flag but before posting leaves notifications suppressed.
Readers still deliver messages through polling, but restoring notifications requires a fresh queue.
This was exercised by killing a real publisher process at that point. Resetting the flag on timeout
would violate the bound when a delayed publisher resumes, so this prototype deliberately does not do it.

The separate expired-owner memory corruption issue (#33) is unchanged. This prototype does not make
it safe for a reclaimed reader or publisher to resume touching old memory.

## Validation

Native Apple M5 Max, macOS 26.6.2, .NET 10.0.12 / SDK 10.0.401:

- Debug and Release suites: 161 passed, 4 platform-specific skips each; final notification tests also rerun in Release.
- After narrowing the notification flag to 32 bits: 67 notification/circular-buffer tests and 8,000,000 additional integrity-checked messages passed.
- 80,000,000 variable-length messages in a 120-byte ring across 1/4 publishers and 1/4 subscribers:
  every length, payload byte, unique identity, and final empty state checked.
- Four publisher and four reader processes: 4,000 further variable-length messages, verified per burst.
- Actual publisher killed between notification claim and native post: remaining and subsequent
  messages delivered through polling; notification suppression confirmed.
- 100,000 immediate enqueue/dequeue pairs leave one native permit, not 100,000.
- Tests cover publication immediately before a wait, publication between permit consumption and
  acknowledgement, cancellation after consuming a permit, a four-reader wakeup chain, and a publisher
  paused before posting while other traffic and timed-out waits continue.
- Existing lifetime, overflow, recovery, disposal, and cross-version resource isolation tests retained
  and updated where they previously assumed a native semaphore post for every message.

Reproduce the committed regression tests with:

```sh
dotnet test src/Interprocess.sln -c Release
```

Windows/Linux execution has not been performed for this prototype. PR #53 passed its three-platform
CI matrix, but those results belong to the base, not this change. No CI configuration was changed.

## Preliminary native performance

These measurements precede narrowing the flag from 64 to 32 bits; the notification algorithm is unchanged.
Two fresh-process launches, each with 20 alternating-order paired samples after warmup. Both versions
are Release builds; the prototype assembly was renamed only to load it beside the baseline. No VM
measurements. No other test workloads ran during measurement. Values below are medians, in nanoseconds.

| Workload | Baseline, run 1 | Prototype, run 1 | Baseline, run 2 | Prototype, run 2 |
| --- | ---: | ---: | ---: | ---: |
| 8-byte round trip | 208.0 | 36.3 | 206.8 | 37.5 |
| 50-byte round trip, 120-byte ring | 214.8 | 40.6 | 215.1 | 40.7 |
| 1 publisher / 4 readers, per message | 394.2 | 325.3 | 378.9 | 311.5 |
| 4 publishers / 4 readers, per message | 984.4 | 461.8 | 935.4 | 455.0 |

Round-trip samples perform 500,000 operations with reused buffers. Concurrent samples use eight batches
of 32,768 eight-byte messages, a 64 KiB queue, and dedicated threads; their timing includes worker
startup and completion. These are in-process exploratory measurements, not cross-process latency,
latency percentiles, or a replacement for the published BenchmarkDotNet results.

For burst-to-idle behavior, each sample first performs 100,000 immediate round trips, then blocks on
the empty queue with cancellation requested after 25 ms. Across 20 alternating-order samples per
version, median whole-process CPU during the idle wait fell from 27.09 ms to 0.58 ms (means 27.12 ms
and 2.55 ms). Median wall time was 27.04 ms and 29.73 ms respectively. Removing the backlog lets the
reader sleep; cancellation still waits for the existing polling interval and scheduling. Whole-process
CPU measurements include timer/runtime activity and have outliers.

The standalone stress, IPC and comparison harnesses plus raw logs are retained in the local
`notification-coalescing` investigation directory. These results support further evaluation of the
prototype; they do not establish performance on other hardware or eliminate the failure-mode limits.
