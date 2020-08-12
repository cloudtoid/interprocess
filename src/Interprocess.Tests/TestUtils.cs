using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess.Tests
{
    internal static class TestUtils
    {
        internal static ILoggerFactory LoggerFactory { get; }
            = Microsoft.Extensions.Logging.LoggerFactory.Create(builder => builder.AddConsole());

        internal static IQueueFactory QueueFactory { get; }
            = new QueueFactory(LoggerFactory);
    }
}
