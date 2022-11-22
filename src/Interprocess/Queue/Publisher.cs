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
            while (true)
            {
                var header = *Header;
                var headOffset = header.HeadOffset;
                var tailOffset = header.TailOffset;

                var messageLength = GetMessageLength(bodyLength);
                if (tailOffset == headOffset)
                {
                    if (messageLength > Buffer.Capacity)
                        return false;
                }
                else
                {
                    var tail = tailOffset % Buffer.Capacity;
                    var head = headOffset % Buffer.Capacity;

                    if (head == tail)
                        return false;

                    if (tail > head)
                    {
                        if (messageLength > Buffer.Capacity - (tail - head))
                            return false;
                    }
                    else
                    {
                        if (messageLength > head - tail)
                            return false;
                    }
                }

                var newTailOffset = SafeIncrementMessageOffset(tailOffset, messageLength);

                // try to atomically update the tail-offset that is stored in the queue header
                var currentTailOffset = ((long*)Header) + 1;
                if (Interlocked.CompareExchange(ref *currentTailOffset, newTailOffset, tailOffset) == tailOffset)
                {
                    try
                    {
                        // write the message body
                        Buffer.Write(message, GetMessageBodyOffset(tailOffset));

                        // write the message header
                        Buffer.Write(
                            new MessageHeader(MessageHeader.ReadyToBeConsumedState, bodyLength),
                            tailOffset);
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
    }
}
