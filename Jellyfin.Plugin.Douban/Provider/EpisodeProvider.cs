using Jellyfin.Plugin.Douban.Configuration;
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
    private static PluginConfiguration Configuration
    {
        get
        {
            if (Plugin.Instance != null) { return Plugin.Instance.Configuration; }
            return new PluginConfiguration();
        }
    }

    public int Order => 0;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Episode>> GetMetadata(EpisodeInfo info, CancellationToken token)
    {
        // Handle specials
        if (Helper.ParseIfSeasonIsSpecials(info, out var _))
        {
            return new MetadataResult<Episode>()
            {
                ResultLanguage = Constants.Language,
                Item = new Episode() { ParentIndexNumber = 0 },
                HasMetadata = true,
            };
        }

        token.ThrowIfCancellationRequested();
        logger.LogDebug("EpisodeInfo: {info:l}", JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions));
        logger.LogDebug("Index: {index}, parent index: {parent}, path: {path}", info.IndexNumber, info.ParentIndexNumber, info.Path);
        var result = new MetadataResult<Episode> { ResultLanguage = Constants.Language };

        var movie = await api.FetchMovie(info, token);
        if (string.IsNullOrEmpty(movie.Sid)) { return result; }

        var index = Helper.ParseDoubanEpisodeId(info) ?? 0;
        if (index == 0)
        {
            var fileName = Path.GetFileName(info.Path);
            var indexString = Helper.AnitomySharpParse(fileName, ElementCategory.ElementEpisodeNumber);
            if (indexString?.Length == 1)
            {
                // In most cases, the number of episodes in file name should be at least two digits (e.g., 01, 05).
                // If the number of episodes is only one digit, it is likely to be part of the series name.
                // e.g., [Sakurato] Hibike! Euphonium 3 [05][HEVC-10bit 1080p AAC][CHS&CHT].mkv
                var altIndex = Helper.AnitomySharpParse(fileName, ElementCategory.ElementEpisodeNumberAlt);
                if (altIndex?.Length > 1) { indexString = altIndex; }
            }
            if (!string.IsNullOrEmpty(indexString)) { int.TryParse(indexString, out index); }
        }
        if (index == 0) { index = info.IndexNumber ?? 0; }
        if (index == 0 || index > movie.EpisodeCount) { return result; }

        result.Item = new Episode()
        {
            IndexNumber = index,
            ParentIndexNumber = movie.SeasonIndex,
        };
        if (Configuration.UseEpisodeInformation)
        {
            var subject = await api.FetchMovieEpisode(movie.Sid, index, token);

            result.Item.Name = subject.Name;
            result.Item.OriginalTitle = subject.OriginalName;
            result.Item.Overview = subject.Intro;
            result.Item.PremiereDate = subject.ScreenTime;
        }
        result.Item.SetProviderId(Constants.ProviderId, $"{movie.Sid}/episode/{index}");
        result.QueriedById = true;
        result.HasMetadata = true;

        logger.LogDebug("Metadata: {info:l}", Helper.ConvertMetadataToJson(result.Item));

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
