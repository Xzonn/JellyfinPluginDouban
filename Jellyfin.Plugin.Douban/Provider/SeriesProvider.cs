﻿using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Jellyfin.Plugin.Douban.Provider;

public class SeriesProvider : IRemoteMetadataProvider<Series, SeriesInfo>, IHasOrder
{
    private readonly DoubanApi _api;
    private readonly ILogger<SeriesProvider> _log;

    public SeriesProvider(DoubanApi api, ILogger<SeriesProvider> logger)
    {
        _api = api;
        _log = logger;
    }

    public int Order => 0;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Series>> GetMetadata(SeriesInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _log.LogDebug($"SeriesInfo: {JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions)}");
        var result = new MetadataResult<Series> { ResultLanguage = Constants.Language };

        var subject = await _api.FetchMovie(info, token);
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
            result.Item.AirDays = new[] { (subject.ScreenTime ?? new DateTime()).DayOfWeek };
        }
        result.Item.SetProviderId(Constants.ProviderId, subject.Sid);
        if (!string.IsNullOrEmpty(subject.ImdbId)) { result.Item.SetProviderId(MetadataProvider.Imdb, subject.ImdbId); }
        result.QueriedById = true;
        result.HasMetadata = true;

        (await _api.FetchMovieCelebrities(subject.Sid!, token)).ForEach(_ => result.AddPerson(_));

        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeriesInfo info, CancellationToken token)
    {
        _log.LogDebug($"SeriesInfo: {JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions)}");
        return await _api.GetMovieSearchResults(info, true, token);
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        _log.LogDebug($"Fetching image: {url}");
        return await _api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}
