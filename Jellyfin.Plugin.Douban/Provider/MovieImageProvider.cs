using Jellyfin.Plugin.Douban.Configuration;
using Jellyfin.Plugin.Douban.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Douban.Provider;

public class MovieImageProvider(DoubanApi api, ILogger<MovieImageProvider> logger) : IRemoteImageProvider, IHasOrder
{
    private static PluginConfiguration Configuration
    {
        get
        {
            if (Plugin.Instance != null) { return Plugin.Instance.Configuration; }
            return new PluginConfiguration();
        }
    }

    public int Order => 0;
    public string Name => Constants.ProviderName;

    public bool Supports(BaseItem item)
    {
        return item is Series or Season or Movie;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return [ImageType.Primary, ImageType.Backdrop];
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var images = new List<RemoteImageInfo>();

        var id = Helper.ParseDoubanId(item);
        if (id == 0)
        {
            return images;
        }

        var subject = await api.FetchMovie(id.ToString(), token);

        if (!string.IsNullOrEmpty(subject.PosterId))
        {
            var image = new RemoteImageInfo()
            {
                ProviderName = Constants.PluginName,
                Language = Constants.Language,
                ThumbnailUrl = $"{Configuration.CdnServer}/view/photo/s/public/{subject.PosterId}.jpg",
                Type = ImageType.Primary,
                Url = $"{Configuration.CdnServer}/view/photo/l/public/{subject.PosterId}.jpg",
            };
            images.Add(image);
        }
        var dict = new Dictionary<string, ImageType>()
        {
            ["R"] = ImageType.Primary,
            ["W"] = ImageType.Backdrop,
        };
        if (Configuration.FetchStagePhoto)
        {
            dict["S"] = ImageType.Backdrop;
        }
        foreach (var _ in dict)
        {
            (await api.FetchMovieImages(id.ToString(), _.Key, _.Value, Configuration.ImageSortingMethod, token)).ForEach(images.Add);
        }

        return images;
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        logger.LogDebug("Fetching image: {url}", url);
        return await api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}
