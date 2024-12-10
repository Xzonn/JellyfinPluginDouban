using Jellyfin.Plugin.Douban.Model;
using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.Douban.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Cookie
    /// </summary>
    public string DoubanCookie { get; set; } = string.Empty;

    /// <summary>
    /// 豆瓣图片服务器
    /// </summary>
    public string CdnServer { get; set; } = Helper.DEFAULT_CDN_SERVER;

    /// <summary>
    /// 请求时间间隔
    /// </summary>
    public int RequestTimeSpan { get; set; } = 2000;

    /// <summary>
    /// 超时时间
    /// </summary>
    public int Timeout { get; set; } = 8000;

    /// <summary>
    /// 图片排序方式
    /// </summary>
    public ImageSortingMethod ImageSortingMethod { get; set; } = ImageSortingMethod.Default;

    /// <summary>
    /// 根据宽高比区分海报和背景图
    /// </summary>
    public bool DistinguishUsingAspectRatio { get; set; } = Helper.DEFAULT_DISTINGUISH_USING_ASPECT_RATIO;

    /// <summary>
    /// 获取图片时获取剧照
    /// </summary>
    public bool FetchStagePhoto { get; set; } = Helper.DEFAULT_FETCH_STAGE_PHOTO;

    /// <summary>
    /// 在影视页面获取演职员照片
    /// </summary>
    public bool FetchCelebrityImages { get; set; } = Helper.DEFAULT_FETCH_CELEBRITY_IMAGES;

    /// <summary>
    /// 优化对第一季的搜索
    /// </summary>
    public bool OptimizeForFirstSeason { get; set; } = Helper.DEFAULT_OPTIMIZE_FOR_FIRST_SEASON;

    /// <summary>
    /// 强制将剧集首季作为系列信息
    /// </summary>
    public bool ForceSeriesAsFirstSeason { get; set; } = Helper.DEFAULT_FORCE_SERIES_AS_FIRST_SEASON;

    /// <summary>
    /// 采用豆瓣的单集信息
    /// </summary>
    public bool UseEpisodeInformation { get; set; } = Helper.DEFAULT_USE_EPISODE_INFORMATION;

    /// <summary>
    /// 对不存在标题的单集采用自动生成的标题
    /// </summary>
    public bool UseAutomaticalEpisodeTitles { get; set; } = Helper.DEFAULT_USE_AUTOMATICAL_EPISODE_TITLES;
}
