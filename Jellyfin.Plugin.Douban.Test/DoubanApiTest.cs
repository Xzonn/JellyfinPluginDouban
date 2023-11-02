using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Xunit.Abstractions;

namespace Jellyfin.Plugin.Douban.Test
{
    public class DoubanApiTest
    {
        private readonly DoubanApi _doubanApi;

        public DoubanApiTest(ITestOutputHelper output)
        {
            var serviceProvider = ServiceUtils.BuildServiceProvider<DoubanApi>(output);
            _doubanApi = serviceProvider.GetService<DoubanApi>();
        }

        [Fact]
        public void TestSearchMovie()
        {
            var list = _doubanApi.SearchMovie("我是大哥大").Result;
            Assert.NotEmpty(list);
            Assert.Equal("我是大哥大", list[0].Name ?? "");

            list = _doubanApi.SearchMovie("tt8207660").Result;
            Assert.NotEmpty(list);
            Assert.Equal("我是大哥大", list[0].Name ?? "");
        }

        [Fact]
        public void TestGetMovieSearchResults()
        {
            var list = _doubanApi.GetMovieSearchResults(new MovieInfo()
            {
                Name = "我是大哥大",
            }).Result;
            Assert.NotEmpty(list);
            Assert.Equal("33400537", list[0].GetProviderId(Constants.ProviderId));

            list = _doubanApi.GetMovieSearchResults(new SeasonInfo()
            {
                Name = "洛基",
                IndexNumber = 1,
            }).Result;
            Assert.NotEmpty(list);
            Assert.Equal("30331432", list[0].GetProviderId(Constants.ProviderId));
        }

        [Fact]
        public void TestFetchMovie()
        {
            var result = _doubanApi.FetchMovie("33400537").Result;
            Assert.Equal("我是大哥大 电影版", result.Name ?? "");

            result = _doubanApi.FetchMovie("27199894").Result;
            Assert.Equal("超级马力欧兄弟大电影", result.Name ?? "");

            result = _doubanApi.FetchMovie("26299465").Result;
            Assert.Equal("探索未知 第一季", result.Name ?? "");
        }

        [Fact]
        public void TestFetchMovieCelebrities()
        {
            var list = _doubanApi.FetchMovieCelebrities("33400537").Result;
            Assert.NotEmpty(list);
            Assert.Equal("福田雄一", list[0].Name ?? "");
        }
    }
}
