using Jellyfin.Plugin.Douban.Provider;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.Douban.Test
{
    public class PersonTest
    {
        private readonly PersonProvider _provider;

        public PersonTest(ITestOutputHelper output)
        {
            var serviceProvider = ServiceUtils.BuildServiceProvider<PersonProvider>(output);
            _provider = serviceProvider.GetService<PersonProvider>();
        }

        [Fact]
        public async Task TestGetMetadata()
        {
            var result = await _provider.GetMetadata(new PersonLookupInfo
            {
                Name = "及川光博",
            }, new System.Threading.CancellationToken());
            Assert.True(result.HasMetadata);
            Assert.Equal("27248016", result.Item.GetProviderId(Constants.PersonageId));

            result = await _provider.GetMetadata(new PersonLookupInfo
            {
                Name = "吉高由里子",
            }, new System.Threading.CancellationToken());
            Assert.True(result.HasMetadata);
            Assert.Equal("27215727", result.Item.GetProviderId(Constants.PersonageId));
        }
    }
}
