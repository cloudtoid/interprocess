using System;
using System.Threading;
using System.Threading.Tasks;

namespace Cloudtoid.Interprocess
{
    internal sealed class Publisher : Queue, IPublisher
    {
        private readonly IInterprocessSemaphoreReleaser signal;

        internal Publisher(QueueOptions options)
            : base(options)
        {
            signal = InterprocessSemaphore.CreateReleaser(CreateIdentifier());
        }

        public unsafe Task<bool> TryEnqueueAsync(
            ReadOnlySpan<byte> message,
            CancellationToken cancellationToken)
        {
            var bodyLength = message.Length;
            while (true)
            {
                var header = *Header;
                var tailOffset = header.TailOffset;

                long messageLength = GetMessageLength(bodyLength);
                long capacity = buffer.Capacity - tailOffset + header.HeadOffset;
                if (messageLength > capacity)
                    return Task.FromResult(false);

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

                        // signal the next receiver that there is a new message in the queue
                        signal.ReleaseAsync(cancellationToken).Wait();
                        return Task.FromResult(true);
                    }
                    catch
                    {
                        // if there is an error here, we are in a bad state.
                        // treat this as a fatal exception and crash the process
                        Environment.FailFast("Publishing to the shared memory queue failed leaving the queue in a bad state.");
                    }
                }
            }
        }
    }
}
