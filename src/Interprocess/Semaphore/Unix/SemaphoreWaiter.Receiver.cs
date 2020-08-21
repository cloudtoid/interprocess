using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Cloudtoid.Interprocess.DomainSocket;
using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess.Semaphore.Unix
{
    internal sealed partial class SemaphoreWaiter
    {
        private sealed class Receiver : IDisposable
        {
            private readonly CancellationTokenSource cancellationSource = new CancellationTokenSource();
            private readonly ILogger<Receiver> logger;
            private readonly Thread thread;

            [SuppressMessage("Usage", "VSTHRD002:Avoid problematic synchronous waits", Justification = "Used in a creation of s thread.")]
            internal Receiver(string file, Action onMessage, ILoggerFactory loggerFactory)
            {
                logger = loggerFactory.CreateLogger<Receiver>();

                // using a dedicated thread as this is a very long blocking call
                thread = new Thread(() => ReceiveLoopAsync(file, onMessage, loggerFactory).Wait());
                thread.IsBackground = true;
                thread.Start();
            }

            public void Dispose()
            {
                cancellationSource.Cancel();
                thread.Join();
            }

            private async Task ReceiveLoopAsync(string file, Action onMessage, ILoggerFactory loggerFactory)
            {
                using var client = new UnixDomainSocketClient(file, loggerFactory);
                await Async.LoopTillCancelledAsync(
                    async cancellation =>
                    {
                        await client
                            .ReceiveAsync(MessageBuffer, cancellation)
                            .ConfigureAwait(false);

                        onMessage();
                        await Task.CompletedTask;
                    },
                    logger,
                    cancellationSource.Token)
                    .ConfigureAwait(false);
            }
        }
    }
}
