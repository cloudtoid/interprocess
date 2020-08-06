using Cloudtoid.Interprocess.DomainSocket;
using FluentAssertions;
using System;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace Cloudtoid.Interprocess.Tests
{
    public class DomainSocketTests
    {
        [Fact]
        public void CanCreateUnixDomainSocket()
        {
            using var socket = UnixDomainSocketUtil.CreateUnixDomainSocket();
            socket.AddressFamily.Should().Be(AddressFamily.Unix);
            socket.SocketType.Should().Be(SocketType.Stream);
            socket.ProtocolType.Should().Be(ProtocolType.Unspecified);
        }

        [Fact]
        public void CanSafeDispose()
        {
            var socket = UnixDomainSocketUtil.CreateUnixDomainSocket();
            socket.SafeDispose();
            socket = null;
            socket.SafeDispose();
        }

        [Fact]
        public void CanSocketOperationTimeout()
        {
            using var source = new CancellationTokenSource();
            source.CancelAfter(200);
            Action action = () => UnixDomainSocketUtil.SocketOperation(
                _ => { },
                _ => true,
                source.Token);

            action.Should().ThrowExactly<OperationCanceledException>();
        }

        [Fact]
        public void CanSocketOperationCatchAndRethrowException()
        {
            using var source = new CancellationTokenSource();
            Action action = () => UnixDomainSocketUtil.SocketOperation<bool>(
                callback => callback(null!),
                _ => throw new NotSupportedException(),
                source.Token);

            action.Should().ThrowExactly<NotSupportedException>();
        }
    }
}
