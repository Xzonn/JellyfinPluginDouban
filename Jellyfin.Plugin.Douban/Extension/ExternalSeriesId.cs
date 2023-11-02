using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Douban.Extension;

public class ExternalSeriesId : IExternalId
{
    public bool Supports(IHasProviderIds item) => item is Series;

    public string ProviderName => Constants.ProviderName;

    public string Key => Constants.ProviderId;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Series;

    public string UrlFormatString => "https://movie.douban.com/subject/{0}/";
}
