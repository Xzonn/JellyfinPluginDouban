using HtmlAgilityPack;
using Jellyfin.Plugin.Douban.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using System.Text.RegularExpressions;
using System.Web;

using static AnitomySharp.Element;

#if NET8_0_OR_GREATER
using PersonType = Jellyfin.Data.Enums.PersonKind;
#else
using PersonType = MediaBrowser.Model.Entities.PersonType;
#endif

namespace Jellyfin.Plugin.Douban;

public static class Helper
{
    public const string DEFAULT_CDN_SERVER = "https://img2.doubanio.com";
    public const bool DEFAULT_DISTINGUISH_USING_ASPECT_RATIO = true;
    public const bool DEFAULT_FETCH_STAGE_PHOTO = true;
    public const bool DEFAULT_FETCH_CELEBRITY_IMAGES = true;

    private static Regex REGEX_SID => new(@"\s*sid:\s*(\d+)");
    private static Regex REGEX_IMAGE => new(@"/(p\d+)\.(?:webp|png|jpg|jpeg|gif)$");
    private static Regex REGEX_IMAGE_URL => new(@"url\((.+?\.(?:webp|png|jpg|jpeg|gif))\)");
    private static Regex REGEX_ORIGINAL_NAME => new(@"^原名:");
    private static Regex REGEX_DOUBAN_POSTFIX => new(@" \(豆瓣\)$");
    private static Regex REGEX_BRACKET => new(@"\(.+?\)?$");
    private static Regex REGEX_DATE => new(@"\d{4}-\d\d-\d\d");
    private static Regex REGEX_CELEBRITY => new(@"/celebrity/(\d+)/");
    private static Regex REGEX_DOUBANIO_HOST => new(@"https?://img\d+\.doubanio.com");
    private static Regex REGEX_SEASON => new(@" *第(?<season>[一二三四五六七八九十百千万\d]+)[季期部]| *\b(?:Season) (?<season>\d+)", RegexOptions.IgnoreCase);
    private static Regex REGEX_SEASON_2 => new(@"(?<![a-z\d\.']|女神异闻录|Part )(?<season>\d{1,2})$");
    private static Regex REGEX_IMAGE_VOTE => new(@"(\d+)回应");

