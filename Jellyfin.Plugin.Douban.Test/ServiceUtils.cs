using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.Douban.Test
{
    class ServiceUtils
    {
        public static ServiceProvider BuildServiceProvider<T>(ITestOutputHelper output) where T : class
        {
            var services = new ServiceCollection()
                .AddHttpClient()
                .AddLogging(builder => builder.AddXUnit(output).SetMinimumLevel(LogLevel.Debug))
                .AddSingleton<DoubanApi>()
                .AddSingleton<T>();

            var serviceProvider = services.BuildServiceProvider();
            var oddbApiClient = serviceProvider.GetService<DoubanApi>();

            return serviceProvider;
        }
    }
}
