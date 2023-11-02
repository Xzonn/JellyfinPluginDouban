using Emby.Naming.Video;
using HtmlAgilityPack;
using Jellyfin.Plugin.Douban.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Jellyfin.Plugin.Douban;

public class DoubanApi
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly HttpClient _httpClient;
    public HttpClient GetHttpClient() => _httpClient;
    private readonly ILogger<DoubanApi> _log;
    private DateTime _lastSearch;
    private TimeSpan _timeSpan;

    public DoubanApi(IHttpClientFactory httpClientFactory, ILogger<DoubanApi> log)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://www.douban.com/");
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _log = log;
        _lastSearch = DateTime.MinValue;
        _timeSpan = TimeSpan.FromMilliseconds(1500);
    }

    private static Plugin Plugin => Plugin.Instance!;

    public async Task<List<ApiMovieSubject>> SearchMovie(string keyword, CancellationToken token = default)
    {
        _log.LogInformation($"Searching movie: {keyword}");
        string url = $"https://www.douban.com/search?cat=1002&q={Uri.EscapeDataString(keyword)}";

        if (DateTime.Now - _lastSearch < _timeSpan)
        {
            await Task.Delay(_timeSpan - (DateTime.Now - _lastSearch), token).ConfigureAwait(false);
        }
        HttpResponseMessage response = await _httpClient.GetAsync(url, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogError($"Response: {response.StatusCode}");
            return new List<ApiMovieSubject>();
        }
        string responseText = await response.Content.ReadAsStringAsync(token);
        _lastSearch = DateTime.Now;
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var resultList = htmlDoc.QuerySelector(".result-list");
        if (resultList == null)
        {
            _log.LogInformation($"No results found: {keyword}");
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
            var subjectCast = _.QuerySelector(".rating-info .subject-cast").InnerText.Split("/");
            var originalName = Regex.Replace(subjectCast[0].Trim(), @"^原名:", "");
            var year = subjectCast[^1].Trim();
            return new ApiMovieSubject()
            {
                Sid = sid,
                Name = name,
                PosterId = posterId,
                Type = type,
                Rating = Convert.ToDecimal(rating),
                OriginalName = originalName,
                Year = Convert.ToInt32(year),
            };
        }).ToList();
        _log.LogInformation($"{results.Count} result(s) found.");
        return results;
    }

    public async Task<List<RemoteSearchResult>> GetMovieSearchResults(ItemLookupInfo info, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();
        var searchResults = new List<ApiMovieSubject>();

        if (int.TryParse(info.ProviderIds.GetValueOrDefault(Constants.ProviderId), out var id) || int.TryParse(info.ProviderIds.GetValueOrDefault(Constants.OddbId), out id))
        {
            var subject = await FetchMovie(id.ToString(), token);
            if (subject != null)
            {
                searchResults.Add(subject);
            }
        }
        if (searchResults.Count == 0 && info is SeasonInfo seasonInfo && (int.TryParse(seasonInfo.SeriesProviderIds.GetValueOrDefault(Constants.ProviderId), out id) || int.TryParse(seasonInfo.SeriesProviderIds.GetValueOrDefault(Constants.OddbId), out id)))
        {
            var subject = await FetchMovie(id.ToString(), token);
            if (subject != null)
            {
                searchResults.Add(subject);
            }
        }
        if (searchResults.Count == 0 && info.GetProviderId(MetadataProvider.Imdb) is string imdbId)
        {
            searchResults = await SearchMovie(imdbId, token);
        }
        if (searchResults.Count == 0 && !string.IsNullOrEmpty(info.Name))
        {
            searchResults = await SearchMovie(info.Name.Replace(".", " "), token);
            if (searchResults.Count == 0)
            {
                VideoResolver.TryCleanString(info.Name, new Emby.Naming.Common.NamingOptions(), out var newName);
                if (!string.IsNullOrEmpty(newName) && !string.Equals(info.Name, newName, StringComparison.Ordinal))
                {
                    searchResults = await SearchMovie(newName.Replace(".", " "), token);
                }
            }
        }
        if (searchResults.Count == 0 && !string.IsNullOrEmpty(info.OriginalTitle) && !string.Equals(info.OriginalTitle, info.Name, StringComparison.Ordinal))
        {
            searchResults = await SearchMovie(info.OriginalTitle.Replace(".", " "), token);
        }
        if (searchResults.Count > 0 && (info is SeriesInfo || (info is SeasonInfo seasonInfo2 && seasonInfo2.IndexNumber == 1)))
        {
            var season = Regex.Match(searchResults[0].Name!, @"第([一二三四五六七八九十百千万\d]+)季");
            if (season.Success && !new string[] { "一", "1" }.Contains(season.Groups[1].Value))
            {
                searchResults = await SearchMovie(Regex.Replace(searchResults[0].Name!, @"第([一二三四五六七八九十百千万\d]+)季", "第一季"), token);
            }
        }
        searchResults = searchResults.OrderBy(_ => _.Type == (info is MovieInfo ? "电影" : "电视剧") ? 0 : 1).ToList();
        var results = searchResults.Select(_ =>
        {
            var result = new RemoteSearchResult
            {
                Name = _.Name,
                SearchProviderName = _.OriginalName,
                ImageUrl = string.IsNullOrEmpty(_.PosterId) ? "" : $"https://img2.doubanio.com/view/photo/l/public/{_.PosterId}.jpg",
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
        _log.LogInformation($"Fetching movie: {sid}");
        string url = $"https://movie.douban.com/subject/{sid}/";

        if (DateTime.Now - _lastSearch < _timeSpan)
        {
            await Task.Delay(_timeSpan - (DateTime.Now - _lastSearch), token).ConfigureAwait(false);
        }
        HttpResponseMessage response = await _httpClient.GetAsync(url, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogError($"Response: {response.StatusCode}");
            return new ApiMovieSubject();
        }
        string responseText = await response.Content.ReadAsStringAsync(token);
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var name = Regex.Replace(htmlDoc.QuerySelector("title").InnerText.Trim(), @" \(豆瓣\)$", "");
        var content = htmlDoc.QuerySelector("#content");
        var posterId = Regex.Match(content.QuerySelector("#mainpic img").Attributes["src"].Value, @"/(p\d+)\.(?:webp|png|jpg|jpeg|gif)$").Groups[1].Value;
        var originalName = content.QuerySelector("h1 span").InnerText.Replace(name, "").Trim();
        var year = Convert.ToInt32(content.QuerySelector("h1 .year").InnerText.Trim().TrimStart('(').TrimEnd(')'));
        var rating = content.QuerySelector("#interest_sectl .rating_num").InnerText.Trim();
        rating = string.IsNullOrEmpty(rating) ? "0.0" : rating;
        var info = content.QuerySelector("#info").InnerText.Trim().Split("\n").Select(_ => _.Trim().Split(": ")).Where(_ => _.Length > 1).ToDictionary(_ => _[0], _ => string.Join(": ", _[1..]).Trim());
        var type = "电影";
        if (info.ContainsKey("集数") || info.ContainsKey("单集片长")) { type = "电视剧"; }
        var intro = string.Join("\n", content.QuerySelector("#link-report-intra span").InnerText.Trim().Split("\n").Select(_ => _.Trim()));
        var screenTime = info!.GetValueOrDefault("上映日期", "")!.Split("/").Select(_ => Regex.Replace(_.Trim(), @"\(.+?\)?$", "")).FirstOrDefault();
        var seasonIndex = 0;
        if (content.QuerySelector("#season option[selected]") is HtmlNode selected) { seasonIndex = Convert.ToInt32(selected.InnerText.Trim()); }

        var result = new ApiMovieSubject()
        {
            Sid = sid,
            Name = name,
            PosterId = posterId,
            Type = type,
            Rating = Convert.ToDecimal(rating),
            OriginalName = originalName,
            Year = year,
            Intro = intro,
            Genre = info!.GetValueOrDefault("类型", "")!.Split("/").Select(_ => _.Trim()).ToArray(),
            Website = info!.GetValueOrDefault("官方网站", null),
            Country = info!.GetValueOrDefault("制片国家/地区", "")!.Split("/").Select(_ => _.Trim()).ToArray(),
            ScreenTime = string.IsNullOrEmpty(screenTime) ? null : Convert.ToDateTime(screenTime),
            ImdbId = info!.GetValueOrDefault("IMDb", null),
            SeasonIndex = seasonIndex,
        };
        return result;
    }

    public async Task<ApiMovieSubject> FetchMovie(ItemLookupInfo info, CancellationToken token = default)
    {
        if (!int.TryParse(info.ProviderIds.GetValueOrDefault(Constants.ProviderId), out var subjectId))
        {
            int.TryParse(info.ProviderIds.GetValueOrDefault(Constants.OddbId), out subjectId);
        }

        if (subjectId == 0 && info is SeasonInfo seasonInfo && seasonInfo.IndexNumber == 1)
        {
            if (!int.TryParse(seasonInfo.SeriesProviderIds.GetValueOrDefault(Constants.ProviderId), out subjectId))
            {
                int.TryParse(seasonInfo.SeriesProviderIds.GetValueOrDefault(Constants.OddbId), out subjectId);
            }
        }

        if (subjectId == 0)
        {
            var searchResults = (await GetMovieSearchResults(info, token)).ToList();
            if (searchResults.Count > 0)
            {
                if (!int.TryParse(searchResults[0].GetProviderId(Constants.ProviderId), out subjectId))
                {
                    int.TryParse(searchResults[0].GetProviderId(Constants.OddbId), out subjectId);
                }
            }
        }

        if (subjectId == 0)
        {
            return new ApiMovieSubject();
        }

        return await FetchMovie(subjectId.ToString(), token);
    }

    public async Task<List<PersonInfo>> FetchMovieCelebrities(string sid, CancellationToken token = default)
    {
        _log.LogInformation($"Fetching celebrities for movie: {sid}");
        string url = $"https://movie.douban.com/subject/{sid}/celebrities";

        if (DateTime.Now - _lastSearch < _timeSpan)
        {
            await Task.Delay(_timeSpan - (DateTime.Now - _lastSearch), token).ConfigureAwait(false);
        }
        HttpResponseMessage response = await _httpClient.GetAsync(url, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogError($"Response: {response.StatusCode}");
            return new List<PersonInfo>();
        }
        string responseText = await response.Content.ReadAsStringAsync(token);
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var celebrities = htmlDoc.QuerySelectorAll(".celebrities-list .celebrity");
        var results = celebrities.Select(_ =>
        {
            var link = _.QuerySelector("a.name");
            var name = link.InnerText.Trim().Split(" ")[0];
            var cid = Regex.Match(link.Attributes["href"].Value, @"/celebrity/(\d+)/").Groups[1].Value;
            var posterUrl = Regex.Match(_.QuerySelector(".avatar").Attributes["style"].Value, @"url\((.+?\.(?:webp|png|jpg|jpeg|gif))\)").Groups[1].Value;
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
                    "作曲" => PersonType.Composer,
                    _ => type,
                },
                Role = role,
            };
            result.SetProviderId(Constants.ProviderId, cid);
            return result;
        }).ToList();
        return results;
    }

    public async Task<List<RemoteImageInfo>> FetchMovieImages(string sid, string type = "R", ImageType imageType=ImageType.Primary, CancellationToken token = default)
    {
        _log.LogInformation($"Fetching images for movie: {sid}");
        string url = $"https://movie.douban.com/subject/{sid}/photos?type={type}";

        if (DateTime.Now - _lastSearch < _timeSpan)
        {
            await Task.Delay(_timeSpan - (DateTime.Now - _lastSearch), token).ConfigureAwait(false);
        }
        HttpResponseMessage response = await _httpClient.GetAsync(url, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogError($"Response: {response.StatusCode}");
            return new List<RemoteImageInfo>();
        }
        string responseText = await response.Content.ReadAsStringAsync(token);
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
                Type = imageType,
                ThumbnailUrl = $"https://img2.doubanio.com/view/photo/s/public/{posterId}.jpg",
                Url = $"https://img2.doubanio.com/view/photo/l/public/{posterId}.jpg",
                Width = width,
                Height = height,
            };
        }).ToList();
        return results;
    }

    public async Task<List<ApiPersonSubject>> SearchPerson(string keyword,  CancellationToken token = default)
    {
        _log.LogInformation($"Searching person: {keyword}");
        string url = $"https://movie.douban.com/j/subject_suggest?q={keyword}";

        if (DateTime.Now - _lastSearch < _timeSpan)
        {
            await Task.Delay(_timeSpan - (DateTime.Now - _lastSearch), token).ConfigureAwait(false);
        }
        HttpResponseMessage response = await _httpClient.GetAsync(url, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogError($"Response: {response.StatusCode}");
            return new List<ApiPersonSubject>();
        }
        string responseText = await response.Content.ReadAsStringAsync(token);

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
        _log.LogInformation($"{results.Count} result(s) found.");
        return results;
    }

    public async Task<ApiPersonSubject> FetchPerson(string cid, CancellationToken token = default)
    {
        _log.LogInformation($"Fetching celebrity: {cid}");
        string url = $"https://movie.douban.com/celebrity/{cid}/";

        if (DateTime.Now - _lastSearch < _timeSpan)
        {
            await Task.Delay(_timeSpan - (DateTime.Now - _lastSearch), token).ConfigureAwait(false);
        }
        HttpResponseMessage response = await _httpClient.GetAsync(url, token).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
        {
            _log.LogError($"Response: {response.StatusCode}");
            return new ApiPersonSubject();
        }
        string responseText = await response.Content.ReadAsStringAsync(token);

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);
        var content = htmlDoc.QuerySelector("#content");
        var image = content.QuerySelector("#headline .pic img");
        var name = image.Attributes["alt"].Value;
        var posterUrl = image.Attributes["src"].Value;
        var originalName = content.QuerySelector("h1").InnerText.Replace(name, "").Trim();
        var info = content.QuerySelectorAll(".info ul li").Select(_ => _.InnerText.Trim().Split(": ")).Where(_ => _.Length > 1).ToDictionary(_ => _[0], _ => string.Join(": ", _[1..]).Trim());
        var intro = string.Join("\n", (content.QuerySelector("#intro .bd .all") ?? content.QuerySelector("#intro .bd")).InnerText.Trim().Split("\n").Select(_ => _.Trim()));
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
            OriginalName = originalName,
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
}
