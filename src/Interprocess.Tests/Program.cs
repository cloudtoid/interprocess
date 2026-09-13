#pragma warning disable IDE0210 // Explicit entry point distinguishes the child worker from the test SDK entry point.

namespace Cloudtoid.Interprocess.Tests;

// Run a real queue participant in a child process for lifecycle tests.
internal static class Program
{
    private static async Task Main(string[] args)
    {
        var options = new QueueOptions(args[1], args[0], 1024);
        if (args[2] == "publisher-disposal")
            await PublisherDisposalTests.RunChildAsync(options, args[3]);
        else
            RunParticipant(options, args[2]);
    }

    private static void RunParticipant(QueueOptions options, string role)
    {
        var factory = new QueueFactory();
        using var participant = role == "publisher"
            ? (IDisposable)factory.CreatePublisher(options)
            : factory.CreateSubscriber(options);
        Console.WriteLine("ready");
        while (Console.ReadLine() is { } command)
        {
            if (command == "process-exit")
                Environment.Exit(0);

            if (command == "exit")
                return;

            if (participant is IPublisher publisher)
                Console.WriteLine(publisher.TryEnqueue("*"u8) ? "sent" : "full");
            else if (participant is ISubscriber subscriber)
                Console.WriteLine(subscriber.TryDequeue(out _) ? "received" : "empty");
        }
    }
}