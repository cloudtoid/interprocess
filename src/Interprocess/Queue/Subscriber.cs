using System;
using System.Runtime.CompilerServices;
using System.Threading;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess
{
    internal sealed class Subscriber : Queue, ISubscriber
    {
        private const int BeingCreated = (int)MessageState.BeingCreated;
        private const int LockedToBeConsumed = (int)MessageState.LockedToBeConsumed;
        private const int ReadyToBeConsumed = (int)MessageState.ReadyToBeConsumed;
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
            countdownEvent.Dispose();

            signal.Dispose();
            cancellationSource.Dispose();
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
            message = ReadOnlyMemory<byte>.Empty;
            try
            {
                using var linkedSource = new LinkedCancellationToken(cancellationSource.Token, cancellation);
                cancellation = linkedSource.Token;

                while (true)
                {
                    cancellation.ThrowIfCancellationRequested();

                    Interlocked.MemoryBarrier();

                    var header = Header;
                    var headOffset = header->HeadOffset;

                    if (headOffset == header->TailOffset)
                        return false; // this is an empty queue

                    var state = *(int*)Buffer.GetPointer(headOffset);

                    // is the message still being written/created?
                    if (state == BeingCreated)
                        continue; // message is still being created

                    // take a lock so no other thread can start processing this message
                    if (Interlocked.CompareExchange(ref state, LockedToBeConsumed, ReadyToBeConsumed) != ReadyToBeConsumed)
                        return false; // some other receiver got to this message before us

                    // read the message body from the queue buffer
                    var bodyOffset = GetMessageBodyOffset(headOffset);
                    var bodyLength = ReadMessageBodyLength(headOffset);
                    message = Buffer.Read(bodyOffset, bodyLength, resultBuffer);

                    // zero out the entire message block
                    var messageLength = GetMessageLength(bodyLength);
                    Buffer.Clear(headOffset, messageLength);

                    // updating the queue header to point the head of the queue to the next available message
                    var newHeadOffset = SafeIncrementMessageOffset(headOffset, messageLength);
                    var currentHeadOffset = (long*)header;
                    if (Interlocked.CompareExchange(ref *currentHeadOffset, newHeadOffset, headOffset) != headOffset)
                        throw new Exception("This should never happen and is a bug if it does!");

                    return true;
                }
            }
            finally
            {
                countdownEvent.Signal();
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int ReadMessageBodyLength(long messageHeaderOffset)
            => Buffer.ReadInt32(messageHeaderOffset + sizeof(int));
    }
}
