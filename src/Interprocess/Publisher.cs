using System;
using System.Threading;

namespace Cloudtoid.Interprocess
{
    internal sealed class Publisher : Queue, IPublisher
    {
        internal Publisher(QueueOptions options)
            : base(options)
        {
        }

        public unsafe bool TryEnqueue(ReadOnlySpan<byte> message)
        {
            var bodyLength = message.Length;
            while (true)
            {
                var header = *(QueueHeader*)view.Pointer;
                var tailOffset = header.TailOffset;

                long messageLength = GetMessageLength(bodyLength);
                long capacity = buffer.Capacity - tailOffset + header.HeadOffset;
                if (messageLength > capacity)
                    return false;

                var newTailOffset = SafeIncrementMessageOffset(tailOffset, messageLength);

                // try to atomically update the tail-offset that is stored in the queue header
                var currentTailOffset = ((long*)view.Pointer) + 1;
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
                        SignalReceivers();
                    }
                    catch
                    {
                        // if there is an error here, we are in a bad state.
                        // treat this as a fatal exception and crash the process
                        Environment.FailFast("Publishing to the shared memory queue failed leaving the queue in a bad state.");
                    }

                    return true;
                }
            }
        }
    }
}
