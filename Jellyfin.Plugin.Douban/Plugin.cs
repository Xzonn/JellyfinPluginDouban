using Jellyfin.Plugin.Douban.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Douban;

public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public const string ProviderName = Constants.ProviderName;

    public const string ProviderId = Constants.ProviderId;

    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer) : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    public static Plugin? Instance { get; private set; }

    public override string Name => Constants.PluginName;

    public override Guid Id => Guid.Parse(Constants.PluginGuid);

    public IEnumerable<PluginPageInfo> GetPages() =>
    [
        new PluginPageInfo
        {
            Name = "Plugin.Douban.Configuration",
            DisplayName = "豆瓣设置",
            MenuIcon = "app_registration",
            EnableInMainMenu = true,
            EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.ConfigPage.html",
        },
    ];
}
