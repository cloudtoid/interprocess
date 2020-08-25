using Microsoft.Extensions.DependencyInjection;
using static Cloudtoid.Contract;

namespace Cloudtoid.Interprocess
{
    public static class DependencyInjection
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
}
