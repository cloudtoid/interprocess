using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Cloudtoid.Interprocess.Tests
{
    public class XunitLoggerProvider : ILoggerProvider
    {
        private readonly ITestOutputHelper testOutputHelper;
        private readonly string? fileName;

        public XunitLoggerProvider(ITestOutputHelper testOutputHelper, string? fileName = null)
        {
            this.testOutputHelper = testOutputHelper;
            this.fileName = fileName;
        }

        public ILogger CreateLogger(string categoryName)
            => new XunitLogger(testOutputHelper, fileName ?? categoryName, fileName);

        public void Dispose() { }
    }
}
