using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Providers;

namespace Jellyfin.Plugin.Douban.Extension;

public class ExternalPersonId : IExternalId
{
    public bool Supports(IHasProviderIds item) => item is Person;

    public string ProviderName => Constants.ProviderName;

    public string Key => Constants.PersonageId;

    public ExternalIdMediaType? Type => ExternalIdMediaType.Person;

    public string UrlFormatString => "https://www.douban.com/personage/{0}/";
}
