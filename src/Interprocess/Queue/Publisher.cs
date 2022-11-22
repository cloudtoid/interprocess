using System;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess
{
    internal sealed class Publisher : Queue, IPublisher
    {
        private readonly IInterprocessSemaphoreReleaser signal;

        internal Publisher(QueueOptions options, ILoggerFactory loggerFactory)
            : base(options, loggerFactory)
        {
            signal = InterprocessSemaphore.CreateReleaser(options.QueueName);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                signal.Dispose();

            base.Dispose(disposing);
        }

        public unsafe bool TryEnqueue(ReadOnlySpan<byte> message)
        {
            var bodyLength = message.Length;
            var messageLength = GetMessageLength(bodyLength);

            while (true)
            {
                var header = *Header;
                var writeOffset = header.WriteOffset;

                if (!CheckCapacity(header, messageLength))
                    return false;

                var newWriteOffset = SafeIncrementMessageOffset(writeOffset, messageLength);

                // try to atomically update the writeOffset-offset that is stored in the queue header
                var currentTailOffset = ((long*)Header) + 1;
                if (Interlocked.CompareExchange(ref *currentTailOffset, newWriteOffset, writeOffset) == writeOffset)
                {
                    try
                    {
                        // writeOffset the message body
                        Buffer.Write(message, GetMessageBodyOffset(writeOffset));

                        // writeOffset the message header
                        Buffer.Write(
                            new MessageHeader(MessageHeader.ReadyToBeConsumedState, bodyLength),
                            writeOffset);
                    }
                    catch (Exception ex)
                    {
                        // if there is an error here, we are in a bad state.
                        // treat this as a fatal exception and crash the process
                        Logger.FailFast(
                            "Publishing to the shared memory queue failed leaving the queue in a bad state. " +
                            "The only option is to crash the application.",
                            ex);
                    }

                    // signal the next receiver that there is a new message in the queue
                    signal.Release();
                    return true;
                }
            }
        }

        private bool CheckCapacity(QueueHeader header, long messageLength)
        {
            var readOffset = header.ReadOffset;
            var writeOffset = header.WriteOffset;

            if (messageLength > Buffer.Capacity)
                return false;

            if (readOffset == writeOffset)
                return true; // it is an empty queue

            readOffset %= Buffer.Capacity;
            writeOffset %= Buffer.Capacity;

            if (readOffset == writeOffset)
                return false; // queue is 100% full (readOffset a message to open room)

            if (readOffset < writeOffset)
            {
                if (messageLength > Buffer.Capacity + readOffset - writeOffset)
                    return false;
            }
            else
            {
                if (messageLength > readOffset - writeOffset)
                    return false;
            }

            return true;
        }
    }
}
