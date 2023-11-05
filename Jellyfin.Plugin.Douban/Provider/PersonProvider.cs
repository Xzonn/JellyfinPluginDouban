using Jellyfin.Plugin.Douban.Model;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace Jellyfin.Plugin.Douban.Provider;

public class PersonProvider : IRemoteMetadataProvider<Person, PersonLookupInfo>, IHasOrder
{
    private readonly DoubanApi _api;
    private readonly ILogger<PersonProvider> _log;

    public PersonProvider(DoubanApi api, ILogger<PersonProvider> logger)
    {
        _api = api;
        _log = logger;
    }

    public int Order => 0;
    public string Name => Constants.ProviderName;

    public async Task<MetadataResult<Person>> GetMetadata(PersonLookupInfo info, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        _log.LogDebug($"PersonLookupInfo: {JsonSerializer.Serialize(info, options: Constants.JsonSerializerOptions)}");
        var result = new MetadataResult<Person> { ResultLanguage = Constants.Language };
        if (!int.TryParse(info.ProviderIds.GetValueOrDefault(Constants.ProviderId), out var personId))
        {
            int.TryParse(info.ProviderIds.GetValueOrDefault(Constants.OddbId), out personId);
        }

        if (personId == 0)
        {
            var searchResults = (await GetSearchResults(info, token)).ToList();
            if (searchResults.Count > 0)
            {
                if (!int.TryParse(searchResults[0].GetProviderId(Constants.ProviderId), out personId))
                {
                    int.TryParse(searchResults[0].GetProviderId(Constants.OddbId), out personId);
                }
            }
        }

        if (personId == 0) { return result; }

        var person = await _api.FetchPerson(personId.ToString(), token);
        if (string.IsNullOrEmpty(person.Cid)) { return result; }
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
        result.Item.SetProviderId(Constants.ProviderId, person.Cid);
        if (!string.IsNullOrEmpty(person.ImdbId)) { result.Item.SetProviderId(MetadataProvider.Imdb, person.ImdbId); }
        result.QueriedById = true;
        result.HasMetadata = true;

        return result;
    }

    public async Task<IEnumerable<RemoteSearchResult>> GetSearchResults(PersonLookupInfo searchInfo, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        var searchResults = new List<ApiPersonSubject>();

        if (int.TryParse(searchInfo.ProviderIds.GetValueOrDefault(Constants.ProviderId), out var id) || int.TryParse(searchInfo.ProviderIds.GetValueOrDefault(Constants.OddbId), out id))
        {
            var subject = await _api.FetchPerson(id.ToString(), token);
            if (subject != null)
            {
                searchResults.Add(subject);
            }
        }
        else if (searchInfo.GetProviderId(MetadataProvider.Imdb) is string imdbId)
        {
            searchResults = await _api.SearchPerson(imdbId, token);
        }
        else if (!string.IsNullOrEmpty(searchInfo.Name))
        {
            searchResults = await _api.SearchPerson(searchInfo.Name, token);
        }
        var results = searchResults.Select(_ =>
        {
            var result = new RemoteSearchResult
            {
                Name = _.Name,
                SearchProviderName = _.OriginalName,
                ImageUrl = _.PosterUrl,
            };
            result.SetProviderId(Constants.ProviderId, _.Cid);
            if (!string.IsNullOrEmpty(_.ImdbId)) { result.SetProviderId(MetadataProvider.Imdb, _.ImdbId); }
            return result;
        });
        return results;
    }

    public async Task<HttpResponseMessage> GetImageResponse(string url, CancellationToken token)
    {
        _log.LogDebug($"Fetching image: {url}");
        return await _api.GetHttpClient().GetAsync(url, token).ConfigureAwait(false);
    }
}
