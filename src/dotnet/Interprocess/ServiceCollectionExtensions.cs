using static Cloudtoid.Contract;

namespace Cloudtoid.Interprocess;

/// <summary>
/// Extensions to the <see cref="IServiceCollection"/> to register the shared-memory queue.
/// </summary>
public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers what is needed to create and consume shared-memory queues that are
    /// cross-process accessible.
    /// Use <see cref="IQueueFactory"/> to access the queue.
    /// </summary>
    public static IServiceCollection AddInterprocessQueue(this IServiceCollection services)
    {
        CheckValue(services, nameof(services));

        Util.Ensure64Bit();
        services.TryAddSingleton<IQueueFactory, QueueFactory>();
        return services;
    }
}