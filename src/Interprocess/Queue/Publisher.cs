namespace Cloudtoid.Interprocess;

internal sealed class Publisher : Queue, IPublisher
{
    private readonly IInterprocessSemaphoreReleaser signal;
    private int activeEnqueues;

    internal Publisher(
        QueueOptions options,
        ILoggerFactory loggerFactory,
        IInterprocessSemaphoreReleaser? signal = null)
        : base(options, loggerFactory)
    {
        try
        {
            this.signal = signal ?? InterprocessSemaphore.CreateReleaser(options.QueueName);
        }
        catch
        {
            base.Dispose(true);
            throw;
        }
    }

    public bool TryEnqueue(ReadOnlySpan<byte> message)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        Interlocked.Increment(ref activeEnqueues);
        try
        {
            // Disposal may have started between the first check and incrementing the counter.
            ObjectDisposedException.ThrowIf(IsDisposed, this);
            return TryEnqueueCore(message);
        }
        finally
        {
            Interlocked.Decrement(ref activeEnqueues);
        }
    }

    protected override void Dispose(bool disposing)
    {
        // Queue.Dispose has already closed admission. Drain calls that passed the second check.
        SpinWait spin = default;
        while (Volatile.Read(ref activeEnqueues) != 0)
            spin.SpinOnce();

        if (disposing)
            signal.Dispose();

        base.Dispose(disposing);
    }

    private unsafe bool TryEnqueueCore(ReadOnlySpan<byte> message)
    {
        var bodyLength = message.Length;
        var messageLength = GetPaddedMessageLength(bodyLength);
        var maxUsed = Buffer.Capacity - messageLength;
        if (maxUsed < 0)
            return false;

        while (true)
        {
            var header = *Header;

            // A stale read position only underestimates free space. If the write position
            // changes, the CAS below fails; monotonic positions can never match an earlier lap.
            var used = header.WriteOffset - header.ReadOffset;
            if (used < 0 || used > maxUsed)
                return false;

            var writeOffset = header.WriteOffset;
            var newWriteOffset = checked(writeOffset + messageLength);

            if (Interlocked.CompareExchange(ref Header->WriteOffset, newWriteOffset, writeOffset) != writeOffset)
                continue;

            try
            {
                // write the message body
                Buffer.Write(message, GetMessageBodyOffset(writeOffset));

                // Publish readiness only after the body and length are visible to readers.
                var messageHeader = (MessageHeader*)Buffer.GetPointer(writeOffset);
                messageHeader->BodyLength = bodyLength;
                Volatile.Write(ref messageHeader->State, MessageHeader.ReadyToBeConsumedState);
            }
            catch
            {
                // if there is an error here, we are in a bad state.
                // treat this as a fatal exception and crash the process
                Environment.FailFast(
                    "Publishing to the shared memory queue failed leaving the queue in a bad state. The only option is to crash the application.");
            }

            // signal the next receiver that there is a new message in the queue
            try
            {
                signal.Release();
            }
            catch (SemaphoreFullException)
            {
                // The message is committed, and a full semaphore already has pending notifications.
            }

            return true;
        }
    }
}