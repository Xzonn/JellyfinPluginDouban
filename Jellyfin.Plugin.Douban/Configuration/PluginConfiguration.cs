using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Douban.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string Cookie { get; set; } = string.Empty;

    public int RequestTimeSpan { get; set; } = 2000;
}
