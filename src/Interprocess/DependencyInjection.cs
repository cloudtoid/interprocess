using Microsoft.Extensions.DependencyInjection;
using static Cloudtoid.Contract;

namespace Cloudtoid.Interprocess
{
    public static class DependencyInjection
    {
        public static IServiceCollection AddInterprocessQueue(this IServiceCollection services)
        {
            CheckValue(services, nameof(services));

            Util.Ensure64Bit();
            services.TryAddSingleton<IQueueFactory, QueueFactory>();
            return services;
        }
    }
}
