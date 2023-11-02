using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Douban.Extension;

public class ExternalSeasonId : IExternalId
{
    public bool Supports(IHasProviderIds item) => item is Season;

    public string ProviderName => Constants.ProviderName;

    public string Key => Constants.ProviderId;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Season;

    public string UrlFormatString => "https://movie.douban.com/subject/{0}/";
}
