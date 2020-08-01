using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Cloudtoid.SharedMemory
{
    internal unsafe sealed class CircularBuffer
    {
        private readonly byte* buffer;

        internal CircularBuffer(byte* buffer, long capacity)
        {
            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (capacity <= 0)
                throw new ArgumentException($"{nameof(capacity)} must be greater than 0.", nameof(capacity));

            this.buffer = buffer;
            Capacity = capacity;
        }

        internal long Capacity { get; }

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
        internal byte[] Read(long offset, long length)
        {
            if (length == 0)
                return Array.Empty<byte>();

            AdjustedOffset(ref offset);

            var result = new byte[length];
            fixed (byte* resultPtr = &result[0])
            {
                var sourcePtr = buffer + offset;

                var rightLength = Math.Min(Capacity - offset, length);
                if (rightLength > 0)
                    Buffer.MemoryCopy(sourcePtr, resultPtr, rightLength, rightLength);

                var leftLength = length - rightLength;
                if (leftLength > 0)
                    Buffer.MemoryCopy(buffer, resultPtr + rightLength, leftLength, leftLength);
            }
            return result;
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

        // internal for testing
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal void AdjustedOffset(ref long offset)
            => offset %= Capacity;
    }
}
