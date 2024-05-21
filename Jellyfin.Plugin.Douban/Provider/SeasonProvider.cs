using MediaBrowser.Controller.Entities.TV;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Jellyfin.Plugin.Douban.Provider;

public class SeasonProvider(DoubanApi api, ILogger<SeasonProvider> logger) : IRemoteMetadataProvider<Season, SeasonInfo>, IHasOrder
{
    public int Order => 0;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Season>> GetMetadata(SeasonInfo info, CancellationToken token)
    {
        // Season does not contain sub folders, and info was automatically generated
        if (info.IndexNumber is null && string.IsNullOrEmpty(info.Path)) { return new MetadataResult<Season>(); }

        // Handle specials
        if (Helper.ParseIfSeasonIsSpecials(info, out var folderName))
        {
            return new MetadataResult<Season>()
            {
                ResultLanguage = Constants.Language,
                Item = new Season() { Name = folderName, IndexNumber = 0 },
                HasMetadata = true,
            };
        }

        token.ThrowIfCancellationRequested();
        logger.LogDebug("SeasonInfo: {info:l}", JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions));
        logger.LogDebug("Index: {index}, parent index: {parent}, path: {path}", info.IndexNumber, info.ParentIndexNumber, info.Path);

        var result = new MetadataResult<Season> { ResultLanguage = Constants.Language };

        var subject = await api.FetchMovie(info, token);
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
            IndexNumber = subject.SeasonIndex,
        };
        result.Item.SetProviderId(Constants.ProviderId, subject.Sid);
        if (!string.IsNullOrEmpty(subject.ImdbId)) { result.Item.SetProviderId(MetadataProvider.Imdb, subject.ImdbId); }
        result.QueriedById = true;
        result.HasMetadata = true;

        (await api.FetchMovieCelebrities(subject.Sid!, token)).ForEach(result.AddPerson);

        logger.LogDebug("Metadata: {info:l}", Helper.ConvertMetadataToJson(result.Item));

        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(SeasonInfo info, CancellationToken token)
    {
        logger.LogDebug("SeasonInfo: {info:l}", JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions));
        return await api.GetMovieSearchResults(info, true, token);
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        logger.LogDebug("Fetching image: {url}", url);
        return await api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}
