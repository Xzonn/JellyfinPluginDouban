using Emby.Naming.Video;
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

public class DoubanApi
{
    private struct Cache
    {
        public string content;
        public DateTime time;
    }

    private const string SEASON_PATTERN = @"第([一二三四五六七八九十百千万\d]+)[季期]";

    private readonly HttpClient _httpClient;
    public HttpClient GetHttpClient() => _httpClient;
    private readonly ILogger<DoubanApi> _log;
    private static DateTime _lastSearch = DateTime.MinValue;
    private TimeSpan _timeSpan => TimeSpan.FromMilliseconds(_configuration.RequestTimeSpan);
    private static DateTime _cacheLastClean = DateTime.MinValue;
    private readonly Dictionary<string, Cache> _caches;
    private static PluginConfiguration _configuration
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
        _cacheLastClean = DateTime.Now;
        _caches = new Dictionary<string, Cache>();
    }

    public async Task<List<ApiMovieSubject>> SearchMovie(string keyword, CancellationToken token = default)
    {
        keyword = keyword.Trim();
        if (string.IsNullOrEmpty(keyword)) { return new List<ApiMovieSubject>(); }

        _log.LogDebug($"Searching movie: {keyword}");
        string url = $"https://www.douban.com/search?cat=1002&q={Uri.EscapeDataString(keyword)}";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return new List<ApiMovieSubject>(); }

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var resultList = htmlDoc.QuerySelector(".result-list");
        if (resultList == null)
        {
            _log.LogDebug($"No results found: {keyword}");
            return new List<ApiMovieSubject>();
        }
        var results = resultList.ChildNodes.Where(_ => _.HasClass("result")).Select(_ =>
        {
            var link = _.QuerySelector(".content .title h3 a");
            var sid = Regex.Match(link.Attributes["onclick"].Value, @"\s*sid:\s*(\d+)").Groups[1].Value;
            var name = link.InnerText.Trim();
            var posterId = Regex.Match(_.QuerySelector(".pic img").Attributes["src"].Value, @"/(p\d+)\.(?:webp|png|jpg|jpeg|gif)$").Groups[1].Value;
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
                originalName = Regex.Replace(subjectCast[0].Trim(), @"^原名:", "");
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
        _log.LogDebug($"{results.Count} result(s) found for: {keyword}, first: {results[0].Name}");
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

        var infoName = string.IsNullOrEmpty(info.Name) ? "" : info.Name;
        var searchNames = new List<string?>();
        if (searchResults.Count == 0)
        {
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
                if (info is SeasonInfo seasonInfo)
                {
                    searchNames.Add(seasonInfo.SeriesProviderIds.GetValueOrDefault(MetadataProvider.Imdb.ToString()));
                }
                if (info.Year is not null && info.Year > 0)
                {
                    searchNames.Add($"{infoName} {info.Year}");
                }
                searchNames.Add(infoName);
                searchNames.Add(info.OriginalTitle);
                searchNames.Add(Path.GetFileName(info.Path));
            }
        }
        _log.LogDebug($"Info type: {info.GetType()}, names for searching: {string.Join(", ", searchNames)}");

        foreach (var name in searchNames)
        {
            if (
                string.IsNullOrWhiteSpace(name) ||
                // Season name is auto-generated, no need to search
                ((info is SeasonInfo || info is EpisodeInfo) && Regex.IsMatch(name, @"^(?:第 \d+ 季|Season \d+|未知季|Season Unknown|Specials)$"))
                )
            {
                continue;
            }

            searchResults = await SearchMovie(name.Replace(".", " "), token);
            if (searchResults.Count != 0) { break; }

            VideoResolver.TryCleanString(infoName, new Emby.Naming.Common.NamingOptions(), out var newName);
            if (!string.IsNullOrEmpty(newName))
            {
                searchResults = await SearchMovie(newName.Replace(".", " "), token);
                if (searchResults.Count != 0) { break; }
            }

            newName = AnitomySharp.AnitomySharp.Parse(infoName).FirstOrDefault(_ => _.Category == AnitomySharp.Element.ElementCategory.ElementAnimeTitle)?.Value;
            if (!string.IsNullOrEmpty(newName))
            {
                searchResults = await SearchMovie(newName.Replace(".", " "), token);
                if (searchResults.Count != 0) { break; }
            }
        }

        if (searchResults.Count > 0 && ((info is SeriesInfo && !Regex.IsMatch(infoName, SEASON_PATTERN)) || (info is SeasonInfo seasonInfo2 && seasonInfo2.IndexNumber == 1)))
        {
            var season = Regex.Match(searchResults[0].Name!, SEASON_PATTERN);
            if (season.Success && !new string[] { "一", "1" }.Contains(season.Groups[1].Value))
            {
                searchResults = await SearchMovie(Regex.Replace(searchResults[0].Name!, SEASON_PATTERN, "第一季"), token);
            }
        }
        searchResults = searchResults.OrderBy(_ => _.Type == (info is MovieInfo ? "电影" : "电视剧") ? 0 : 1).ToList();
        var results = searchResults.Select(_ =>
        {
            var result = new RemoteSearchResult
            {
                Name = _.Name,
                SearchProviderName = _.OriginalName,
                ImageUrl = string.IsNullOrEmpty(_.PosterId) ? "" : $"{_configuration.CdnServer}/view/photo/l/public/{_.PosterId}.jpg",
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

    public static int TryParseDoubanId(ItemLookupInfo info)
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

            if (subjectId == 0 && info is SeasonInfo seasonInfo && (seasonInfo.IndexNumber == 0 || seasonInfo.IndexNumber == 1))
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
        _log.LogDebug($"Fetching movie: {sid}");
        string url = $"https://movie.douban.com/subject/{sid}/";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return new ApiMovieSubject(); }
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var name = HttpUtility.HtmlDecode(Regex.Replace(htmlDoc.QuerySelector("title").InnerText.Trim(), @" \(豆瓣\)$", ""));
        _log.LogDebug($"Sid {sid} is: {name}");
        var content = htmlDoc.QuerySelector("#content");
        var posterId = Regex.Match(content.QuerySelector("#mainpic img").Attributes["src"].Value, @"/(p\d+)\.(?:webp|png|jpg|jpeg|gif)$").Groups[1].Value;
        var originalName = content.QuerySelector("h1 span").InnerText.Replace(name, "").Trim();
        var year = Convert.ToInt32(content.QuerySelector("h1 .year").InnerText.Trim().TrimStart('(').TrimEnd(')'));
        var rating = content.QuerySelector("#interest_sectl .rating_num").InnerText.Trim();
        rating = string.IsNullOrEmpty(rating) ? "0.0" : rating;
        var info = content.QuerySelector("#info").InnerText.Trim().Split("\n").Select(_ => _.Trim().Split(": ")).Where(_ => _.Length > 1).ToDictionary(_ => _[0], _ => string.Join(": ", _[1..]).Trim());
        var type = "电影";
        if (info.ContainsKey("集数") || info.ContainsKey("单集片长")) { type = "电视剧"; }
        var intro = string.Join("\n", (content.QuerySelector("#link-report-intra span.all") ?? content.QuerySelector("#link-report-intra span"))?.InnerText.Trim().Split("\n").Select(_ => _.Trim()) ?? Array.Empty<string>());
        var screenTime = info!.GetValueOrDefault("上映日期", info!.GetValueOrDefault("首播", ""))!.Split("/").Select(_ => Regex.Replace(_.Trim(), @"\(.+?\)?$", "")).Where(_ => Regex.IsMatch(_, @"\d{4}-\d\d-\d\d")).FirstOrDefault();
        var seasonIndex = 0;
        if (info.ContainsKey("季数"))
        {
            seasonIndex = Convert.ToInt32(info["季数"]);
        }
        else if (content.QuerySelector("#season option[selected]") is HtmlNode selected)
        {
            seasonIndex = Convert.ToInt32(selected.InnerText.Trim());
        }
        else if (type == "电视剧")
        {
            seasonIndex = 1;
            if (Regex.IsMatch(name, SEASON_PATTERN))
            {
                seasonIndex = ConvertChineseNumberToNumber(Regex.Match(name, SEASON_PATTERN).Groups[1].Value);
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
            _log.LogDebug($"No results found: {info.Name}");
            return new ApiMovieSubject();
        }

        return await FetchMovie(subjectId.ToString(), token);
    }

    public async Task<List<PersonInfo>> FetchMovieCelebrities(string sid, CancellationToken token = default)
    {
        _log.LogDebug($"Fetching celebrities for movie: {sid}");
        string url = $"https://movie.douban.com/subject/{sid}/celebrities";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return new List<PersonInfo>(); }
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var celebrities = htmlDoc.QuerySelectorAll(".celebrities-list .celebrity");
        var results = celebrities.Select(_ =>
        {
            var link = _.QuerySelector("a.name");
            var name = link.InnerText.Trim().Split(" ")[0];
            var cid = Regex.Match(link.Attributes["href"].Value, @"/celebrity/(\d+)/").Groups[1].Value;
            var posterUrl = Regex.Match(_.QuerySelector(".avatar").Attributes["style"].Value, @"url\((.+?\.(?:webp|png|jpg|jpeg|gif))\)").Groups[1].Value;
            if (!_configuration.FetchCelebrityImages || posterUrl.Contains("celebrity-default"))
            {
                posterUrl = null;
            }
            else
            {
                posterUrl = Regex.Replace(posterUrl, @"https?://img\d+\.doubanio.com", _configuration.CdnServer);
            }
            var roleText = new string[] { "" };
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
        _log.LogDebug($"Fetching images for movie: {sid}, type: {type}");
        string url = $"https://movie.douban.com/subject/{sid}/photos?type={type}";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return new List<RemoteImageInfo>(); }
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var results = htmlDoc.QuerySelectorAll(".article ul li").Select(_ =>
        {
            var posterId = Regex.Match(_.QuerySelector("img").Attributes["src"].Value, @"/(p\d+)\.(?:webp|png|jpg|jpeg|gif)$").Groups[1].Value;
            var size = _.QuerySelector(".prop").InnerText.Trim().Split("x");
            var width = Convert.ToInt32(size[0]);
            var height = Convert.ToInt32(size[1]);
            return new RemoteImageInfo()
            {
                ProviderName = Constants.PluginName,
                Language = Constants.Language,
                Type = _configuration.DistinguishUsingAspectRatio ? (width > height ? ImageType.Backdrop : ImageType.Primary) : imageType,
                ThumbnailUrl = $"{_configuration.CdnServer}/view/photo/s/public/{posterId}.jpg",
                Url = $"{_configuration.CdnServer}/view/photo/l/public/{posterId}.jpg",
                Width = width,
                Height = height,
            };
        }).ToList();
        return results;
    }

    public async Task<ApiEpisodeSubject> FetchMovieEpisode(string sid, int index, CancellationToken token = default)
    {
        _log.LogDebug($"Fetching episode: {sid}/{index}");
        string url = $"https://movie.douban.com/subject/{sid}/episode/{index}/";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return new ApiEpisodeSubject(); }
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var title = htmlDoc.QuerySelector("title").InnerText.Trim();
        _log.LogDebug($"Episode {sid}/{index} is: {title}");
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
        if (string.IsNullOrEmpty(keyword)) { return new List<ApiPersonSubject>(); }

        _log.LogDebug($"Searching person: {keyword}");
        string url = $"https://movie.douban.com/j/subject_suggest?q={Uri.EscapeDataString(keyword)}";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return new List<ApiPersonSubject>(); }

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
        if (results.Count == 0) { _log.LogDebug($"No results found: {keyword}"); }
        else { _log.LogDebug($"{results.Count} result(s) found for: {keyword}, first: {results[0].Name}"); }
        return results;
    }

    public async Task<ApiPersonSubject> FetchPerson(string cid, CancellationToken token = default)
    {
        _log.LogDebug($"Fetching celebrity: {cid}");
        string url = $"https://movie.douban.com/celebrity/{cid}/";
        string? responseText = await FetchUrl(url, token);
        if (string.IsNullOrEmpty(responseText)) { return new ApiPersonSubject(); }
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var content = htmlDoc.QuerySelector("#content");
        var image = content.QuerySelector("#headline .pic img");
        var name = image.Attributes["alt"].Value;
        _log.LogDebug($"Cid {cid} is: {name}");
        var posterUrl = image.Attributes["src"].Value;
        if (posterUrl.Contains("celebrity-default"))
        {
            posterUrl = null;
        }
        else
        {
            posterUrl = Regex.Replace(posterUrl, @"https?://img\d+\.doubanio.com", _configuration.CdnServer);
        }
        var originalName = content.QuerySelector("h1").InnerText.Replace(name, "").Trim();
        var info = content.QuerySelectorAll(".info ul li").Select(_ => _.InnerText.Trim().Split(": ")).Where(_ => _.Length > 1).ToDictionary(_ => _[0], _ => string.Join(": ", _[1..]).Trim());
        var intro = string.Join("\n", (content.QuerySelector("#intro .bd .all") ?? content.QuerySelector("#intro .bd"))?.InnerText.Trim().Split("\n").Select(_ => _.Trim()) ?? Array.Empty<string>());
        var birthdate = info!.GetValueOrDefault("出生日期", null);
        var deathdate = "";
        if (info.ContainsKey("生卒日期"))
        {
            birthdate = info["生卒日期"].Split("至")[0].Trim();
            deathdate = info["生卒日期"].Split("至")[1].Trim();
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
            Birthplace = string.IsNullOrEmpty(bitrhplace) ? null : new string[] { bitrhplace },
            Website = info!.GetValueOrDefault("官方网站", null),
            ImdbId = info!.GetValueOrDefault("imdb编号", null),
        };
        return result;
    }

    private async Task<string?> FetchUrl(string url, CancellationToken token = default)
    {
        if (_caches.Count > 0 && _cacheLastClean < DateTime.Now - TimeSpan.FromDays(1))
        {
            _log.LogDebug($"Removing expired cache");
            _caches.Where(_ => _.Value.time < DateTime.Now - TimeSpan.FromDays(1)).ToList().ForEach(_ => _caches.Remove(_.Key));
            _cacheLastClean = DateTime.Now;
        }
        if (_caches.ContainsKey(url))
        {
            if (_caches[url].time > DateTime.Now - TimeSpan.FromDays(1))
            {
                _log.LogDebug($"Cache hit: {url}");
                return _caches[url].content;
            }
            else
            {
                _caches.Remove(url);
            }
        }
        if (DateTime.Now < _lastSearch + _timeSpan)
        {
            _lastSearch += _timeSpan;
            var delay = _lastSearch - DateTime.Now;
            _log.LogDebug($"Delay: {delay.TotalMilliseconds} ms");
            await Task.Delay(_lastSearch - DateTime.Now, token).ConfigureAwait(false);
        }
        var message = new HttpRequestMessage(HttpMethod.Get, url);
        message.Headers.Add("Cookie", _configuration.DoubanCookie);
        var response = await _httpClient.SendAsync(message, token).ConfigureAwait(false);
        _lastSearch = DateTime.Now;
        if (!response.IsSuccessStatusCode)
        {
            _log.LogError($"Response: {(int)response.StatusCode} {response.StatusCode}" + (response.StatusCode == System.Net.HttpStatusCode.Forbidden ? ", maybe you need to provide cookie" : ""));
            return null;
        }
        var responseText = await response.Content.ReadAsStringAsync(token);
        _caches[url] = new Cache() { content = responseText, time = DateTime.Now };
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
