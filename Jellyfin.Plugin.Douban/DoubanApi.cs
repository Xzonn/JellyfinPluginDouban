﻿using Emby.Naming.Video;
using HtmlAgilityPack;
using Jellyfin.Plugin.Douban.Configuration;
using Jellyfin.Plugin.Douban.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;

namespace Jellyfin.Plugin.Douban;

public partial class DoubanApi
{
    private struct Cache
    {
        public string content;
        public DateTime time;
    }

    private static readonly string[] ONES = ["一", "1"];

    private static Regex REGEX_SID => new(@"\s*sid:\s*(\d+)");
    private static Regex REGEX_IMAGE => new(@"/(p\d+)\.(?:webp|png|jpg|jpeg|gif)$");
    private static Regex REGEX_IMAGE_URL => new(@"url\((.+?\.(?:webp|png|jpg|jpeg|gif))\)");
    private static Regex REGEX_ORIGINAL_NAME => new(@"^原名:");
    private static Regex REGEX_AUTOMATIC_SEASON_NAME => new(@"^ *(?:第 *\d+ *季|Season \d+|未知季|Season Unknown|Specials) *$");
    private static Regex REGEX_SEASON => new(@" *第([一二三四五六七八九十百千万\d]+)[季期]");
    private static Regex REGEX_DOUBAN_POSTFIX => new(@" \(豆瓣\)$");
    private static Regex REGEX_BRACKET => new(@"\(.+?\)?$");
    private static Regex REGEX_DATE => new(@"\d{4}-\d\d-\d\d");
    private static Regex REGEX_CELEBRITY => new(@"/celebrity/(\d+)/");
    private static Regex REGEX_DOUBANIO_HOST => new(@"https?://img\d+\.doubanio.com");

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
        keyword = keyword.Trim();
        if (string.IsNullOrEmpty(keyword)) { return []; }

