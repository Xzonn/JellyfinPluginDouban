using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Text.Json;

using static AnitomySharp.Element;

namespace Jellyfin.Plugin.Douban.Provider;

public class EpisodeProvider(DoubanApi api, ILogger<EpisodeProvider> logger) : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
{
    public int Order => 0;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        logger.LogDebug("EpisodeInfo: {info:l}", JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions));
        var result = new MetadataResult<Episode> { ResultLanguage = Constants.Language };

        var movie = await api.FetchMovie(info, token);
        if (string.IsNullOrEmpty(movie.Sid)) { return result; }

        var index = 0;
        var fileName = Path.GetFileName(info.Path);
        var indexString = Helper.AnitomySharpParse(fileName, ElementCategory.ElementEpisodeNumber);
        if (!string.IsNullOrEmpty(indexString)) { index = int.Parse(indexString); }
        if (index == 0) { index = info.IndexNumber ?? 0; }
        if (index == 0 || index > movie.EpisodeCount) { return result; }

        var subject = await api.FetchMovieEpisode(movie.Sid, index, token);
        if (string.IsNullOrEmpty(subject.Name)) { return result; }

        result.Item = new Episode()
        {
            Name = subject.Name,
            OriginalTitle = subject.OriginalName,
            IndexNumber = index,
            Overview = subject.Intro,
            PremiereDate = subject.ScreenTime,
            ParentIndexNumber = movie.SeasonIndex,
        };
        result.Item.SetProviderId(Constants.ProviderId, $"{movie.Sid}/episode/{index}");
        result.QueriedById = true;
        result.HasMetadata = true;

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo info, CancellationToken token)
    {
        logger.LogDebug("EpisodeInfo: {info:l}", JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions));
        throw new NotImplementedException();
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        logger.LogDebug("Fetching image: {url}", url);
        return await api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}
