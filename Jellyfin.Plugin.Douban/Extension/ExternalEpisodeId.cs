using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Douban.Extension;

public class ExternalEpisodeId : IExternalId
{
    public bool Supports(IHasProviderIds item) => item is Episode;

    public string ProviderName => Constants.ProviderName;

    public string Key => Constants.ProviderId;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Episode;

    public string UrlFormatString => "https://movie.douban.com/subject/{0}/";
}
