using Jellyfin.Plugin.Douban.Provider;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.Douban.Test
{
    public class SeasonTest
    {
        private readonly SeasonProvider _provider;

        public SeasonTest(ITestOutputHelper output)
        {
            var serviceProvider = ServiceUtils.BuildServiceProvider<SeasonProvider>(output);
            _provider = serviceProvider.GetService<SeasonProvider>();
        }

        [Fact]
        public async Task TestGetMetadata()
        {
            var result = await _provider.GetMetadata(new SeasonInfo
            {
                Name = "第 2 季",
                Path = Path.Combine(Path.GetTempPath(), "间谍过家家 第二季 (2023)"),
                Year = 2023,
                IsAutomated = true,
                IndexNumber = 2,
            }, new System.Threading.CancellationToken());
            Assert.True(result.HasMetadata);
            Assert.Equal("36190888", result.Item.GetProviderId(Constants.ProviderId));
            Assert.Equal(2, result.Item.IndexNumber);

            result = await _provider.GetMetadata(new SeasonInfo
            {
                Name = "第 1 季",
                Path = Path.Combine(Path.GetTempPath(), "洛基 第一季"),
                Year = 2023,
                IsAutomated = true,
                IndexNumber = 1,
            }, new System.Threading.CancellationToken());
            Assert.True(result.HasMetadata);
            Assert.Equal("30331432", result.Item.GetProviderId(Constants.ProviderId));
            Assert.Equal(1, result.Item.IndexNumber);
        }
    }
}
