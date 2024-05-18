using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Douban.Provider;

public class PersonImageProvider(DoubanApi api, ILogger<PersonImageProvider> logger) : IRemoteImageProvider, IHasOrder
{
    public int Order => 0;
    public string Name => Constants.ProviderName;

    public bool Supports(BaseItem item)
    {
        return item is Person;
    }

    public IEnumerable<ImageType> GetSupportedImages(BaseItem item)
    {
        return [ImageType.Primary];
    }

    public async Task<IEnumerable<RemoteImageInfo>> GetImages(BaseItem item, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var images = new List<RemoteImageInfo>();

        var pid = await Helper.ParseDoubanPersonageId(item, api, token);
        if (pid == 0) { return images; }

        var subject = await api.FetchPersonByPersonageId(pid.ToString(), token);

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
        logger.LogDebug("Fetching image: {url}", url);
        return await api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}
