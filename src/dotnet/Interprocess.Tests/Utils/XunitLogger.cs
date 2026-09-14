namespace Cloudtoid.Interprocess.Tests;

public class XunitLogger(ITestOutputHelper testOutputHelper, string categoryName, string? fileName) : ILogger
{
    public IDisposable BeginScope<TState>(TState state)
        where TState : notnull =>
        NoopDisposable.Instance;

    public bool IsEnabled(LogLevel logLevel) =>
        true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        var message = $"{categoryName} [{eventId}] {formatter(state, exception)}";
        testOutputHelper.WriteLine(message);
        if (exception is not null)
            testOutputHelper.WriteLine(exception.ToString());

        LogToFile(message, exception);
    }

    private void LogToFile(string message, Exception? exception)
    {
        if (fileName is null)
            return;

        if (exception is not null)
            message += Environment.NewLine + exception;

        while (true)
        {
            try
            {
                File.AppendAllText(fileName, message);
                break;
            }
            catch
            {
            }
        }
    }

    private sealed class NoopDisposable : IDisposable
    {
        public static readonly NoopDisposable Instance = new();
        public void Dispose() { }
    }
}