using System.Diagnostics;

namespace Cloudtoid.Interprocess;

// Inspired by https://github.com/dotnet/aspnetcore/blob/main/src/Shared/ValueStopwatch/ValueStopwatch.cs
internal readonly struct ValueStopwatch
{
    private readonly long start;
    private ValueStopwatch(long start) => this.start = start;
    public readonly bool IsActive => start != 0;
    public static ValueStopwatch StartNew() => new(Stopwatch.GetTimestamp());

    public readonly TimeSpan GetElapsedTime()
    {
        // Start timestamp can't be zero in an initialized ValueStopwatch. It would have to be literally the first thing executed when the machine boots to be 0.
        // So it being 0 is a clear indication of default(ValueStopwatch)
        if (!IsActive)
        {
            throw new InvalidOperationException(
                "An uninitialized, or 'default', ValueStopwatch cannot be used to get elapsed time.");
        }

        return Stopwatch.GetElapsedTime(start);
    }
}