using Jellyfin.Plugin.Douban.Provider;
using MediaBrowser.Controller.Providers;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.Douban.Test
{
    public class SeriesTest
    {
        private readonly SeriesProvider _provider;

        public SeriesTest(ITestOutputHelper output)
        {
            var serviceProvider = ServiceUtils.BuildServiceProvider<SeriesProvider>(output);
            _provider = serviceProvider.GetService<SeriesProvider>();
        }

        [Fact]
        public async Task TestGetMetadata()
        {
            var result = await _provider.GetMetadata(new SeriesInfo
            {
                Name = "民王",
                Path = Path.Combine(Path.GetTempPath(), "民王 (2015)"),
                Year = 2015,
                IsAutomated = true,
            }, new System.Threading.CancellationToken());
            Assert.True(result.HasMetadata);
        }
    }
}
