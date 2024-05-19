using Emby.Naming.Video;
using HtmlAgilityPack;
using Jellyfin.Plugin.Douban.Configuration;
using Jellyfin.Plugin.Douban.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Text.RegularExpressions;

using static AnitomySharp.Element;

namespace Jellyfin.Plugin.Douban;

public partial class DoubanApi
{
    private struct Cache
    {
        public string content;
        public DateTime time;
    }

    private static Regex REGEX_AUTOMATIC_SEASON_NAME => new(@"^ *(?:第 *\d+ *季|Season \d+|未知季|Season Unknown|Specials) *$");
    private static Regex REGEX_PERSONAGE_ID => new(@"/personage/(\d+)");

    private readonly HttpClient _httpClient;
    public HttpClient GetHttpClient() => _httpClient;
    private readonly ILogger<DoubanApi> _log;
    private static DateTime LastSearch = DateTime.MinValue;
    private static TimeSpan TimeSpan => TimeSpan.FromMilliseconds(Configuration.RequestTimeSpan);
    private static DateTime CacheLastClean = DateTime.MinValue;
    private readonly Dictionary<string, Cache> Caches;
    private static PluginConfiguration Configuration
    {
        get
        {
            if (Plugin.Instance != null) { return Plugin.Instance.Configuration; }
            return new PluginConfiguration();
        }
    }

