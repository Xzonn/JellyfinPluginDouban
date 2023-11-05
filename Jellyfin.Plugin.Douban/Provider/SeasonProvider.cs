using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;

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
            Genres = subject.Genre,
            ProductionLocations = subject.Country,
            PremiereDate = subject.ScreenTime,
            IndexNumber = subject.SeasonIndex,
        };
        result.Item.SetProviderId(Constants.ProviderId, subject.Sid);
        if (!string.IsNullOrEmpty(subject.ImdbId)) { result.Item.SetProviderId(MetadataProvider.Imdb, subject.ImdbId); }
        result.QueriedById = true;
        result.HasMetadata = true;

        (await _api.FetchMovieCelebrities(subject.Sid!, token)).ForEach(_ => result.AddPerson(_));

        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo searchInfo, CancellationToken token)
    {
        return await _api.GetMovieSearchResults(searchInfo, token);
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        _log.LogInformation($"Fetching image: {url}");
        return await _api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}
