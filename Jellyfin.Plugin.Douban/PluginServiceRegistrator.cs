#if NET8_0_OR_GREATER
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
#else
using MediaBrowser.Common.Plugins;
#endif
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Douban;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
#if NET8_0_OR_GREATER
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost serverApplicationHost)
#else
    public void RegisterServices(IServiceCollection serviceCollection)
#endif
    {
        serviceCollection.AddSingleton<DoubanApi>();
    }
}