    public DoubanApi(IHttpClientFactory httpClientFactory, ILogger<DoubanApi> log)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://movie.douban.com/");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _log = log;
        CacheLastClean = DateTime.Now;
        Caches = [];
    }

    public async Task<List<ApiMovieSubject>> SearchMovie(string keyword, bool isMovie = true, bool isFirstSeason = false, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        keyword = keyword.Replace(".", " ").Trim();
        if (string.IsNullOrEmpty(keyword)) { return []; }

        _log.LogDebug("Searching movie: {keyword} (isMovie: {isMovie}, isFirstSeason: {isFirstSeason})", keyword, isMovie, isFirstSeason);
        string url = $"https://www.douban.com/search?cat=1002&q={Uri.EscapeDataString(keyword)}";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return []; }

        var results = Helper.ParseSearchMovieResults(responseText, keyword, isMovie, isFirstSeason);
        if (results.Count == 0)
        {
            _log.LogDebug("No results found for: {keyword}", keyword);
        }
        else
        {
            _log.LogDebug("{count} result(s) found for: {keyword}, first: {first}", results.Count, keyword, results[0].Name);
        }
        return results;
    }

    public async Task<List<RemoteSearchResult>> GetMovieSearchResults(ItemLookupInfo info, bool tryParse = true, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var searchResults = new List<ApiMovieSubject>();

        if (tryParse)
        {
            int id = Helper.ParseDoubanId(info);
            if (id != 0)
            {
                var subject = await FetchMovie(id.ToString(), token);
                if (subject != null)
                {
                    searchResults.Add(subject);
                }
            }
        }

        var infoName = (string.IsNullOrEmpty(info.Name) || REGEX_AUTOMATIC_SEASON_NAME.IsMatch(info.Name)) ? "" : info.Name;
        var isMovie = info is MovieInfo;

        var gussedSeason = Helper.GuessSeasonIndex(info);
        var isFirstSeason = gussedSeason < 2 && (gussedSeason == 1 || (info is SeriesInfo && gussedSeason == 0) || (info is SeasonInfo && (info.IndexNumber ?? 0) < 2));

        if (searchResults.Count == 0)
        {
            var names = new List<string?>();

            if (info is EpisodeInfo episodeInfo)
            {
                // For episode, DO NOT SEARCH NAME DIRECTLY
                names.Add(Helper.AnitomySharpParse(Path.GetFileName(info.Path), ElementCategory.ElementAnimeTitle));
                names.Add(Path.GetFileName(Path.GetDirectoryName(info.Path)));
            }
            else
            {
                names.Add(info.GetProviderId(MetadataProvider.Imdb));
                if (info is SeasonInfo seasonInfo && isFirstSeason)
                {
                    names.Add(seasonInfo.SeriesProviderIds.GetValueOrDefault(MetadataProvider.Imdb.ToString()));
                }
                names.Add(infoName);
                names.Add(info.OriginalTitle);

                if (info is SeasonInfo && ((info.IndexNumber ?? 0) > 1 || gussedSeason > 1))
                {
                    var season = (info.IndexNumber ?? 0) > 1 ? (info.IndexNumber ?? 0) : gussedSeason;
                    var parentId = Helper.ParseDoubanId(info, true);
                    if (parentId != 0)
                    {
                        var subject = await FetchMovie(parentId.ToString(), token);
                        if (!string.IsNullOrWhiteSpace(subject.Name))
                        {
                            names.Add(Helper.ReplaceSeasonIndexWith(info.Name, season));
                        }
                    }
                    if (!string.IsNullOrWhiteSpace(infoName))
                    {
                        names.Add(Helper.ReplaceSeasonIndexWith(infoName, season));
                    }
                }

                names.Add(Path.GetFileName(info.Path));
                names.Add(Helper.AnitomySharpParse(Path.GetFileName(info.Path), ElementCategory.ElementAnimeTitle));
            }

            var searchNames = new List<string>();
            foreach (var name in names)
            {
                if (string.IsNullOrWhiteSpace(name)) continue;
                // Season name is auto-generated, no need to search
                if ((info is SeasonInfo || info is EpisodeInfo) && REGEX_AUTOMATIC_SEASON_NAME.IsMatch(name)) continue;

                if ((info.Year ?? 0) > 0 && !new Regex($@"(?<![A-Za-z\d_]){info.Year}(?![A-Za-z\d_])").IsMatch(name))
                {
                    var yearName = $"{name} {info.Year}";
                    if (!searchNames.Contains(yearName)) { searchNames.Add(yearName); }
                }

                if (!searchNames.Contains(name)) { searchNames.Add(name); }

                VideoResolver.TryCleanString(infoName, new Emby.Naming.Common.NamingOptions(), out var newName);
                if (!string.IsNullOrEmpty(newName) && !searchNames.Contains(newName)) { searchNames.Add(newName); }

                newName = Helper.AnitomySharpParse(infoName, ElementCategory.ElementAnimeTitle);
                if (!string.IsNullOrEmpty(newName) && !searchNames.Contains(newName)) { searchNames.Add(newName); }
            }

            if (searchNames.Count == 0)
            {
                _log.LogDebug("Info type: {type}, cannot determine names for searching", info.GetType());
            }
            else
            {
                _log.LogDebug("Info type: {type}, names for searching: {names}", info.GetType(), string.Join(", ", searchNames));
            }

            foreach (var name in searchNames)
            {
                searchResults = await SearchMovie(name, isMovie, isFirstSeason, token);
                if (searchResults.Count > 0) { break; }
            }
        }

        if (searchResults.Count > 0 && isFirstSeason)
        {
            // If the name of search result contains "第x季" but x != 1, search for the first seasonMatch again
            var season = Helper.ParseSeasonIndex(searchResults[0].Name);
            if (season > 1)
            {
                searchResults = await SearchMovie(Helper.ReplaceSeasonIndexWith(searchResults[0].Name, 1), isMovie, isFirstSeason, token);
            }
        }

        var results = searchResults.Select(_ =>
        {
            var result = new RemoteSearchResult
            {
                Name = _.Name,
                SearchProviderName = _.OriginalName,
                ImageUrl = string.IsNullOrEmpty(_.PosterId) ? "" : $"{Configuration.CdnServer}/view/photo/l/public/{_.PosterId}.jpg",
                Overview = _.Intro,
                ProductionYear = _.Year,
                PremiereDate = _.ScreenTime,
            };
            result.SetProviderId(Constants.ProviderId, _.Sid);
            if (!string.IsNullOrEmpty(_.ImdbId)) { result.SetProviderId(MetadataProvider.Imdb, _.ImdbId); }
            return result;
        }).ToList();
        return results;
    }

    public async Task<ApiMovieSubject> FetchMovie(string sid, CancellationToken token = default)
    {
        _log.LogDebug("Fetching movie: {sid}", sid);
        string url = $"https://movie.douban.com/subject/{sid}/";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return new(); }

        var result = Helper.ParseMovie(responseText, sid);
        _log.LogDebug("Sid {sid} is: {name}", sid, result.Name);
        return result;
    }

    public async Task<ApiMovieSubject> FetchMovie(ItemLookupInfo info, CancellationToken token = default)
    {
        int subjectId = Helper.ParseDoubanId(info);
        if (subjectId == 0)
        {
            var searchResults = await GetMovieSearchResults(info, false, token);
            if (searchResults.Count > 0)
            {
                subjectId = Helper.ParseDoubanId(searchResults[0]);
            }
        }

        if (subjectId == 0)
        {
            _log.LogDebug("No results found: {name}", info.Name);
            return new ApiMovieSubject();
        }

        return await FetchMovie(subjectId.ToString(), token);
    }

    public async Task<List<PersonInfo>> FetchMovieCelebrities(string sid, CancellationToken token = default)
    {
        _log.LogDebug("Fetching celebrities for movie: {sid}", sid);
        string url = $"https://movie.douban.com/subject/{sid}/celebrities";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return []; }

        var results = Helper.ParseMovieCelebrities(responseText, sid, Configuration.FetchCelebrityImages, Configuration.CdnServer);
        _log.LogDebug("{count} person(s) found for movie {sid}", results.Count, sid);
        return results;
    }

    public async Task<List<RemoteImageInfo>> FetchMovieImages(string sid, string type = "R", ImageType imageType = ImageType.Primary, CancellationToken token = default)
    {
        _log.LogDebug("Fetching images for movie: {sid}, type: {type}", sid, type);
        string url = $"https://movie.douban.com/subject/{sid}/photos?type={type}";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return []; }
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var results = Helper.ParseImages(responseText, imageType, Configuration.DistinguishUsingAspectRatio, Configuration.CdnServer);
        _log.LogDebug("{count} image(s) found for movie {sid}", results.Count, sid);
        return results;
    }

    public async Task<ApiEpisodeSubject> FetchMovieEpisode(string sid, int index, CancellationToken token = default)
    {
        _log.LogDebug("Fetching episode: {sid}/{index}", sid, index);
        string url = $"https://movie.douban.com/subject/{sid}/episode/{index}/";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return new ApiEpisodeSubject(); }

        var result = Helper.ParseMovieEpisode(responseText);
        _log.LogDebug("Episode {sid:l}/{index} is: {title}", sid, index, result.Name);
        return result;
    }

    public async Task<List<ApiPersonSubject>> SearchPerson(string keyword, CancellationToken token = default)
    {
        keyword = keyword.Trim();
        if (string.IsNullOrEmpty(keyword)) { return []; }

        _log.LogDebug("Searching person: {keyword}", keyword);
        string url = $"https://www.douban.com/search?cat=1065&q={Uri.EscapeDataString(keyword)}";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return []; }

        var results = Helper.ParseSearchPersonResults(responseText);
        if (results.Count == 0)
        {
            _log.LogDebug("No results found for: {keyword}", keyword);
        }
        else
        {
            _log.LogDebug("{count} result(s) found for: {keyword}, first: {first}", results.Count, keyword, results[0].Name);
        }
        return results;
    }

    public async Task<string> ConvertCelebrityIdToPersonageId(string cid, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        var head = await _httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, $"https://movie.douban.com/celebrity/{cid}/"), token);
        var pid = REGEX_PERSONAGE_ID.Match(head.RequestMessage?.RequestUri?.ToString() ?? "")?.Groups[1].Value ?? "";
        return pid;
    }

    public async Task<ApiPersonSubject> FetchPersonByCelebrityId(string cid, CancellationToken token = default)
        => await FetchPersonByPersonageId(await ConvertCelebrityIdToPersonageId(cid, token), token);

    public async Task<ApiPersonSubject> FetchPersonByPersonageId(string pid, CancellationToken token = default)
    {
        _log.LogDebug("Fetching celebrity: {pid}", pid);
        string url = $"https://www.douban.com/personage/{pid}/";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return new ApiPersonSubject(); }

        var result = Helper.ParsePerson(responseText, pid, Configuration.CdnServer);
        _log.LogDebug("PersonageId {pid} is: {name}", pid, result.Name);
        return result;
    }

    public async Task<List<RemoteImageInfo>> FetchPersonImages(string pid, CancellationToken token = default)
    {
        _log.LogDebug("Fetching images for person: {pid}", pid);
        string url = $"https://www.douban.com/personage/{pid}/photos/";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return []; }
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var results = Helper.ParseImages(responseText, ImageType.Primary, false, Configuration.CdnServer);
        _log.LogDebug("{count} image(s) found for person {pid}", results.Count, pid);
        return results;
    }

    private async Task<string?> FetchUrl(string url, CancellationToken token = default)
    {
        if (Caches.Count > 0 && CacheLastClean < DateTime.Now - TimeSpan.FromDays(1))
        {
            _log.LogDebug($"Removing expired cache");
            Caches.Where(_ => _.Value.time < DateTime.Now - TimeSpan.FromDays(1)).ToList().ForEach(_ => Caches.Remove(_.Key));
            CacheLastClean = DateTime.Now;
        }
        if (Caches.TryGetValue(url, out Cache cache))
        {
            if (cache.time > DateTime.Now - TimeSpan.FromDays(1))
            {
                _log.LogDebug("Cache hit: {url}", url);
                return cache.content;
            }
            else
            {
                Caches.Remove(url);
            }
        }
        if (DateTime.Now < LastSearch + TimeSpan)
        {
            LastSearch += TimeSpan;
            var delay = LastSearch - DateTime.Now;
            _log.LogDebug("Delay: {delay} ms", delay.TotalMilliseconds);
            await Task.Delay(LastSearch - DateTime.Now, token).ConfigureAwait(false);
        }
        var message = new HttpRequestMessage(HttpMethod.Get, url);
        if (!string.IsNullOrEmpty(Configuration.DoubanCookie))
        {
            message.Headers.Add("Cookie", Configuration.DoubanCookie);
        }
        var response = await _httpClient.SendAsync(message, token).ConfigureAwait(false);
        LastSearch = DateTime.Now;
        if (!response.IsSuccessStatusCode)
        {
            _log.LogError("Response: {code} {status}{content}", (int)response.StatusCode, response.StatusCode, response.StatusCode == System.Net.HttpStatusCode.Forbidden ? ", maybe you need to provide cookie" : "");
            return null;
        }
        var responseText = await response.Content.ReadAsStringAsync(token);
        Caches[url] = new Cache() { content = responseText, time = DateTime.Now };
        return responseText;
    }
}
