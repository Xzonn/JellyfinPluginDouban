#if NET9_0_OR_GREATER
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;

namespace Jellyfin.Plugin.Douban.Extension;

public class ExternalUrlProvider : IExternalUrlProvider
{
    public string Name => Constants.ProviderName;

    public IEnumerable<string> GetExternalUrls(BaseItem item)
    {
        var id = item.GetProviderId(Constants.ProviderId);
        if (id == null)
            yield break;

        switch (item)
        {
            case Movie:
            case Series:
            case Season:
            case Episode:
                yield return $"https://movie.douban.com/subject/{id}/";
                break;
            case Person:
                yield return $"https://www.douban.com/personage/{id}/";
                break;
            default:
                yield break;
        }
    }
}
#endif