        _log.LogDebug("Searching movie: {keyword} (isMovie: {isMovie}, isFirstSeason: {isFirstSeason})", keyword, isMovie, isFirstSeason);
        string url = $"https://www.douban.com/search?cat=1002&q={Uri.EscapeDataString(keyword)}";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return []; }

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var resultList = htmlDoc.QuerySelector(".result-list");
        if (resultList == null)
        {
            _log.LogDebug("No results found: {keyword}", keyword);
            return [];
        }
        var results = resultList.ChildNodes.Where(_ => _.HasClass("result")).Select(_ =>
        {
            var link = _.QuerySelector(".content .title h3 a");
            var sid = REGEX_SID.Match(link.Attributes["onclick"].Value).Groups[1].Value;
            var name = link.InnerText.Trim();
            var posterId = REGEX_IMAGE.Match(_.QuerySelector(".pic img").Attributes["src"].Value).Groups[1].Value;
            var type = _.QuerySelector(".content .title h3 span").InnerText.Trim().TrimStart('[').TrimEnd(']');
            var rating = "0.0";
            if (_.QuerySelector(".rating-info .rating_nums") is HtmlNode __) { rating = __.InnerText.Trim(); }
            var subjectCast = _.QuerySelector(".rating-info .subject-cast")?.InnerText?.Split("/");
            // 35196946 can not be rated
            subjectCast ??= _.QuerySelector(".rating-info")?.InnerText?.Split("/");
            string? originalName = null;
            int year = 0;
            if (subjectCast is not null)
            {
                originalName = REGEX_ORIGINAL_NAME.Replace(subjectCast[0].Trim(), "");
                int.TryParse(subjectCast[^1].Trim(), out year);
            }
            return new ApiMovieSubject()
            {
                Sid = sid,
                Name = name,
                PosterId = posterId,
                Type = type,
                Rating = Convert.ToDecimal(rating),
                OriginalName = originalName,
                Year = year,
            };
        }).ToList();
        results = [.. results.OrderBy(_ => _.Type == (isMovie ? "电影" : "电视剧") ? 0 : 1)];
        if (!isMovie && isFirstSeason)
        {
            var first = results[0].Name!;
            results = [.. results.OrderBy(_ => _.Name != first && first.StartsWith(_.Name!) ? 0 : 1)];
        }
        _log.LogDebug("{count} result(s) found for: {keyword}, first: {first}", results.Count, keyword, results[0].Name);
        return results;
    }

    public async Task<List<RemoteSearchResult>> GetMovieSearchResults(ItemLookupInfo info, bool tryParse = true, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var searchResults = new List<ApiMovieSubject>();

        if (tryParse)
        {
            int id = TryParseDoubanId(info);
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
        // For series, if the name does not include a season name, search for the first season
        // For seasons, if the index < 2, search for the first season
        var isFirstSeason = (info is SeriesInfo && !REGEX_SEASON.IsMatch(infoName)) || (info is SeasonInfo && info.IndexNumber < 2);
        if (searchResults.Count == 0)
        {
            var searchNames = new List<string?>();

            if (info is EpisodeInfo episodeInfo)
            {
                searchNames.Add(episodeInfo.SeriesProviderIds.GetValueOrDefault(MetadataProvider.Imdb.ToString()));
                // For episode, DO NOT SEARCH NAME DIRECTLY
                searchNames.Add(AnitomySharp.AnitomySharp.Parse(Path.GetFileName(info.Path)).FirstOrDefault(_ => _.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value);
                searchNames.Add(Path.GetFileName(Path.GetDirectoryName(info.Path)));
            }
            else
            {
                searchNames.Add(info.GetProviderId(MetadataProvider.Imdb));
                if (info is SeasonInfo seasonInfo && info.IndexNumber < 2)
                {
                    searchNames.Add(seasonInfo.SeriesProviderIds.GetValueOrDefault(MetadataProvider.Imdb.ToString()));
                }
                if (!string.IsNullOrEmpty(infoName))
                {
                    if (info.Year is not null && info.Year > 0)
                    {
                        searchNames.Add($"{infoName} {info.Year}");
                    }
                    searchNames.Add(infoName);
                }
                searchNames.Add(info.OriginalTitle);
                searchNames.Add(Path.GetFileName(info.Path));
            }

            searchNames = searchNames.Where(name =>
            {
                if (string.IsNullOrWhiteSpace(name)) return false;
                // Season name is auto-generated, no need to search
                if ((info is SeasonInfo || info is EpisodeInfo) && REGEX_AUTOMATIC_SEASON_NAME.IsMatch(name)) return false;
                return true;
            }).ToList();

            if (info is SeasonInfo && info.IndexNumber > 1)
            {
                int parentId = TryParseDoubanId(info, true);
                if (parentId != 0)
                {
                    var subject = await FetchMovie(parentId.ToString(), token);
                    if (subject != null)
                    {
                        searchNames.Add($"{REGEX_SEASON.Replace(subject.Name!, "")} 第{info.IndexNumber}季");
                    }
                }
                if (!string.IsNullOrWhiteSpace(infoName)) { searchNames.Add($"{REGEX_SEASON.Replace(infoName, "")} 第{info.IndexNumber}季"); }
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
                searchResults = await SearchMovie(name!.Replace(".", " ").Trim(), isMovie, isFirstSeason, token);
                if (searchResults.Count != 0) { break; }

                VideoResolver.TryCleanString(infoName, new Emby.Naming.Common.NamingOptions(), out var newName);
                if (!string.IsNullOrEmpty(newName))
                {
                    searchResults = await SearchMovie(newName.Replace(".", " "), isMovie, isFirstSeason, token);
                    if (searchResults.Count != 0) { break; }
                }

                newName = AnitomySharp.AnitomySharp.Parse(infoName).FirstOrDefault(_ => _.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
                if (!string.IsNullOrEmpty(newName))
                {
                    searchResults = await SearchMovie(newName.Replace(".", " "), isMovie, isFirstSeason, token);
                    if (searchResults.Count != 0) { break; }
                }
            }
        }

        if (searchResults.Count > 0 && isFirstSeason)
        {
            // If the name of search result contains "第x季" but x != 1, search for the first season again
            var season = REGEX_SEASON.Match(searchResults[0].Name!);
            if (season.Success && !ONES.Contains(season.Groups[1].Value))
            {
                searchResults = await SearchMovie(REGEX_SEASON.Replace(searchResults[0].Name!, "第1季"), isMovie, isFirstSeason, token);
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

    public static int TryParseDoubanId(ItemLookupInfo info, bool ignoreSeasonIndex = false)
    {
        int subjectId;
        if (info is EpisodeInfo episodeInfo)
        {
            if (!int.TryParse(episodeInfo.SeriesProviderIds.GetValueOrDefault(Constants.ProviderId), out subjectId))
            {
                int.TryParse(episodeInfo.SeriesProviderIds.GetValueOrDefault(Constants.OddbId), out subjectId);
            }

            if (subjectId == 0)
            {
                var episodeId = episodeInfo.SeriesProviderIds.GetValueOrDefault(Constants.ProviderId);
                if (!string.IsNullOrEmpty(episodeId) && episodeId.Contains("/episode/"))
                {
                    int.TryParse(episodeId.Split("/episode/")[0], out subjectId);
                }
            }
        }
        else
        {
            if (!int.TryParse(info.ProviderIds.GetValueOrDefault(Constants.ProviderId), out subjectId))
            {
                int.TryParse(info.ProviderIds.GetValueOrDefault(Constants.OddbId), out subjectId);
            }

            if (subjectId == 0 && info is SeasonInfo seasonInfo && (seasonInfo.IndexNumber < 2 || ignoreSeasonIndex))
            {
                if (!int.TryParse(seasonInfo.SeriesProviderIds.GetValueOrDefault(Constants.ProviderId), out subjectId))
                {
                    int.TryParse(seasonInfo.SeriesProviderIds.GetValueOrDefault(Constants.OddbId), out subjectId);
                }
            }
        }
        return subjectId;
    }

    public async Task<ApiMovieSubject> FetchMovie(string sid, CancellationToken token = default)
    {
        _log.LogDebug("Fetching movie: {sid}", sid);
        string url = $"https://movie.douban.com/subject/{sid}/";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return new ApiMovieSubject(); }
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var name = HttpUtility.HtmlDecode(REGEX_DOUBAN_POSTFIX.Replace(htmlDoc.QuerySelector("title").InnerText.Trim(), ""));
        _log.LogDebug("Sid {sid} is: {name}", sid, name);
        var content = htmlDoc.QuerySelector("#content");
        var posterId = REGEX_IMAGE.Match(content.QuerySelector("#mainpic img").Attributes["src"].Value).Groups[1].Value;
        var originalName = content.QuerySelector("h1 span").InnerText.Replace(name, "").Trim();
        var year = Convert.ToInt32(content.QuerySelector("h1 .year").InnerText.Trim().TrimStart('(').TrimEnd(')'));
        var rating = content.QuerySelector("#interest_sectl .rating_num").InnerText.Trim();
        rating = string.IsNullOrEmpty(rating) ? "0.0" : rating;
        var info = content.QuerySelector("#info").InnerText.Trim().Split("\n").Select(_ => _.Trim().Split(": ")).Where(_ => _.Length > 1).ToDictionary(_ => _[0], _ => string.Join(": ", _[1..]).Trim());
        var type = "电影";
        if (info.ContainsKey("集数") || info.ContainsKey("单集片长")) { type = "电视剧"; }
        var intro = string.Join("\n", (content.QuerySelector("#link-report-intra span.all") ?? content.QuerySelector("#link-report-intra span"))?.InnerText.Trim().Split("\n").Select(_ => _.Trim()) ?? []);
        var screenTime = info!.GetValueOrDefault("上映日期", info!.GetValueOrDefault("首播", ""))!.Split("/").Select(_ => REGEX_BRACKET.Replace(_.Trim(), "")).Where(_ => REGEX_DATE.IsMatch(_)).FirstOrDefault();
        var seasonIndex = 0;
        if (info.TryGetValue("季数", out string? seasonNumber))
        {
            seasonIndex = Convert.ToInt32(seasonNumber);
        }
        else if (content.QuerySelector("#season option[selected]") is HtmlNode selected)
        {
            seasonIndex = Convert.ToInt32(selected.InnerText.Trim());
        }
        else if (type == "电视剧")
        {
            seasonIndex = 1;
            if (REGEX_SEASON.IsMatch(name))
            {
                seasonIndex = ConvertChineseNumberToNumber(REGEX_SEASON.Match(name).Groups[1].Value);
            }
        }
        int.TryParse(info!.GetValueOrDefault("集数", "0"), out var episodeCount);

        var result = new ApiMovieSubject()
        {
            Sid = sid,
            Name = name,
            PosterId = posterId,
            Type = type,
            Rating = Convert.ToDecimal(rating),
            OriginalName = string.IsNullOrEmpty(originalName) ? name : originalName,
            Year = year,
            Intro = intro,
            Genres = info!.GetValueOrDefault("类型")?.Split("/").Select(_ => _.Trim()).ToArray(),
            Website = info!.GetValueOrDefault("官方网站", null),
            Country = info!.GetValueOrDefault("制片国家/地区")?.Split("/").Select(_ => _.Trim()).ToArray(),
            ScreenTime = string.IsNullOrEmpty(screenTime) ? null : Convert.ToDateTime(screenTime),
            ImdbId = info!.GetValueOrDefault("IMDb", null),
            SeasonIndex = seasonIndex,
            EpisodeCount = episodeCount,
        };
        result.Tags = result.Genres?.ToArray();
        return result;
    }

    public async Task<ApiMovieSubject> FetchMovie(ItemLookupInfo info, CancellationToken token = default)
    {
        int subjectId = TryParseDoubanId(info);
        if (subjectId == 0)
        {
            var searchResults = (await GetMovieSearchResults(info, false, token)).ToList();
            if (searchResults.Count > 0)
            {
                int.TryParse(searchResults[0].GetProviderId(Constants.ProviderId), out subjectId);
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
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var celebrities = htmlDoc.QuerySelectorAll(".celebrities-list .celebrity");
        var results = celebrities.Select(_ =>
        {
            var link = _.QuerySelector("a.name");
            var name = link.InnerText.Trim().Split(" ")[0];
            var cid = REGEX_CELEBRITY.Match(link.Attributes["href"].Value).Groups[1].Value;
            var posterUrl = REGEX_IMAGE_URL.Match(_.QuerySelector(".avatar").Attributes["style"].Value).Groups[1].Value;
            if (!Configuration.FetchCelebrityImages || posterUrl.Contains("celebrity-default"))
            {
                posterUrl = null;
            }
            else
            {
                posterUrl = REGEX_DOUBANIO_HOST.Replace(posterUrl, Configuration.CdnServer);
            }
            string[] roleText = [""];
            if (_.QuerySelector(".role") is HtmlNode __) { roleText = __.InnerText.Trim().Split(" "); }
            var type = roleText[0];
            var role = "";
            if (roleText.Contains("(饰"))
            {
                role = string.Join(" ", roleText[(Array.IndexOf(roleText, "(饰") + 1)..]).TrimEnd(')');
            }
            else if (roleText.Contains("(配"))
            {
                role = string.Join(" ", roleText[(Array.IndexOf(roleText, "(配") + 1)..]).TrimEnd(')');
            }
            var result = new PersonInfo()
            {
                Name = name,
                ImageUrl = posterUrl,
                Type = type switch
                {
                    "导演" => PersonType.Director,
                    "演员" => PersonType.Actor,
                    "配音" => PersonType.Actor,
                    "编剧" => PersonType.Writer,
                    "制片人" => PersonType.Producer,
                    "制作人" => PersonType.Producer,
                    "作曲" => PersonType.Composer,
                    "音乐" => PersonType.Composer,
                    _ => "",
                },
                Role = role,
            };
            result.SetProviderId(Constants.ProviderId, cid);
            return result;
        }).Where(_ => !string.IsNullOrEmpty(_.Type)).ToList();
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

        var results = htmlDoc.QuerySelectorAll(".article ul li").Select(_ =>
        {
            var posterId = REGEX_IMAGE.Match(_.QuerySelector("img").Attributes["src"].Value).Groups[1].Value;
            var size = _.QuerySelector(".prop").InnerText.Trim().Split("x");
            var width = Convert.ToInt32(size[0]);
            var height = Convert.ToInt32(size[1]);
            return new RemoteImageInfo()
            {
                ProviderName = Constants.PluginName,
                Language = Constants.Language,
                Type = Configuration.DistinguishUsingAspectRatio ? (width > height ? ImageType.Backdrop : ImageType.Primary) : imageType,
                ThumbnailUrl = $"{Configuration.CdnServer}/view/photo/s/public/{posterId}.jpg",
                Url = $"{Configuration.CdnServer}/view/photo/l/public/{posterId}.jpg",
                Width = width,
                Height = height,
            };
        }).ToList();
        return results;
    }

    public async Task<ApiEpisodeSubject> FetchMovieEpisode(string sid, int index, CancellationToken token = default)
    {
        _log.LogDebug("Fetching episode: {sid}/{index}", sid, index);
        string url = $"https://movie.douban.com/subject/{sid}/episode/{index}/";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return new ApiEpisodeSubject(); }
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var title = htmlDoc.QuerySelector("title").InnerText.Trim();
        _log.LogDebug("Episode {sid}/{index} is: {title}", sid, index, title);
        var info = htmlDoc.QuerySelectorAll("#content .ep-info li").Select(_ => _.InnerText.Trim().Split(":\n")).Where(_ => _.Length > 1).ToDictionary(_ => _[0], _ => string.Join(":\n", _[1..]).Trim());
        var name = info!.GetValueOrDefault("本集中文名", "暂无，欢迎添加");
        var originalName = info!.GetValueOrDefault("本集原名", "暂无，欢迎添加");
        var screenTimeStr = info!.GetValueOrDefault("播放时间", "暂无，欢迎添加");
        DateTime screenTime = DateTime.MinValue;
        try { DateTime.TryParse(screenTimeStr[..10], out screenTime); } catch { }
        var intro = htmlDoc.QuerySelector("meta[name=\"description\"]")?.Attributes["content"]?.Value.Trim();

        var result = new ApiEpisodeSubject()
        {
            Name = name == "暂无，欢迎添加" ? null : name,
            OriginalName = originalName == "暂无，欢迎添加" ? null : originalName,
            ScreenTime = screenTime == DateTime.MinValue ? null : screenTime,
            Intro = info!.GetValueOrDefault("剧情简介", "暂无，欢迎添加") == "暂无，欢迎添加" ? null : intro,
        };
        return result;
    }

    public async Task<List<ApiPersonSubject>> SearchPerson(string keyword, CancellationToken token = default)
    {
        keyword = keyword.Trim();
        if (string.IsNullOrEmpty(keyword)) { return []; }

        _log.LogDebug("Searching person: {keyword}", keyword);
        string url = $"https://movie.douban.com/j/subject_suggest?q={Uri.EscapeDataString(keyword)}";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return []; }

        var results = JsonSerializer.Deserialize<List<ResultItem>>(responseText)!.Where(_ => _.type == "celebrity").Select(_ =>
        {
            var result = new ApiPersonSubject()
            {
                Cid = _.id,
                Name = _.title,
                PosterUrl = _.url,
                OriginalName = _.sub_title,
            };
            return result;
        }).ToList();
        if (results.Count == 0) { _log.LogDebug("No results found: {keyword}", keyword); }
        else { _log.LogDebug("{count} result(s) found for: {keyword}, first: {first}", results.Count, keyword, results[0].Name); }
        return results;
    }

    public async Task<ApiPersonSubject> FetchPerson(string cid, CancellationToken token = default)
    {
        _log.LogDebug("Fetching celebrity: {cid}", cid);
        string url = $"https://movie.douban.com/celebrity/{cid}/";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return new ApiPersonSubject(); }
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var content = htmlDoc.QuerySelector("#content");
        var image = content.QuerySelector("#headline .pic img");
        var name = image.Attributes["alt"].Value;
        _log.LogDebug("Cid {cid} is: {name}", cid, name);
        var posterUrl = image.Attributes["src"].Value;
        if (posterUrl.Contains("celebrity-default"))
        {
            posterUrl = null;
        }
        else
        {
            posterUrl = REGEX_DOUBANIO_HOST.Replace(posterUrl, Configuration.CdnServer);
        }
        var originalName = content.QuerySelector("h1").InnerText.Replace(name, "").Trim();
        var info = content.QuerySelectorAll(".info ul li").Select(_ => _.InnerText.Trim().Split(": ")).Where(_ => _.Length > 1).ToDictionary(_ => _[0], _ => string.Join(": ", _[1..]).Trim());
        var intro = string.Join("\n", (content.QuerySelector("#intro .bd .all") ?? content.QuerySelector("#intro .bd"))?.InnerText.Trim().Split("\n").Select(_ => _.Trim()) ?? []);
        var birthdate = info!.GetValueOrDefault("出生日期", null);
        var deathdate = "";
        if (info.TryGetValue("生卒日期", out string? birthAndDeath))
        {
            birthdate = birthAndDeath.Split("至")[0].Trim();
            deathdate = birthAndDeath.Split("至")[1].Trim();
        }
        var bitrhplace = info!.GetValueOrDefault("出生地", null);
        var result = new ApiPersonSubject()
        {
            Cid = cid,
            Name = name,
            PosterUrl = posterUrl,
            OriginalName = string.IsNullOrEmpty(originalName) ? name : originalName,
            Intro = intro,
            Gender = info!.GetValueOrDefault("性别", null),
            Birthdate = string.IsNullOrEmpty(birthdate) ? null : Convert.ToDateTime(birthdate),
            Deathdate = string.IsNullOrEmpty(deathdate) ? null : Convert.ToDateTime(deathdate),
            Birthplace = string.IsNullOrEmpty(bitrhplace) ? null : [bitrhplace],
            Website = info!.GetValueOrDefault("官方网站", null),
            ImdbId = info!.GetValueOrDefault("imdb编号", null),
        };
        return result;
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

    private static int ConvertChineseNumberToNumber(string chinese)
    {
        if (int.TryParse(chinese, out int result)) { return result; }
        int unit = 1;
        for (int i = chinese.Length - 1; i > -1; --i)
        {
            char c = chinese[i];
            switch (c)
            {
                case '十': unit = 10; break;
                case '百': unit = 100; break;
                case '千': unit = 1000; break;
                case '万': unit = 10000; break;
                default: result += unit * ("一二三四五六七八九".IndexOf(c) + 1); continue;
            }
            if (i == 0)
            {
                result += unit;
                break;
            }
        }
        return result;
    }
}
