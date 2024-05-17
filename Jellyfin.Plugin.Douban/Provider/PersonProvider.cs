using Jellyfin.Plugin.Douban.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Jellyfin.Plugin.Douban.Provider;

public class PersonProvider(DoubanApi api, ILogger<PersonProvider> logger) : IRemoteMetadataProvider<Person, PersonLookupInfo>, IHasOrder
{
    public int Order => 0;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        logger.LogDebug("PersonLookupInfo: {info:l}", JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions));
        var result = new MetadataResult<Person> { ResultLanguage = Constants.Language };

        if (!int.TryParse(info.GetProviderId(Constants.PersonageId), out var pid))
        {
            // Fetch person by celebrity id
            if (int.TryParse(info.GetProviderId(Constants.ProviderId), out var cid))
            {
                int.TryParse(await api.ConvertCelebrityIdToPersonageId(cid.ToString(), token), out pid);
            }
        }
        if (pid == 0)
        {
            var searchResults = (await GetSearchResults(info, token)).ToList();
            if (searchResults.Count > 0)
            {
                int.TryParse(searchResults[0].GetProviderId(Constants.PersonageId), out pid);
            }
        }

        if (pid == 0) { return result; }

        var person = await api.FetchPersonByPersonageId(pid.ToString(), token);
        if (person is null || string.IsNullOrEmpty(person.PersonageId)) { return result; }

        result.Item = new Person
        {
            Name = person.Name,
            OriginalTitle = person.OriginalName,
            Overview = person.Intro,
            PremiereDate = person.Birthdate,
            EndDate = person.Deathdate,
            ProductionYear = person.Birthdate?.Year,
            HomePageUrl = person.Website,
            ProductionLocations = person.Birthplace,
        };
        result.Item.SetProviderId(Constants.PersonageId, person.PersonageId);
        if (!string.IsNullOrEmpty(person.ImdbId)) { result.Item.SetProviderId(MetadataProvider.Imdb, person.ImdbId); }
        result.QueriedById = true;
        result.HasMetadata = true;

        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var searchResults = new List<ApiPersonSubject>();

        if (!int.TryParse(searchInfo.GetProviderId(Constants.PersonageId), out var pid))
        {
            // Fetch person by celebrity id
            if (int.TryParse(searchInfo.GetProviderId(Constants.ProviderId), out var cid))
            {
                int.TryParse(await api.ConvertCelebrityIdToPersonageId(cid.ToString(), token), out pid);
            }
        }

        if (pid != 0)
        {
            var subject = await api.FetchPersonByPersonageId(pid.ToString(), token);
            if (subject != null)
            {
                searchResults.Add(subject);
            }
        }
        else if (searchInfo.GetProviderId(MetadataProvider.Imdb) is string imdbId)
        {
            searchResults = await api.SearchPerson(imdbId, token);
        }
        else if (!string.IsNullOrEmpty(searchInfo.Name))
        {
            searchResults = await api.SearchPerson(searchInfo.Name, token);
        }
        var results = searchResults.Select(_ =>
        {
            var result = new RemoteSearchResult
            {
                Name = _.Name,
                SearchProviderName = _.OriginalName,
                ImageUrl = _.PosterUrl,
            };
            result.SetProviderId(Constants.PersonageId, _.PersonageId);
            if (!string.IsNullOrEmpty(_.ImdbId)) { result.SetProviderId(MetadataProvider.Imdb, _.ImdbId); }
            return result;
        });
        return results;
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        logger.LogDebug("Fetching image: {url}", url);
        return await api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}