    public static string? AnitomySharpParse(string name, ElementCategory category)
    {
        try
        {
            var element = AnitomySharp.AnitomySharp.Parse(name)?.FirstOrDefault(p => p.Category == category);
            return element?.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }

    public static int ConvertChineseNumberToNumber(string chinese)
    {
        if (string.IsNullOrWhiteSpace(chinese)) { return 0; }
        chinese = chinese.Trim();
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

    public static int GuessSeasonIndex(ItemLookupInfo info)
    {
        int index = ParseSeasonIndex(info.Name);
        if (index == 0) { index = ParseSeasonIndex(info.OriginalTitle); }
        if (index == 0) { index = ParseSeasonIndex(Path.GetFileName(info.Path)); }
        if (index == 0) { int.TryParse(AnitomySharpParse(Path.GetFileName(info.Path), ElementCategory.ElementAnimeSeason), out index); }
        if (index == 0) { index = ParseSeasonIndex(info.Name, REGEX_SEASON_2); }

        return index;
    }

    public static int ParseSeasonIndex(string name, Regex? pattern = null)
    {
        if (string.IsNullOrWhiteSpace(name)) { return 0; }

        var seasonMatch = (pattern ?? REGEX_SEASON).Match(name ?? "");
        var season = ConvertChineseNumberToNumber(seasonMatch.Groups.GetValueOrDefault("season")?.Value ?? "");

        return season;
    }

    public static string ReplaceSeasonIndexWith(string name, int index)
    {
        name = $"{REGEX_SEASON_2.Replace(REGEX_SEASON.Replace(name, ""), "")} 第{index}季";
        return name;
    }

    public static int ParseDoubanId(IHasProviderIds info, bool ignoreSeasonIndex = false)
    {
        int id = 0;
        if (info is EpisodeInfo episodeInfo)
        {
#if NET8_0_OR_GREATER
            int.TryParse(episodeInfo.SeasonProviderIds.GetValueOrDefault(Constants.ProviderId), out id);
            if (id == 0) { int.TryParse(episodeInfo.SeasonProviderIds.GetValueOrDefault(Constants.ProviderId_Old), out id); }
            if (id == 0) { int.TryParse(episodeInfo.SeasonProviderIds.GetValueOrDefault(Constants.ProviderId_OpenDouban), out id); }
#endif

            if (id == 0)
            {
                int.TryParse(episodeInfo.SeriesProviderIds.GetValueOrDefault(Constants.ProviderId), out id);
                if (id == 0) { int.TryParse(episodeInfo.SeriesProviderIds.GetValueOrDefault(Constants.ProviderId_Old), out id); }
                if (id == 0) { int.TryParse(episodeInfo.SeriesProviderIds.GetValueOrDefault(Constants.ProviderId_OpenDouban), out id); }
            }

            if (id == 0)
            {
                var episodeId = episodeInfo.GetProviderId(Constants.ProviderId);
                episodeId ??= episodeInfo.ProviderIds.GetValueOrDefault(Constants.ProviderId_Old);

                if (!string.IsNullOrEmpty(episodeId) && episodeId.Contains("/episode/"))
                {
                    int.TryParse(episodeId.Split("/episode/")[0], out id);
                }
            }
        }
        else
        {
            int.TryParse(info.GetProviderId(Constants.ProviderId), out id);
            if (id == 0) { int.TryParse(info.GetProviderId(Constants.ProviderId_Old), out id); }
            if (id == 0) { int.TryParse(info.GetProviderId(Constants.ProviderId_OpenDouban), out id); }

            if (id == 0 && info is SeasonInfo seasonInfo && ((seasonInfo.IndexNumber ?? 0) < 2 || ignoreSeasonIndex))
            {
                int.TryParse(seasonInfo.SeriesProviderIds.GetValueOrDefault(Constants.ProviderId), out id);
                if (id == 0) { int.TryParse(seasonInfo.SeriesProviderIds.GetValueOrDefault(Constants.ProviderId_Old), out id); }
                if (id == 0) { int.TryParse(seasonInfo.SeriesProviderIds.GetValueOrDefault(Constants.ProviderId_OpenDouban), out id); }
            }
        }
        return id;
    }

    public static async Task<int> ParseDoubanPersonageId(IHasProviderIds info, DoubanApi api, CancellationToken token = default)
    {
        token.ThrowIfCancellationRequested();

        if (!int.TryParse(info.GetProviderId(Constants.PersonageId), out var pid) && !int.TryParse(info.GetProviderId(Constants.PersonageId_Old), out pid))
        {
            // Fetch person by celebrity id
            var cid = ParseDoubanId(info);
            if (cid != 0)
            {
                int.TryParse(await api.ConvertCelebrityIdToPersonageId(cid.ToString(), token), out pid);
            }
        }
        return pid;
    }

    public static List<ApiMovieSubject> ParseSearchMovieResults(string responseText, string keyword = "", bool isMovie = true, bool isFirstSeason = false)
    {
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var resultList = htmlDoc.QuerySelector(".result-list");
        if (resultList is null) { return []; }
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
            if (first != keyword)
            {
                results = [.. results.OrderBy(_ => _.Name != first && first.StartsWith(_.Name!) ? 0 : 1)];
            }
        }
        return results;
    }

    public static ApiMovieSubject ParseMovie(string responseText, string sid)
    {
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var name = HttpUtility.HtmlDecode(REGEX_DOUBAN_POSTFIX.Replace(htmlDoc.QuerySelector("title").InnerText.Trim(), ""));
        var content = htmlDoc.QuerySelector("#content");
        var posterId = REGEX_IMAGE.Match(content?.QuerySelector("#mainpic img")?.Attributes["src"].Value ?? "")?.Groups[1].Value;
        var originalName = content?.QuerySelector("h1 span")?.InnerText.Replace(name, "").Trim();
        var year = Convert.ToInt32(content?.QuerySelector("h1 .year")?.InnerText.Trim().TrimStart('(').TrimEnd(')'));
        var rating = content?.QuerySelector("#interest_sectl .rating_num")?.InnerText.Trim();
        rating = string.IsNullOrEmpty(rating) ? "0.0" : rating;
        var info = content?.QuerySelector("#info")?.InnerText.Trim().Split("\n").Select(_ => _.Trim().Split(":", 2)).Where(_ => _.Length > 1).ToDictionary(_ => _[0], _ => _[1].Trim()) ?? [];
        var type = "电影";
        if (info.ContainsKey("集数") || info.ContainsKey("单集片长")) { type = "电视剧"; }
        var intro = string.Join("\n", (content?.QuerySelector("#link-report-intra span.all") ?? content?.QuerySelector("#link-report-intra span"))?.InnerText.Trim().Split("\n").Select(_ => _.Trim()) ?? []);
        var screenTime = info.GetValueOrDefault("上映日期", info.GetValueOrDefault("首播", "")).Split("/").Select(_ => REGEX_BRACKET.Replace(_.Trim(), "")).Where(_ => REGEX_DATE.IsMatch(_)).FirstOrDefault();

        var otherNames = info.GetValueOrDefault("又名", "").Split("/").Select(_ => _.Trim());
        var seasonIndex = 0;
        if (content.QuerySelector("#season option[selected]") is HtmlNode selected)
        {
            seasonIndex = Convert.ToInt32(selected.InnerText.Trim());
        }
        else if (info.TryGetValue("季数", out var seasonNumber))
        {
            seasonIndex = Convert.ToInt32(seasonNumber);
        }
        else if (type == "电视剧")
        {
            seasonIndex = ParseSeasonIndex(name);
            if (seasonIndex == 0) { seasonIndex = ParseSeasonIndex(originalName ?? ""); }
            foreach (var otherName in otherNames)
            {
                if (seasonIndex != 0) { break; }
                seasonIndex = ParseSeasonIndex(otherName);
            }
        }
        int.TryParse(info.GetValueOrDefault("集数", "0"), out var episodeCount);

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

    public static List<PersonInfo> ParseMovieCelebrities(string responseText, string sid, bool fetchCelebrityImages = DEFAULT_FETCH_CELEBRITY_IMAGES, string cdnServer = DEFAULT_CDN_SERVER)
    {
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var lists = htmlDoc.QuerySelectorAll("#celebrities .list-wrapper");
        List<PersonInfo> results = [];
        foreach (var list in lists)
        {
            var topType = (list.QuerySelector("h2")?.InnerText.Trim().Split(" ") ?? [""])[0];
            foreach (var _ in list.QuerySelectorAll("ul.celebrities-list li.celebrity"))
            {
                var link = _.QuerySelector("a.name");
                var name = link?.InnerText.Trim().Split(" ")[0];
                var cid = REGEX_CELEBRITY.Match(link?.Attributes["href"].Value ?? "")?.Groups[1].Value;
                var posterUrl = REGEX_IMAGE_URL.Match(_.QuerySelector(".avatar")?.Attributes["style"].Value ?? "")?.Groups[1].Value;
                if (!fetchCelebrityImages || (posterUrl ?? "").Contains("celebrity-default"))
                {
                    posterUrl = null;
                }
                else
                {
                    posterUrl = REGEX_DOUBANIO_HOST.Replace(posterUrl!, cdnServer);
                }
                string[] roleText = _.QuerySelector(".role")?.InnerText.Trim().Split(" ") ?? [topType];
                var type = roleText[0];
                var convertedType = ConvertTypeString(type) ?? ConvertTypeString(topType);
                if (convertedType is null) { continue; }

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
#if NET8_0_OR_GREATER
                    Type = (PersonType)convertedType,
#else
                    Type = convertedType,
#endif
                    Role = role,
                };
                result.SetProviderId(Constants.ProviderId, cid);
                results.Add(result);
            }
        }
        return results;

#if NET8_0_OR_GREATER
        static PersonType? ConvertTypeString(string type)
#else
        static string? ConvertTypeString(string type)
#endif
        {
            return type switch
            {
                "导演" => PersonType.Director,
                "演员" => PersonType.Actor,
                "配音" => PersonType.Actor,
                "编剧" => PersonType.Writer,
                "脚本" => PersonType.Writer,
                "剧本" => PersonType.Writer,
                "制片人" => PersonType.Producer,
                "制作人" => PersonType.Producer,
                "作曲" => PersonType.Composer,
                "音乐" => PersonType.Composer,
                "编曲" => PersonType.Arranger,
                _ => null,
            };
        }
    }

    public static List<RemoteImageInfo> ParseImages(string responseText, string cdnServer) => ParseImages(responseText, ImageType.Primary, DEFAULT_DISTINGUISH_USING_ASPECT_RATIO, cdnServer);

    public static List<RemoteImageInfo> ParseImages(string responseText, ImageType imageType = ImageType.Primary, bool distinguishUsingAspectRatio = DEFAULT_DISTINGUISH_USING_ASPECT_RATIO, string cdnServer = DEFAULT_CDN_SERVER)
    {
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var results = htmlDoc.QuerySelectorAll(".article ul li").Select(_ =>
        {
            var posterId = REGEX_IMAGE.Match(_.QuerySelector("img").Attributes["src"].Value).Groups[1].Value;
            var size = (_.QuerySelector(".prop") ?? _.QuerySelector(".size"))?.InnerText.Trim().Split("x") ?? ["0", "0"];
            var width = Convert.ToInt32(size[0]);
            var height = Convert.ToInt32(size[1]);
            int.TryParse(REGEX_IMAGE_VOTE.Match(_.QuerySelector(".name a")?.InnerText ?? "").Groups[1].Value, out var vote);
            return new RemoteImageInfo()
            {
                ProviderName = Constants.PluginName,
                Language = Constants.Language,
                Type = distinguishUsingAspectRatio ? (width > height ? ImageType.Backdrop : ImageType.Primary) : imageType,
                ThumbnailUrl = $"{cdnServer}/view/photo/s/public/{posterId}.jpg",
                Url = $"{cdnServer}/view/photo/l/public/{posterId}.jpg",
                Width = width,
                Height = height,
                CommunityRating = vote == 0 ? null : vote,
                RatingType = RatingType.Likes,
            };
        }).ToList();
        return results;
    }

    public static ApiEpisodeSubject ParseMovieEpisode(string responseText)
    {
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var title = htmlDoc.QuerySelector("title").InnerText.Trim();
        var info = htmlDoc.QuerySelectorAll("#content .ep-info li").Select(_ => _.InnerText.Trim().Split(":", 2)).Where(_ => _.Length > 1).ToDictionary(_ => _[0], _ => string.Join(":\n", _[1..]).Trim()) ?? [];
        var name = info!.GetValueOrDefault("本集中文名", "暂无，欢迎添加");
        var originalName = info!.GetValueOrDefault("本集原名", "暂无，欢迎添加");
        var screenTimeStr = info!.GetValueOrDefault("播放时间", "暂无，欢迎添加");
        DateTime screenTime = DateTime.MinValue;
        try { DateTime.TryParse(screenTimeStr[..10], out screenTime); } catch { }
        var intro = htmlDoc.QuerySelector("meta[name=\"description\"]")?.Attributes["content"]?.Value.Trim();

        var result = new ApiEpisodeSubject()
        {
            Name = name == "暂无，欢迎添加" ? "" : name,
            OriginalName = originalName == "暂无，欢迎添加" ? null : originalName,
            ScreenTime = screenTime == DateTime.MinValue ? null : screenTime,
            Intro = info!.GetValueOrDefault("剧情简介", "暂无，欢迎添加") == "暂无，欢迎添加" ? null : intro,
        };
        return result;
    }

    public static List<ApiPersonSubject> ParseSearchPersonResults(string responseText)
    {
        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var resultList = htmlDoc.QuerySelector(".result-list");
        if (resultList == null) { return []; }
        var results = resultList.ChildNodes.Where(_ => _.HasClass("result")).Select(_ =>
        {
            var link = _.QuerySelector(".content .title h3 a");
            var sid = REGEX_SID.Match(link.Attributes["onclick"].Value).Groups[1].Value;
            var name = link.InnerText.Trim();
            var posterUrl = _.QuerySelector(".pic img").Attributes["src"].Value;
            var type = _.QuerySelector(".content .title h3 span").InnerText.Trim().TrimStart('[').TrimEnd(']');
            return new ApiPersonSubject()
            {
                PersonageId = sid,
                Name = name,
                PosterUrl = posterUrl,
            };
        }).ToList();
        return results;
    }

    public static ApiPersonSubject ParsePerson(string responseText, string pid, string cdnServer = DEFAULT_CDN_SERVER)
    {

        var htmlDoc = new HtmlDocument();
        htmlDoc.LoadHtml(responseText);

        var content = htmlDoc.QuerySelector("#content");
        var image = content.QuerySelector("#headline .pic img");
        image ??= content.QuerySelector(".subject-target img.avatar");
        var name = image.Attributes["alt"].Value;
        var posterUrl = image.Attributes["src"].Value;
        if (posterUrl.Contains("celebrity-default"))
        {
            posterUrl = null;
        }
        else
        {
            posterUrl = REGEX_DOUBANIO_HOST.Replace(posterUrl, cdnServer);
        }
        var originalName = content.QuerySelector("h1").InnerText.Replace(name, "").Trim();
        var infoList = content.QuerySelector(".info ul");
        infoList ??= content.QuerySelector(".subject-target ul.subject-property");
        var info = infoList.QuerySelectorAll("li").Select(_ => _.InnerText.Trim().Split(":", 2)).Where(_ => _.Length > 1).ToDictionary(_ => _[0], _ => _[1].Trim());
        var introElement = content.QuerySelector("#intro .bd .all");
        introElement ??= content.QuerySelector("#intro .bd");
        introElement ??= content.QuerySelector(".desc .content .content");
        var intro = string.Join("\n", introElement?.InnerText.Trim().Split("\n").Select(_ => _.Trim()) ?? []);
        var birthdate = info!.GetValueOrDefault("出生日期", null);
        var deathdate = info!.GetValueOrDefault("去世日期", null);
        if (info.TryGetValue("生卒日期", out string? birthAndDeath))
        {
            birthdate = birthAndDeath.Split("至")[0].Trim();
            deathdate = birthAndDeath.Split("至")[1].Trim();
        }
        var bitrhplace = info!.GetValueOrDefault("出生地", null);
        var result = new ApiPersonSubject()
        {
            PersonageId = pid,
            Name = name,
            PosterUrl = posterUrl,
            OriginalName = string.IsNullOrEmpty(originalName) ? name : originalName,
            Intro = intro,
            Gender = info!.GetValueOrDefault("性别", null),
            Birthdate = string.IsNullOrEmpty(birthdate) ? null : Convert.ToDateTime(birthdate),
            Deathdate = string.IsNullOrEmpty(deathdate) ? null : Convert.ToDateTime(deathdate),
            Birthplace = string.IsNullOrEmpty(bitrhplace) ? null : [bitrhplace],
            Website = info!.GetValueOrDefault("官方网站", null),
            ImdbId = info!.GetValueOrDefault("imdb编号", info!.GetValueOrDefault("IMDb编号", null)),
        };
        return result;
    }
}
