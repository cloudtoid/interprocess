using Microsoft.Extensions.Logging;

namespace Cloudtoid.Interprocess.Tests
{
    internal static class TestUtils
    {
        internal static ILogger<IQueueFactory> Logger { get; }
            = LoggerFactory.Create(builder => builder.AddConsole()).CreateLogger<QueueFactory>();

        internal static IQueueFactory QueueFactory { get; }
            = new QueueFactory(Logger);
    }
}
