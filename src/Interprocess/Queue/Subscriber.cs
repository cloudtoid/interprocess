using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace Cloudtoid.Interprocess;

internal sealed class Subscriber : Queue, ISubscriber
{
    private static readonly long RecoveryInterval = Stopwatch.Frequency * 10;
    private readonly IInterprocessSemaphoreWaiter signal;
    private readonly QueueOptions options;
    private readonly ReaderLease lease;
    private readonly long readerId;
    private long nextRecoveryCheck = Stopwatch.GetTimestamp() + RecoveryInterval;
    private int activeReads;
    // Accessed only while holding the shared read lock.
    private PendingRead? pendingRead;

    internal Subscriber(
        QueueOptions options,
        ILoggerFactory loggerFactory,
        IInterprocessSemaphoreWaiter? signal = null)
        : base(options, loggerFactory)
    {
        this.options = options;
        try
        {
            readerId = RegisterReader();
            lease = new ReaderLease(options, readerId);
            this.signal = signal ?? InterprocessSemaphore.CreateWaiter(options.QueueName);
        }
        catch
        {
            lease?.Dispose();
            base.Dispose(true);
            throw;
        }
    }

    public bool TryDequeue(out ReadOnlyMemory<byte> message) =>
        TryDequeueCore(default, out message);

    public bool TryDequeue(Memory<byte> buffer, out ReadOnlyMemory<byte> message) =>
        TryDequeueCore(buffer, out message);

    public ReadOnlyMemory<byte> Dequeue(CancellationToken cancellation) =>
        DequeueCore(default, cancellation);

    public ReadOnlyMemory<byte> Dequeue(Memory<byte> buffer, CancellationToken cancellation) =>
        DequeueCore(buffer, cancellation);

    // Internal so tests can exercise admission after disposal has drained the counter.
    internal void EnterRead(CancellationToken cancellation)
    {
        Interlocked.Increment(ref activeReads);
        if (!IsDisposed && !cancellation.IsCancellationRequested)
            return;

        Interlocked.Decrement(ref activeReads);
        ThrowIfCancellationRequested(cancellation);
    }

    // Also used by tests that inspect notifications without dequeuing a message.
    internal unsafe bool WaitForNotification(int millisecondsTimeout)
    {
        if (!signal.Wait(millisecondsTimeout))
            return false;

        // Only a consumed permit can clear the flag. Resetting on timeout could allow
        // another post while a paused participant still owns the previous one.
        Interlocked.Exchange(ref Header->NotificationPending, 0);
        return true;
    }

    protected override void Dispose(bool disposing)
    {
        // Queue.Dispose has closed admission. Blocking readers observe that flag on their next retry.
        SpinWait spin = default;
        while (Volatile.Read(ref activeReads) != 0)
            spin.SpinOnce();

        if (disposing)
        {
            lease.Dispose();
            signal.Dispose();
        }

        base.Dispose(disposing);
    }

    private bool TryDequeueCore(
        Memory<byte>? resultBuffer,
        out ReadOnlyMemory<byte> message)
    {
        EnterRead(default);

        try
        {
            return TryDequeueImpl(resultBuffer, out message);
        }
        finally
        {
            Interlocked.Decrement(ref activeReads);
        }
    }

    private unsafe ReadOnlyMemory<byte> DequeueCore(Memory<byte>? resultBuffer, CancellationToken cancellation)
    {
        EnterRead(cancellation);
        var relayNotification = false;

        try
        {
            SpinWait spin = default;
            while (true)
            {
                ThrowIfCancellationRequested(cancellation);
                if (TryDequeueImpl(resultBuffer, out var message))
                    return message;

                // Retry briefly in user space while another reader finishes. Once spinning
                // would yield, wait for a signal instead of burning CPU on an idle queue.
                if (spin.NextSpinWillYield)
                {
                    relayNotification |= WaitForNotification(millisecondsTimeout: 5);
                    spin.Reset();
                }
                else
                {
                    spin.SpinOnce();
                }
            }
        }
        finally
        {
            try
            {
                // A woken reader must pass the notification on if messages remain,
                // including when cancellation, disposal, or a destination failure ends this call.
                if (relayNotification && !Header->IsEmpty())
                    Notify(signal);
            }
            finally
            {
                Interlocked.Decrement(ref activeReads);
            }
        }
    }

