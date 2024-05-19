using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Jellyfin.Plugin.Douban.Provider;

public class SeriesProvider(DoubanApi api, ILogger<SeriesProvider> logger) : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
{
    public int Order => 0;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        logger.LogDebug("SeriesInfo: {info:l}", JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions));
        var result = new MetadataResult<Series> { ResultLanguage = Constants.Language };

        var subject = await api.FetchMovie(info, token);
        if (string.IsNullOrEmpty(subject.Sid)) { return result; }

        result.Item = new Series()
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
            AirTime = "",
        };
        if (subject.ScreenTime != null)
        {
            result.Item.AirTime = subject.ScreenTime?.ToString("yyyy-MM-dd");
            result.Item.AirDays = [(subject.ScreenTime ?? new DateTime()).DayOfWeek];
        }
        result.Item.SetProviderId(Constants.ProviderId, subject.Sid);
        if (!string.IsNullOrEmpty(subject.ImdbId)) { result.Item.SetProviderId(MetadataProvider.Imdb, subject.ImdbId); }
        result.QueriedById = true;
        result.HasMetadata = true;

        (await api.FetchMovieCelebrities(subject.Sid!, token)).ForEach(result.AddPerson);

        logger.LogDebug("Metadata: {info:l}", Helper.ConvertMetadataToJson(result.Item));

        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo info, CancellationToken token)
    {
        logger.LogDebug("SeriesInfo: {info:l}", JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions));
        return await api.GetMovieSearchResults(info, true, token);
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        logger.LogDebug("Fetching image: {url}", url);
        return await api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}
