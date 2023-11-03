using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Entities.Movies;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Douban.Providers;

public class MovieImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly DoubanApi _api;
    private readonly ILogger<MovieImageProvider> _log;

    public MovieImageProvider(DoubanApi api, ILogger<MovieImageProvider> logger)
    {
        _api = api;
        _log = logger;
    }

    public int Order => 0;
    public string Name => Constants.ProviderName;

    public bool Supports(BaseItem item)
    {
        return item is Series or Season or Movie;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return new[] { ImageType.Primary, ImageType.Backdrop };
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var images = new List<RemoteImageInfo>();

        if (!int.TryParse(item.GetProviderId(Constants.ProviderId), out var id) && !int.TryParse(item.GetProviderId(Constants.OddbId), out id))
        {
            return images;
        }

        var subject = await _api.FetchMovie(id.ToString(), token);

        if (!string.IsNullOrEmpty(subject.PosterId))
        {
            var image = new RemoteImageInfo()
            {
                ProviderName = Constants.PluginName,
                Language = Constants.Language,
                ThumbnailUrl = $"https://img2.doubanio.com/view/photo/s/public/{subject.PosterId}.jpg",
                Type = ImageType.Primary,
                Url = $"https://img2.doubanio.com/view/photo/l/public/{subject.PosterId}.jpg",
            };
            images.Add(image);
        }
        var dict = new Dictionary<string, ImageType>()
        {
            ["R"] = ImageType.Primary,
            ["W"] = ImageType.Backdrop,
        };
        if(Plugin.Instance!.Configuration.FetchStagePhoto)
        {
            dict["S"] = ImageType.Backdrop;
        }
        foreach (var _ in dict)
        {
            (await _api.FetchMovieImages(id.ToString(), _.Key, _.Value, token)).ForEach(__ => images.Add(__));
        }

        return images;
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        _log.LogInformation($"Fetching image: {url}");
        return await _api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}
