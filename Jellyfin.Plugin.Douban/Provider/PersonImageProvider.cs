using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Douban.Provider;

public class PersonImageProvider : IRemoteImageProvider, IHasOrder
{
    private readonly DoubanApi _api;
    private readonly ILogger<PersonImageProvider> _log;

    public PersonImageProvider(DoubanApi api, ILogger<PersonImageProvider> logger)
    {
        _api = api;
        _log = logger;
    }

    public int Order => 0;
    public string Name => Constants.ProviderName;

    public bool Supports(BaseItem item)
    {
        return item is Person;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return new[] { ImageType.Primary };
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var images = new List<RemoteImageInfo>();

        if (!int.TryParse(item.GetProviderId(Constants.ProviderId), out var id) && !int.TryParse(item.GetProviderId(Constants.OddbId), out id))
        {
            return images;
        }

        var subject = await _api.FetchPerson(id.ToString(), token);

        if (subject != null && !string.IsNullOrEmpty(subject.PosterUrl))
        {
            var image = new RemoteImageInfo()
            {
                ProviderName = Constants.PluginName,
                Language = Constants.Language,
                ThumbnailUrl = subject.PosterUrl,
                Type = ImageType.Primary,
                Url = subject.PosterUrl,
            };
            images.Add(image);
        }

        return images;
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        _log.LogDebug($"Fetching image: {url}");
        return await _api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}
