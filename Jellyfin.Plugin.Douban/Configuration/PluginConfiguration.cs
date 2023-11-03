using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Douban.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string DoubanCookie { get; set; } = string.Empty;

    public int RequestTimeSpan { get; set; } = 2000;

    public bool DistinguishUsingAspectRatio { get; set; } = true;

    public bool FetchStagePhoto { get; set; } = false;
}
