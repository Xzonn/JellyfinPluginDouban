using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Douban.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string CdnServer { get; set; } = Helper.DEFAULT_CDN_SERVER;

    public string DoubanCookie { get; set; } = string.Empty;

    public int RequestTimeSpan { get; set; } = 2000;

    public bool DistinguishUsingAspectRatio { get; set; } = Helper.DEFAULT_DISTINGUISH_USING_ASPECT_RATIO;

    public bool FetchStagePhoto { get; set; } = Helper.DEFAULT_FETCH_STAGE_PHOTO;

    public bool FetchCelebrityImages { get; set; } = Helper.DEFAULT_FETCH_CELEBRITY_IMAGES;
}
