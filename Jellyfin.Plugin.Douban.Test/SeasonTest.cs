using Jellyfin.Plugin.Douban.Provider;
using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using System.IO;
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
        public void TestGetMetadata()
        {
            var result = _provider.GetMetadata(new SeasonInfo
            {
                Name = "第 2 季",
                Path = Path.Combine(Path.GetTempPath(), "间谍过家家 第二季 (2023)"),
                Year = 2023,
                IsAutomated = true,
            }, new System.Threading.CancellationToken()).Result;
            Assert.True(result.HasMetadata);
            Assert.Equal("36190888", result.Item.GetProviderId(Constants.ProviderId));
            Assert.Equal(2, result.Item.IndexNumber);

            result = _provider.GetMetadata(new SeasonInfo
            {
                Name = "第 2 季",
                Path = Path.Combine(Path.GetTempPath(), "欢迎来到实力至上主义教室 第2季 (2023)"),
                Year = 2023,
                IsAutomated = true,
            }, new System.Threading.CancellationToken()).Result;
            Assert.True(result.HasMetadata);
            Assert.Equal("35778747", result.Item.GetProviderId(Constants.ProviderId));
            Assert.Equal(2, result.Item.IndexNumber);
        }
    }
}
