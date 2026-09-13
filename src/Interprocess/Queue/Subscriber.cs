using System.Runtime.CompilerServices;

namespace Cloudtoid.Interprocess;

internal sealed class Subscriber : Queue, ISubscriber
{
    private static readonly long TicksForTenSeconds = TimeSpan.FromSeconds(10).Ticks;
    private readonly IInterprocessSemaphoreWaiter signal;
    private int activeReads;
    private PendingRead? pendingRead;

    internal Subscriber(
        QueueOptions options,
        ILoggerFactory loggerFactory,
        IInterprocessSemaphoreWaiter? signal = null)
        : base(options, loggerFactory)
    {
        try
        {
            this.signal = signal ?? InterprocessSemaphore.CreateWaiter(options.QueueName);
        }
        catch
        {
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

        ReleasePendingRead();

        if (disposing)
            signal.Dispose();

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
        // Rejected admission must not enter the catch below, which touches the shared read lock.
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
        catch
        {
            ReleasePendingRead();
            throw;
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
        var pending = Volatile.Read(ref pendingRead);
        if (pending is not null && Interlocked.CompareExchange(ref pendingRead, null, pending) != pending)
            pending = null;

        var header = *Header;

        // is this an empty queue?
        if (header.IsEmpty())
        {
            if (pending is not null)
                Interlocked.CompareExchange(ref Header->ReadLockTimestamp, 0L, pending.Timestamp);

            return false;
        }

        var readLockTimestamp = header.ReadLockTimestamp;
        var start = DateTime.UtcNow.Ticks;

        if (pending is not null
            && readLockTimestamp == pending.Timestamp
            && header.ReadOffset == pending.ReadOffset)
        {
            // Reacquire ownership without restarting this reservation's recovery deadline.
            if (Interlocked.CompareExchange(ref Header->ReadLockTimestamp, start, pending.Timestamp)
                != pending.Timestamp)
            {
                return false;
            }

            pending.Timestamp = start;
        }
        else
        {
            pending = null;
            // is there already a read-lock or has the previous lock timed out meaning that a subscriber crashed?
            if (start - readLockTimestamp < TicksForTenSeconds)
                return false;

            // take a read-lock so no other thread can read a message
            if (Interlocked.CompareExchange(ref Header->ReadLockTimestamp, start, readLockTimestamp)
                != readLockTimestamp)
            {
                return false;
            }
        }

        var retainReadLock = false;
        try
        {
            // is the queue empty now that we were able to get a read-lock?
            if (Header->IsEmpty())
                return false;

            // now finally have a read-lock and the queue is not empty
            var readOffset = Header->ReadOffset;
            var writeOffset = pending?.WriteOffset ?? Header->WriteOffset;
            var messageHeader = (MessageHeader*)Buffer.GetPointer(readOffset);

            var state = Interlocked.CompareExchange(
                ref messageHeader->State,
                MessageHeader.LockedToBeConsumedState,
                MessageHeader.ReadyToBeConsumedState);

            if (state != MessageHeader.ReadyToBeConsumedState)
            {
                // but if the publisher crashed, we will never get the message, so we need to handle that case by timing out
                if (DateTime.UtcNow.Ticks - (pending?.StartedTimestamp ?? start) > TicksForTenSeconds)
                {
                    var discardedLength = writeOffset - readOffset;

                    // Reject a stale snapshot before clearing. These checks cannot fence an owner paused mid-clear.
                    if (discardedLength < 0
                        || discardedLength > Buffer.Capacity
                        || Volatile.Read(ref Header->ReadLockTimestamp) != start
                        || Volatile.Read(ref Header->ReadOffset) != readOffset)
                    {
                        return false;
                    }

                    // Clear through the captured tail before publishers can reuse the space.
                    // Otherwise discarded ready headers could be consumed on a later lap.
                    Buffer.Clear(readOffset, discardedLength);
                    Interlocked.Exchange(ref Header->ReadOffset, writeOffset);
                    return false;
                }

                // Keep ownership between immediate attempts. Releasing the shared lock and
                // remembering only an offset could mistake a later ring lap for this reservation.
                pending ??= new PendingRead(start, readOffset, writeOffset);
                retainReadLock = Interlocked.CompareExchange(ref pendingRead, pending, null) is null;
                return false;
            }

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
                // Destination allocation or custom memory can fail. Leave the message
                // available for retry, but do not change a successor reader's state.
                if (Volatile.Read(ref Header->ReadLockTimestamp) == start
                    && Volatile.Read(ref Header->ReadOffset) == readOffset)
                {
                    Interlocked.CompareExchange(
                        ref messageHeader->State,
                        MessageHeader.ReadyToBeConsumedState,
                        MessageHeader.LockedToBeConsumedState);
                }

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
            // Release only our own read-lock if another reader has recovered it.
            if (!retainReadLock)
                Interlocked.CompareExchange(ref Header->ReadLockTimestamp, 0L, start);
        }

        return true;
    }

    private unsafe void ReleasePendingRead()
    {
        var pending = Interlocked.Exchange(ref pendingRead, null);
        if (pending is not null)
            Interlocked.CompareExchange(ref Header->ReadLockTimestamp, 0L, pending.Timestamp);
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
        internal long Timestamp { get; set; } = timestamp;
        internal long StartedTimestamp { get; } = timestamp;
        internal long ReadOffset { get; } = readOffset;
        internal long WriteOffset { get; } = writeOffset;
    }
}