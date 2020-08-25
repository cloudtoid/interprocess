using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess
{
    internal sealed class Subscriber : Queue, ISubscriber
    {
        private const long BeingCreated = (long)MessageState.BeingCreated;
        private const long LockedToBeConsumed = (long)MessageState.LockedToBeConsumed;
        private const long ReadyToBeConsumed = (long)MessageState.ReadyToBeConsumed;
        private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
        private readonly CountdownEvent countdownEvent = new CountdownEvent(1);
        private readonly IInterprocessSemaphoreWaiter signal;

        internal Subscriber(QueueOptions options, ILoggerFactory loggerFactory)
            : base(options, loggerFactory)
        {
            signal = InterprocessSemaphore.CreateWaiter(Identifier, loggerFactory);
        }

        public override void Dispose()
        {
            // drain the Dequeue/TryDequeue requests
            cancellationSource.Cancel();
            countdownEvent.Signal();
            countdownEvent.Wait();

            signal.Dispose();
            base.Dispose();
        }

        public bool TryDequeue(CancellationToken cancellation, out ReadOnlyMemory<byte> message)
            => TryDequeue(default(Memory<byte>?), cancellation, out message);

        public bool TryDequeue(
            Memory<byte> resultBuffer,
            CancellationToken cancellation,
            out ReadOnlyMemory<byte> message)
            => TryDequeue((Memory<byte>?)resultBuffer, cancellation, out message);

        public ReadOnlyMemory<byte> Dequeue(CancellationToken cancellation)
            => Dequeue(default(Memory<byte>?), cancellation);

        public ReadOnlyMemory<byte> Dequeue(Memory<byte> resultBuffer, CancellationToken cancellation)
            => Dequeue((Memory<byte>?)resultBuffer, cancellation);

        private ReadOnlyMemory<byte> Dequeue(Memory<byte>? resultBuffer, CancellationToken cancellation)
        {
            if (TryDequeue(resultBuffer, cancellation, out var message))
                return message;

            while (true)
            {
                signal.Wait(millisecondsTimeout: 100);

                if (TryDequeue(resultBuffer, cancellation, out message))
                    return message;
            }
        }

        private unsafe bool TryDequeue(
            Memory<byte>? resultBuffer,
            CancellationToken cancellation,
            out ReadOnlyMemory<byte> message)
        {
            countdownEvent.AddCount();
            try
            {
                using var linkedSource = new LinkedCancellationToken(cancellationSource.Token, cancellation);
                cancellation = linkedSource.Token;

                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();

                    var header = Header;
                    var headOffset = header->HeadOffset;

                    // is this is an empty queue?
                    if (headOffset == header->TailOffset)
                    {
                        message = ReadOnlyMemory<byte>.Empty;
                        return false;
                    }

                    var state = (long*)Buffer.GetPointer(headOffset);

                    if (*state == LockedToBeConsumed)
                        continue; // some other receiver got to this message before us

                    // is the message still being written/created?
                    if (*state != ReadyToBeConsumed)
                        continue; // message is not ready to be consumed yet

                    // take a lock so no other thread can start processing this message
                    if (Interlocked.CompareExchange(ref *state, LockedToBeConsumed, ReadyToBeConsumed) != ReadyToBeConsumed)
                        continue; // some other receiver got to this message before us

                    // read the message body from the queue buffer
                    var bodyOffset = GetMessageBodyOffset(headOffset);
                    var bodyLength = ReadMessageBodyLength(headOffset);
                    message = Buffer.Read(bodyOffset, bodyLength, resultBuffer);

                    // zero out the entire message block
                    var messageLength = GetMessageLength(bodyLength);
                    Buffer.ZeroBlock(headOffset, messageLength);

                    // updating the queue header to point the head of the queue to the next available message
                    var newHeadOffset = SafeIncrementMessageOffset(headOffset, messageLength);
                    var currentHeadOffset = (long*)header;
                    Interlocked.Exchange(ref *currentHeadOffset, newHeadOffset);

                    return true;
                }
            }
            finally
            {
                countdownEvent.Signal();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private long ReadMessageBodyLength(long messageHeaderOffset)
            => Buffer.ReadInt64(messageHeaderOffset + sizeof(long));
    }
}
