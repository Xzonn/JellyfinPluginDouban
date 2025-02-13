using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using System.Threading.Tasks;
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
        public async Task TestSearchMovie()
        {
            var list = await _doubanApi.SearchMovie("我是大哥大", false, false);
            Assert.NotEmpty(list);
            Assert.Equal("我是大哥大", list[0].Name ?? "");

            list = await _doubanApi.SearchMovie("tt8207660", false, false);
            Assert.NotEmpty(list);
            Assert.Equal("我是大哥大", list[0].Name ?? "");

            list = await _doubanApi.SearchMovie("鬼灭之刃", false, true);
            Assert.NotEmpty(list);
            Assert.Equal("鬼灭之刃", list[0].Name ?? "");
        }

        [Fact]
        public async Task TestGetMovieSearchResults()
        {
            var list = await _doubanApi.GetMovieSearchResults(new MovieInfo()
            {
                Name = "我是大哥大",
            });
            Assert.NotEmpty(list);
            Assert.Equal("33400537", list[0].GetProviderId(Constants.ProviderId));

            list = await _doubanApi.GetMovieSearchResults(new SeriesInfo()
            {
                Name = "三体",
                Year = 2024,
            });
            Assert.NotEmpty(list);
            Assert.Equal("35196946", list[0].GetProviderId(Constants.ProviderId));

            list = await _doubanApi.GetMovieSearchResults(new SeasonInfo()
            {
                Name = "洛基",
                IndexNumber = 1,
            });
            Assert.NotEmpty(list);
            Assert.Equal("30331432", list[0].GetProviderId(Constants.ProviderId));
        }

        [Fact]
        public async Task TestFetchMovie()
        {
            var result = await _doubanApi.FetchMovie("30183785");
            Assert.Equal("我是大哥大", result.Name ?? "");

            result = await _doubanApi.FetchMovie("33400537");
            Assert.Equal("我是大哥大 电影版", result.Name ?? "");

            result = await _doubanApi.FetchMovie("27199894");
            Assert.Equal("超级马力欧兄弟大电影", result.Name ?? "");

            result = await _doubanApi.FetchMovie("26299465");
            Assert.Equal("探索未知 第一季", result.Name ?? "");

            result = await _doubanApi.FetchMovie("35873969");
            Assert.Equal("名侦探柯南：黑铁的鱼影", result.Name ?? "");
        }

        [Fact]
        public async Task TestFetchMovieCelebrities()
        {
            var list = await _doubanApi.FetchMovieCelebrities("33400537");
            Assert.NotEmpty(list);
            Assert.Equal("福田雄一", list[0].Name ?? "");

            list = await _doubanApi.FetchMovieCelebrities("4074292");
            Assert.NotEmpty(list);
            Assert.Equal("石原立也", list[0].Name ?? "");

            list = await _doubanApi.FetchMovieCelebrities("26387719");
            Assert.NotEmpty(list);
            Assert.Equal("木村尚", list[0].Name ?? "");

            list = await _doubanApi.FetchMovieCelebrities("35766491");
            Assert.NotEmpty(list);
            Assert.Equal("张艺谋", list[0].Name ?? "");
        }

        [Fact]
        public async Task TestFetchPerson()
        {
            var person = await _doubanApi.FetchPersonByCelebrityId("1321489");
            Assert.Equal("佐仓绫音", person.Name ?? "");

            person = await _doubanApi.FetchPersonByPersonageId("27555771");
            Assert.Equal("佐仓绫音", person.Name ?? "");
        }

        [Fact]
        public async Task TestFetchMovieImages()
        {
            var list = await _doubanApi.FetchMovieImages("33400537", "R");
            Assert.NotEmpty(list);
            
            list = await _doubanApi.FetchMovieImages("26816519", "S");
            Assert.NotEmpty(list);
        }
    }
}
