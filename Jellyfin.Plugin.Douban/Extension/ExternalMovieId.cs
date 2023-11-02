using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Douban.Extension;

public class ExternalMovieId : IExternalId
{
    public bool Supports(IHasProviderIds item) => item is Movie;

    public string ProviderName => Constants.ProviderName;

    public string Key => Constants.ProviderId;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Movie;

    public string UrlFormatString => "https://movie.douban.com/subject/{0}/";
}
