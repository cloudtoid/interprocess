namespace Cloudtoid.Interprocess;

internal sealed class Subscriber : Queue, ISubscriber
{
    private static readonly long TicksForTenSeconds = TimeSpan.FromSeconds(10).Ticks;
    private readonly CancellationTokenSource cancellationSource = new();
    private readonly CountdownEvent countdownEvent = new(1);
    private readonly IInterprocessSemaphoreWaiter signal;

    internal Subscriber(QueueOptions options, ILoggerFactory loggerFactory)
        : base(options, loggerFactory)
    {
        try
        {
            signal = InterprocessSemaphore.CreateWaiter(options.QueueName);
        }
        catch
        {
            countdownEvent.Dispose();
            cancellationSource.Dispose();
            base.Dispose(true);
            throw;
        }
    }

    public bool TryDequeue(CancellationToken cancellation, out ReadOnlyMemory<byte> message) =>
        TryDequeueCore(default, cancellation, out message);

    public bool TryDequeue(Memory<byte> buffer, CancellationToken cancellation, out ReadOnlyMemory<byte> message) =>
        TryDequeueCore(buffer, cancellation, out message);

    public ReadOnlyMemory<byte> Dequeue(CancellationToken cancellation) =>
        DequeueCore(default, cancellation);

    public ReadOnlyMemory<byte> Dequeue(Memory<byte> buffer, CancellationToken cancellation) =>
        DequeueCore(buffer, cancellation);

    protected override void Dispose(bool disposing)
    {
        // drain the Dequeue/TryDequeue requests
        cancellationSource.Cancel();
        countdownEvent.Signal();
        countdownEvent.Wait();

        // There is a potential for a race condition in DequeueCore if the cancellationSource
        // was not cancelled before AddEvent is called. The sleep here will prevent that.
        Thread.Sleep(millisecondsTimeout: 10);

        if (disposing)
        {
            countdownEvent.Dispose();
            signal.Dispose();
            cancellationSource.Dispose();
        }

        base.Dispose(disposing);
    }

    private bool TryDequeueCore(
        Memory<byte>? resultBuffer,
        CancellationToken cancellation,
        out ReadOnlyMemory<byte> message)
    {
        // do NOT reorder the cancellation and the AddCount operation below. See Dispose for more information.
        cancellationSource.ThrowIfCancellationRequested(cancellation);
        countdownEvent.AddCount();

        try
        {
            return TryDequeueImpl(resultBuffer, cancellation, out message);
        }
        finally
        {
            countdownEvent.Signal();
        }
    }

    private ReadOnlyMemory<byte> DequeueCore(Memory<byte>? resultBuffer, CancellationToken cancellation)
    {
        // do NOT reorder the cancellation and the AddCount operation below. See Dispose for more information.
        cancellationSource.ThrowIfCancellationRequested(cancellation);
        countdownEvent.AddCount();

        try
        {
            SpinWait spin = default;
            while (true)
            {
                if (TryDequeueImpl(resultBuffer, cancellation, out var message))
                    return message;

                // Retry briefly in user space while another reader finishes. Once spinning
                // would yield, wait for a signal instead of burning CPU on an idle queue.
                if (spin.NextSpinWillYield)
                {
                    signal.Wait(millisecondsTimeout: 5);
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
            countdownEvent.Signal();
        }
    }

    private unsafe bool TryDequeueImpl(
        Memory<byte>? resultBuffer,
        CancellationToken cancellation,
        out ReadOnlyMemory<byte> message)
    {
        cancellationSource.ThrowIfCancellationRequested(cancellation);

        message = ReadOnlyMemory<byte>.Empty;
        var header = *Header;

        // is this an empty queue?
        if (header.IsEmpty())
            return false;

        var readLockTimestamp = header.ReadLockTimestamp;
        var start = DateTime.UtcNow.Ticks;

        // is there already a read-lock or has the previous lock timed out meaning that a subscriber crashed?
        if (start - readLockTimestamp < TicksForTenSeconds)
            return false;

        // take a read-lock so no other thread can read a message
        if (Interlocked.CompareExchange(ref Header->ReadLockTimestamp, start, readLockTimestamp) != readLockTimestamp)
            return false;

        try
        {
            // is the queue empty now that we were able to get a read-lock?
            if (Header->IsEmpty())
                return false;

            // now finally have a read-lock and the queue is not empty
            var readOffset = Header->ReadOffset;
            var writeOffset = Header->WriteOffset;
            var messageHeader = (MessageHeader*)Buffer.GetPointer(readOffset);

            while (true)
            {
                // was this message fully written by the publisher? if not, wait for the publisher to finish writing it
                var state = Interlocked.CompareExchange(
                    ref messageHeader->State,
                    MessageHeader.LockedToBeConsumedState,
                    MessageHeader.ReadyToBeConsumedState);

                if (state == MessageHeader.ReadyToBeConsumedState)
                    break;

                // but if the publisher crashed, we will never get the message, so we need to handle that case by timing out
                if (DateTime.UtcNow.Ticks - start > TicksForTenSeconds)
                {
                    // the publisher crashed and we will never get the message
                    // so we need to release the read-lock and advance the queue for everyone.
                    // some messages might be lost in this case but this is the best we can do.
                    Interlocked.Exchange(ref Header->ReadOffset, writeOffset);
                    return false;
                }
                cancellationSource.ThrowIfCancellationRequested(cancellation);
                Thread.Yield();
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
            var newReadOffset = SafeIncrementMessageOffset(readOffset, messageLength);
            Interlocked.Exchange(ref Header->ReadOffset, newReadOffset);
        }
        finally
        {
            // Release only our own read-lock if another reader has recovered it.
            Interlocked.CompareExchange(ref Header->ReadLockTimestamp, 0L, start);
        }

        return true;
    }
}