    private unsafe bool TryDequeueImpl(
        Memory<byte>? resultBuffer,
        out ReadOnlyMemory<byte> message)
    {
        message = ReadOnlyMemory<byte>.Empty;
        var header = Header;
        if (header->IsEmpty())
            return false;

        var owner = header->ReadLockOwner;
        if (owner != 0)
        {
            TryRecoverReader(owner);
            return false;
        }

        if (Interlocked.CompareExchange(ref header->ReadLockOwner, readerId, 0L) != 0)
            return false;

        try
        {
            // is the queue empty now that we were able to get a read-lock?
            if (Header->IsEmpty())
                return false;

            // now finally have a read-lock and the queue is not empty
            var readOffset = Header->ReadOffset;
            var messageHeader = (MessageHeader*)Buffer.GetPointer(readOffset);

            var state = Interlocked.CompareExchange(
                ref messageHeader->State,
                MessageHeader.LockedToBeConsumedState,
                MessageHeader.ReadyToBeConsumedState);

            if (state != MessageHeader.ReadyToBeConsumedState)
            {
                // Monotonic positions identify this reservation even after the buffer wraps.
                // Releasing the lock between attempts lets other subscribers make progress.
                var pending = pendingRead;
                if (pending is null || pending.ReadOffset != readOffset)
                {
                    pendingRead = new PendingRead(Stopwatch.GetTimestamp(), readOffset, Header->WriteOffset);
                    return false;
                }

                // but if the publisher crashed, we will never get the message, so we need to handle that case by timing out
                if (Stopwatch.GetTimestamp() - pending.StartedTimestamp > RecoveryInterval)
                {
                    // Clear through the captured tail before publishers can reuse the space.
                    // Otherwise discarded ready headers could be consumed on a later lap.
                    Buffer.Clear(readOffset, pending.WriteOffset - readOffset);
                    Interlocked.Exchange(ref Header->ReadOffset, pending.WriteOffset);
                    pendingRead = null;
                }

                return false;
            }

            pendingRead = null;
            // read the message body from the queue
            var bodyLength = messageHeader->BodyLength;
            try
            {
                message = Buffer.Read(
                    GetMessageBodyOffset(readOffset),
                    bodyLength,
                    resultBuffer);
            }
            catch
            {
                // We still own the read lock. Leave the message available for retry.
                Interlocked.CompareExchange(
                    ref messageHeader->State,
                    MessageHeader.ReadyToBeConsumedState,
                    MessageHeader.LockedToBeConsumedState);

                throw;
            }

            // zero out the message, including the message header
            var messageLength = GetPaddedMessageLength(bodyLength);
            Buffer.Clear(readOffset, messageLength);

            // update the read offset of the queue
            var newReadOffset = checked(readOffset + messageLength);
            Interlocked.Exchange(ref Header->ReadOffset, newReadOffset);
        }
        finally
        {
            // A live reader keeps ownership until it explicitly releases the lock.
            Interlocked.CompareExchange(ref Header->ReadLockOwner, 0L, readerId);
        }

        return true;
    }

    private unsafe long RegisterReader()
    {
        while (true)
        {
            var previous = Volatile.Read(ref Header->LastReaderId);
            var next = checked(previous + 1);
            if (Interlocked.CompareExchange(ref Header->LastReaderId, next, previous) == previous)
                return next;
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private unsafe void TryRecoverReader(long owner)
    {
        // Another call on this subscriber is still alive. Only its owner can release it.
        if (owner == readerId)
            return;

        var next = Volatile.Read(ref nextRecoveryCheck);
        var now = Stopwatch.GetTimestamp();
        if (now < next || Interlocked.CompareExchange(ref nextRecoveryCheck, now + RecoveryInterval, next) != next)
            return;

        if (!ReaderLease.IsAlive(options, owner))
            Interlocked.CompareExchange(ref Header->ReadLockOwner, 0L, owner);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void ThrowIfCancellationRequested(CancellationToken cancellation)
    {
        if (IsDisposed)
            throw new OperationCanceledException();

        cancellation.ThrowIfCancellationRequested();
    }

    private sealed class PendingRead(long timestamp, long readOffset, long writeOffset)
    {
        internal long StartedTimestamp { get; } = timestamp;
        internal long ReadOffset { get; } = readOffset;
        internal long WriteOffset { get; } = writeOffset;
    }
}