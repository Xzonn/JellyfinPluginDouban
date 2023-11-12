using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Jellyfin.Plugin.Douban.Provider;

public class SeasonProvider : IRemoteMetadataProvider<Season, SeasonInfo>, IHasOrder
{
    private readonly DoubanApi _api;
    private readonly ILogger<SeasonProvider> _log;

    public SeasonProvider(DoubanApi api, ILogger<SeasonProvider> logger)
    {
        _api = api;
        _log = logger;
    }

    public int Order => 0;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _log.LogDebug($"SeasonInfo: {JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions)}");
        var result = new MetadataResult<Season> { ResultLanguage = Constants.Language };

        var subject = await _api.FetchMovie(info, token);
        if (string.IsNullOrEmpty(subject.Sid)) { return result; }

        result.Item = new Season()
        {
            Name = subject.Name,
            OriginalTitle = subject.OriginalName,
            CommunityRating = (float?)subject.Rating,
            Overview = subject.Intro,
            ProductionYear = subject.Year,
            HomePageUrl = subject.Website,
            Genres = subject.Genres,
            Tags = subject.Tags,
            ProductionLocations = subject.Country,
            PremiereDate = subject.ScreenTime,
            IndexNumber = subject.SeasonIndex > 0 ? subject.SeasonIndex : 1,
        };
        result.Item.SetProviderId(Constants.ProviderId, subject.Sid);
        if (!string.IsNullOrEmpty(subject.ImdbId)) { result.Item.SetProviderId(MetadataProvider.Imdb, subject.ImdbId); }
        result.QueriedById = true;
        result.HasMetadata = true;

        (await _api.FetchMovieCelebrities(subject.Sid!, token)).ForEach(_ => result.AddPerson(_));

        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo info, CancellationToken token)
    {
        _log.LogDebug($"SeasonInfo: {JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions)}");
        return await _api.GetMovieSearchResults(info, true, token);
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        _log.LogDebug($"Fetching image: {url}");
        return await _api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}
