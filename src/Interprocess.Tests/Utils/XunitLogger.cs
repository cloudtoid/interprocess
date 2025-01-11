using System;
using System.IO;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Cloudtoid.Interprocess.Tests
{
    public class XunitLogger : ILogger
    {
        private readonly ITestOutputHelper testOutputHelper;
        private readonly string categoryName;
        private readonly string? fileName;

        public XunitLogger(ITestOutputHelper testOutputHelper, string categoryName, string? fileName)
        {
            this.testOutputHelper = testOutputHelper;
            this.categoryName = categoryName;
            this.fileName = fileName;
        }

        public IDisposable BeginScope<TState>(TState state) where TState : notnull
            => NoopDisposable.Instance;

        public bool IsEnabled(LogLevel logLevel)
            => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var message = $"{categoryName} [{eventId}] {formatter(state, exception)}";
            testOutputHelper.WriteLine(message);
            if (exception != null)
                testOutputHelper.WriteLine(exception.ToString());

            LogToFile(message, exception);
        }

        private void LogToFile(string message, Exception? exception)
        {
            if (fileName is null)
                return;

            if (exception != null)
                message += Environment.NewLine + exception.ToString();

            while (true)
            {
                try
                {
                    File.AppendAllText(fileName, message);
                    break;
                }
                catch { }
            }
        }

        private sealed class NoopDisposable : IDisposable
        {
            public static readonly NoopDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
