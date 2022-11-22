using System.Linq;
using FluentAssertions;
using Xunit;

namespace Cloudtoid.Interprocess.Tests
{
    public unsafe class CircularBufferTests
    {
        private static readonly byte[] ByteArray1 = new byte[] { 100, };
        private static readonly byte[] ByteArray2 = new byte[] { 100, 110 };
        private static readonly byte[] ByteArray3 = new byte[] { 100, 110, 120 };

        [Theory]
        [InlineData(new byte[] { 100 }, 0, 0)]
        [InlineData(new byte[] { 100 }, 1, 0)]
        [InlineData(new byte[] { 100 }, 2, 0)]
        [InlineData(new byte[] { 100 }, 3, 0)]
        [InlineData(new byte[] { 100, 110 }, 0, 0)]
        [InlineData(new byte[] { 100, 110 }, 1, 1)]
        [InlineData(new byte[] { 100, 110 }, 2, 0)]
        [InlineData(new byte[] { 100, 110 }, 3, 1)]
        public void CanAdjustOffset(byte[] bytes, long offset, long adjustedOffset)
        {
            fixed (byte* bytesPtr = &bytes[0])
            {
                var buffer = new CircularBuffer(bytesPtr, bytes.Length);
                buffer.Capacity.Should().Be(bytes.Length);
                buffer.AdjustedOffset(ref offset);
                offset.Should().Be(adjustedOffset);
            }
        }

        [Theory]
        [InlineData(new byte[] { 100 }, 0, 100)]
        [InlineData(new byte[] { 100 }, 1, 100)]
        [InlineData(new byte[] { 100 }, 2, 100)]
        [InlineData(new byte[] { 100 }, 3, 100)]
        [InlineData(new byte[] { 100, 110 }, 0, 100)]
        [InlineData(new byte[] { 100, 110 }, 1, 110)]
        [InlineData(new byte[] { 100, 110 }, 2, 100)]
        [InlineData(new byte[] { 100, 110 }, 3, 110)]
        public void CanGetPointer(byte[] bytes, long offset, byte expectedValue)
        {
            fixed (byte* bytesPtr = &bytes[0])
            {
                var buffer = new CircularBuffer(bytesPtr, bytes.Length);
                buffer.Capacity.Should().Be(bytes.Length);
                var b = *buffer.GetPointer(offset);
                b.Should().Be(expectedValue);
            }
        }

        [Theory]
        [InlineData(0, 0, new byte[] { })]
        [InlineData(0, 1, new byte[] { 100 })]
        [InlineData(1, 1, new byte[] { 110 })]
        [InlineData(2, 1, new byte[] { 120 })]
        [InlineData(3, 1, new byte[] { 100 })]
        [InlineData(0, 2, new byte[] { 100, 110 })]
        [InlineData(1, 2, new byte[] { 110, 120 })]
        [InlineData(2, 2, new byte[] { 120, 100 })]
        [InlineData(3, 2, new byte[] { 100, 110 })]
        [InlineData(0, 3, new byte[] { 100, 110, 120 })]
        [InlineData(1, 3, new byte[] { 110, 120, 100 })]
        [InlineData(2, 3, new byte[] { 120, 100, 110 })]
        [InlineData(3, 3, new byte[] { 100, 110, 120 })]
        [InlineData(0, 4, new byte[] { 100, 110, 120, 100 })]
        [InlineData(1, 4, new byte[] { 110, 120, 100, 110 })]
        [InlineData(0, 0, new byte[] { }, 1)]
        [InlineData(1, 4, new byte[] { 110 }, 1)]
        [InlineData(1, 2, new byte[] { 110, 120 }, 6)]
        public void CanRead(long offset, int length, byte[] expectedResult, int? bufferLength = null)
        {
            fixed (byte* bytesPtr = &ByteArray3[0])
            {
                var buffer = new CircularBuffer(bytesPtr, ByteArray3.Length);
                if (bufferLength is null)
                    buffer.Read(offset, length).ToArray().Should().BeEquivalentTo(expectedResult);

                var resultBuffer = new byte[bufferLength ?? length];
                buffer.Read(offset, length, resultBuffer).ToArray().Should().BeEquivalentTo(expectedResult);
            }
        }

        [Theory]
        [InlineData(0, 0, new byte[] { })]
        [InlineData(0, 1, new byte[] { 100 })]
        [InlineData(1, 1, new byte[] { 110 })]
        [InlineData(2, 1, new byte[] { 120 })]
        [InlineData(3, 1, new byte[] { 100 })]
        [InlineData(0, 2, new byte[] { 100, 110 })]
        [InlineData(1, 2, new byte[] { 110, 120 })]
        [InlineData(2, 2, new byte[] { 120, 100 })]
        [InlineData(3, 2, new byte[] { 100, 110 })]
        [InlineData(0, 3, new byte[] { 100, 110, 120 })]
        [InlineData(1, 3, new byte[] { 110, 120, 100 })]
        [InlineData(2, 3, new byte[] { 120, 100, 110 })]
        [InlineData(3, 3, new byte[] { 100, 110, 120 })]
        public void CanWrite(long offset, long length, byte[] bytes)
        {
            var b = new byte[3];
            fixed (byte* ptr = &b[0])
            {
                var buffer = new CircularBuffer(ptr, b.Length);
                buffer.Write(bytes, offset);
                buffer.Read(offset, length).ToArray().Should().BeEquivalentTo(bytes);
            }
        }

        [Fact]
        public void CanWriteStruct()
        {
            var b = new byte[sizeof(QueueHeader)];
            fixed (byte* ptr = &b[0])
            {
                var buffer = new CircularBuffer(ptr, b.Length);
                var value = new QueueHeader { ReadOffset = 1, WriteOffset = 2 };
                buffer.Write(value, 0);
                value.Should().BeEquivalentTo(*(QueueHeader*)ptr);

                buffer.Write(value, 3);
                value.Should().BeEquivalentTo(*(QueueHeader*)(ptr + 3));
            }
        }

        [Theory]
        [InlineData(0, 0)]
        [InlineData(0, 1)]
        [InlineData(1, 1)]
        [InlineData(2, 1)]
        [InlineData(3, 1)]
        [InlineData(0, 2)]
        [InlineData(1, 2)]
        [InlineData(2, 2)]
        [InlineData(3, 2)]
        [InlineData(0, 3)]
        [InlineData(1, 3)]
        [InlineData(2, 3)]
        [InlineData(3, 3)]
        public void CanZeroBlock(long offset, long length)
        {
            var b = new byte[3] { 1, 1, 1 };
            fixed (byte* ptr = &b[0])
            {
                var buffer = new CircularBuffer(ptr, b.Length);
                buffer.Read(offset, length).ToArray().All(i => i == 1).Should().BeTrue();
                buffer.Clear(offset, length);
                buffer.Read(offset, length).ToArray().All(i => i == 0).Should().BeTrue();
            }
        }
    }
}
