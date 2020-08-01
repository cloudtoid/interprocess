using FluentAssertions;
using System;
using System.Linq;
using Xunit;

namespace Cloudtoid.Interprocess.Tests
{
    public unsafe class CircularBufferTests
    {
        private static readonly byte[] byteArray1 = new byte[] { 100, };
        private static readonly byte[] byteArray2 = new byte[] { 100, 110 };
        private static readonly byte[] byteArray3 = new byte[] { 100, 110, 120 };

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

        [Fact]
        public void CanReadInt64()
        {
            var bytes = BitConverter.GetBytes(100L).Concat(BitConverter.GetBytes(long.MaxValue)).ToArray();

            fixed (byte* bytesPtr = &bytes[0])
            {
                var buffer = new CircularBuffer(bytesPtr, bytes.Length);
                buffer.ReadInt64(0).Should().Be(100L);
                buffer.ReadInt64(8).Should().Be(long.MaxValue);
                buffer.ReadInt64(16).Should().Be(100L);
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
        public void CanRead(long offset, long length, byte[] expectedResult)
        {
            fixed (byte* bytesPtr = &byteArray3[0])
            {
                var buffer = new CircularBuffer(bytesPtr, byteArray3.Length);
                buffer.Read(offset, length).Should().BeEquivalentTo(expectedResult);
            }
        }

        [Fact]
        public void CanWriteInt64()
        {
            var bytes = new byte[16];

            fixed (byte* bytesPtr = &bytes[0])
            {
                var buffer = new CircularBuffer(bytesPtr, bytes.Length);
                buffer.WriteInt64(100L, 0);
                buffer.WriteInt64(long.MaxValue, 8);

                buffer.ReadInt64(0).Should().Be(100L);
                buffer.ReadInt64(8).Should().Be(long.MaxValue);
                buffer.ReadInt64(16).Should().Be(100L);

                buffer.WriteInt64(200L, 16);
                buffer.ReadInt64(16).Should().Be(200L);
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
                buffer.Read(offset, length).Should().BeEquivalentTo(bytes);
            }
        }

        [Fact]
        public void CanWriteStruct()
        {
            var b = new byte[sizeof(QueueHeader)];
            fixed (byte* ptr = &b[0])
            {
                var buffer = new CircularBuffer(ptr, b.Length);
                var value = new QueueHeader { HeadOffset = 1, TailOffset = 2 };
                buffer.Write(value, 0);
                value.Should().BeEquivalentTo(*(QueueHeader*)ptr);

                buffer.Write(value, 3);
                value.Should().BeEquivalentTo(*(QueueHeader*)(ptr + 3));
            }
        }

        [Fact]
        public void ConstructorThrowsWithBadArguments()
        {
            Action action = () => new CircularBuffer(null, 10);
            action.Should().ThrowExactly<ArgumentNullException>();

            action = () =>
            {
                fixed (byte* ptr = &byteArray1[0])
                    new CircularBuffer(ptr, 0);
            };
            action.Should().ThrowExactly<ArgumentException>("*capacity*");
        }
    }
}
