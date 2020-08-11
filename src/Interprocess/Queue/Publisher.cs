using Microsoft.Extensions.Logging;
using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    internal sealed class Publisher : Queue, IPublisher
    {
        private readonly IInterprocessSemaphoreReleaser signal;

        internal Publisher(QueueOptions options, ILogger logger)
            : base(options, logger)
        {
            signal = InterprocessSemaphore.CreateReleaser(identifier, logger);
        }

        public override void Dispose()
        {
            signal.Dispose();
            base.Dispose();
        }

        public unsafe bool TryEnqueue(
            ReadOnlySpan<byte> message,
            CancellationToken cancellation)
        {
            var bodyLength = message.Length;
            while (true)
            {
                var header = *Header;
                var tailOffset = header.TailOffset;

                long messageLength = GetMessageLength(bodyLength);
                long capacity = buffer.Capacity - tailOffset + header.HeadOffset;
                if (messageLength > capacity)
                    return false;

                var newTailOffset = SafeIncrementMessageOffset(tailOffset, messageLength);

                // try to atomically update the tail-offset that is stored in the queue header
                var currentTailOffset = ((long*)Header) + 1;
                if (Interlocked.CompareExchange(ref *currentTailOffset, newTailOffset, tailOffset) == tailOffset)
                {
                    try
                    {
                        // write the message body
                        buffer.Write(message, GetMessageBodyOffset(tailOffset));

                        // write the message header
                        buffer.Write(
                            new MessageHeader(MessageState.ReadyToBeConsumed, bodyLength),
                            tailOffset);
                    }
                    catch (Exception ex)
                    {
                        // if there is an error here, we are in a bad state.
                        // treat this as a fatal exception and crash the process
                        logger.FailFast(
                            "Publishing to the shared memory queue failed leaving the queue in a bad state. " +
                            "The only option is to crash the application.", ex);
                    }

                    // signal the next receiver that there is a new message in the queue
                    signal.ReleaseAsync(cancellation).Wait();
                    return true;
                }
            }
        }
    }
}
