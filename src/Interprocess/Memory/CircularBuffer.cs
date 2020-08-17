using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cloudtoid.Interprocess
{
    internal unsafe sealed class CircularBuffer
    {
        private readonly byte* buffer;

        internal CircularBuffer(byte* buffer, long capacity)
        {
            this.buffer = buffer;
            Capacity = capacity;
        }

        internal long Capacity
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal byte* GetPointer(long offset)
        {
            AdjustedOffset(ref offset);
            return buffer + offset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal long ReadInt64(long offset)
        {
            AdjustedOffset(ref offset);
            Debug.Assert(offset + sizeof(long) <= Capacity);
            return *(long*)(buffer + offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal ReadOnlyMemory<byte> Read(long offset, long length, Memory<byte>? resultBuffer = null)
        {
            if (length == 0)
                return ReadOnlyMemory<byte>.Empty;

            var result = resultBuffer ?? new byte[length];
            if (length > result.Length)
                length = result.Length;

            AdjustedOffset(ref offset);
            using (var pinnedResultBuffer = result.Pin())
            {
                var resultBuffeerPtr = (byte*)pinnedResultBuffer.Pointer;
                var sourcePtr = buffer + offset;

                var rightLength = Math.Min(Capacity - offset, length);
                if (rightLength > 0)
                    Buffer.MemoryCopy(sourcePtr, resultBuffeerPtr, rightLength, rightLength);

                var leftLength = length - rightLength;
                if (leftLength > 0)
                    Buffer.MemoryCopy(buffer, resultBuffeerPtr + rightLength, leftLength, leftLength);
            }

            return result.Slice(0, (int)length);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void WriteInt64(long value, long offset)
        {
            AdjustedOffset(ref offset);
            Debug.Assert(offset + sizeof(long) <= Capacity);
            *(long*)(buffer + offset) = value;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Write(ReadOnlySpan<byte> source, long offset)
        {
            fixed (byte* sourcePtr = &MemoryMarshal.GetReference(source))
                Write(sourcePtr, source.Length, offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void Write<T>(T source, long offset) where T : struct
        {
            Write((byte*)Unsafe.AsPointer(ref source), Unsafe.SizeOf<T>(), offset);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void Write(byte* sourcePtr, long sourceLength, long offset)
        {
            if (sourceLength == 0)
                return;

            AdjustedOffset(ref offset);
            var rightLength = Math.Min(Capacity - offset, sourceLength);
            Buffer.MemoryCopy(sourcePtr, buffer + offset, rightLength, rightLength);

            var leftLength = sourceLength - rightLength;
            if (leftLength > 0)
                Buffer.MemoryCopy(sourcePtr + rightLength, buffer, leftLength, leftLength);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void ZeroBlock(long offset, long length)
        {
            if (length == 0)
                return;

            AdjustedOffset(ref offset);
            var rightLength = Math.Min(Capacity - offset, length);
            Unsafe.InitBlock(buffer + offset, 0, (uint)rightLength);

            var leftLength = length - rightLength;
            if (leftLength > 0)
                Unsafe.InitBlock(buffer, 0, (uint)leftLength);
        }

        // internal for testing
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AdjustedOffset(ref long offset)
            => offset %= Capacity;
    }
}
