using Jellyfin.Plugin.Douban.Provider;
using MediaBrowser.Controller.Entities.TV;
using Microsoft.Extensions.DependencyInjection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.Douban.Test
{
    public class MovieImageTest
    {
        private readonly MovieImageProvider _provider;

        public MovieImageTest(ITestOutputHelper output)
        {
            var serviceProvider = ServiceUtils.BuildServiceProvider<MovieImageProvider>(output);
            _provider = serviceProvider.GetService<MovieImageProvider>();
        }

        [Fact]
        public async Task TestGetImages()
        {
            var result = (await _provider.GetImages(new Season
            {
                ProviderIds = new Dictionary<string, string> { [Constants.ProviderId] = "2015069" }
            }, new System.Threading.CancellationToken())).ToList();
            Assert.NotEmpty(result);
        }
    }
}
