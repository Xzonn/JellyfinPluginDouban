using AnitomySharp;
using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Jellyfin.Plugin.Douban.Provider;

public class EpisodeProvider : IRemoteMetadataProvider<Episode, EpisodeInfo>, IHasOrder
{
    private readonly DoubanApi _api;
    private readonly ILogger<EpisodeProvider> _log;

    public EpisodeProvider(DoubanApi api, ILogger<EpisodeProvider> logger)
    {
        _api = api;
        _log = logger;
    }

    public int Order => 0;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _log.LogDebug($"EpisodeInfo: {JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions)}");
        var result = new MetadataResult<Episode> { ResultLanguage = Constants.Language };

        var movie = await _api.FetchMovie(info, token);
        if (string.IsNullOrEmpty(movie.Sid)) { return result; }

        var index = info.IndexNumber ?? 0;
        if (index == 0)
        {
            var fileName = Path.GetFileName(info.Path);
            var indexString = AnitomySharp.AnitomySharp.Parse(fileName).FirstOrDefault(p => p.Category == Element.ElementCategory.ElementEpisodeNumber)?.Value;
            if (!string.IsNullOrEmpty(indexString)) { index = int.Parse(indexString); }
        }
        if (index == 0) { return result; }
        var subject = await _api.FetchMovieEpisode(movie.Sid, index, token);
        if (string.IsNullOrEmpty(subject.Name)) { return result; }

        result.Item = new Episode()
        {
            Name = subject.Name,
            OriginalTitle = subject.OriginalName,
            IndexNumber = index,
            Overview = subject.Intro,
            PremiereDate = subject.ScreenTime,
        };
        result.Item.SetProviderId(Constants.ProviderId, $"{movie.Sid}/episode/{index}");
        result.QueriedById = true;
        result.HasMetadata = true;

        return result;
    }

    public Task<IEnumerable<RemoteSearchResult>> GetSearchResults(EpisodeInfo info, CancellationToken token)
    {
        _log.LogDebug($"EpisodeInfo: {JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions)}");
        throw new NotImplementedException();
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        _log.LogDebug($"Fetching image: {url}");
        return await _api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}
