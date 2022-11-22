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
                var tailOffset = header.TailOffset;

                if (!CheckCapacity(header, messageLength))
                    return false;

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

        private bool CheckCapacity(QueueHeader header, long messageLength)
        {
            var head = header.HeadOffset;
            var tail = header.TailOffset;

            if (messageLength > Buffer.Capacity)
                return false;

            if (head == tail)
                return true; // it is an empty queue

            head %= Buffer.Capacity;
            tail %= Buffer.Capacity;

            if (head == tail)
                return false; // queue is 100% full (read a message to open room)

            if (head < tail)
            {
                if (messageLength > Buffer.Capacity + head - tail)
                    return false;
            }
            else
            {
                if (messageLength > head - tail)
                    return false;
            }

            return true;
        }
    }
}
