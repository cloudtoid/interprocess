namespace Cloudtoid.Interprocess;

internal sealed class Publisher : Queue, IPublisher
{
    private readonly IInterprocessSemaphoreReleaser signal;

    internal Publisher(QueueOptions options, ILoggerFactory loggerFactory)
        : base(options, loggerFactory)
    {
        try
        {
            signal = InterprocessSemaphore.CreateReleaser(options.QueueName);
        }
        catch
        {
            base.Dispose(true);
            throw;
        }
    }

    public unsafe bool TryEnqueue(ReadOnlySpan<byte> message)
    {
        var bodyLength = message.Length;
        var messageLength = GetPaddedMessageLength(bodyLength);

        while (true)
        {
            var header = *Header;

            if (!CheckCapacity(header, messageLength))
                return false;

            var writeOffset = header.WriteOffset;
            var newWriteOffset = SafeIncrementMessageOffset(writeOffset, messageLength);

            // try to atomically update the write-offset that is stored in the queue header
            if (Interlocked.CompareExchange(ref Header->WriteOffset, newWriteOffset, writeOffset) == writeOffset)
            {
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
                signal.Release();
                return true;
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
            signal.Dispose();

        base.Dispose(disposing);
    }

    private bool CheckCapacity(QueueHeader header, long messageLength)
    {
        if (messageLength > Buffer.Capacity)
            return false;

        if (header.IsEmpty())
            return true; // it is an empty queue

        var readOffset = header.ReadOffset % Buffer.Capacity;
        var writeOffset = header.WriteOffset % Buffer.Capacity;

        if (readOffset == writeOffset)
            return false; // queue is full

        if (readOffset < writeOffset)
        {
            if (messageLength > Buffer.Capacity + readOffset - writeOffset)
                return false;
        }
        else if (messageLength > readOffset - writeOffset)
        {
            return false;
        }

        return true;
    }
}