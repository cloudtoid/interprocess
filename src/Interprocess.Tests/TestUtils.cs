using Microsoft.Extensions.Logging;
using Factory = Microsoft.Extensions.Logging.LoggerFactory;

namespace Cloudtoid.Interprocess.Tests
{
    internal static class TestUtils
    {
        internal static ILoggerFactory LoggerFactory { get; }
            = Factory.Create(builder => builder.AddConsole());

        internal static IQueueFactory QueueFactory { get; }
            = new QueueFactory(LoggerFactory);
    }
}
