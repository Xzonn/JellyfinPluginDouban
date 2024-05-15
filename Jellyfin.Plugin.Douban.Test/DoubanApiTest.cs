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
            var list = await _doubanApi.SearchMovie("���Ǵ���");
            Assert.NotEmpty(list);
            Assert.Equal("���Ǵ���", list[0].Name ?? "");

            list = await _doubanApi.SearchMovie("tt8207660");
            Assert.NotEmpty(list);
            Assert.Equal("���Ǵ���", list[0].Name ?? "");

            list = await _doubanApi.SearchMovie("�����Ӧ (2023)");
            Assert.NotEmpty(list);
            Assert.Equal("�����Ӧ", list[0].Name ?? "");
        }

        [Fact]
        public async Task TestGetMovieSearchResults()
        {
            var list = await _doubanApi.GetMovieSearchResults(new MovieInfo()
            {
                Name = "���Ǵ���",
            });
            Assert.NotEmpty(list);
            Assert.Equal("33400537", list[0].GetProviderId(Constants.ProviderId));

            list = await _doubanApi.GetMovieSearchResults(new SeriesInfo()
            {
                Name = "���� 2024",
            });
            Assert.NotEmpty(list);
            Assert.Equal("35196946", list[0].GetProviderId(Constants.ProviderId));

            list = await _doubanApi.GetMovieSearchResults(new SeasonInfo()
            {
                Name = "���",
                IndexNumber = 1,
            });
            Assert.NotEmpty(list);
            Assert.Equal("30331432", list[0].GetProviderId(Constants.ProviderId));
        }

        [Fact]
        public async Task TestFetchMovie()
        {
            var result = await _doubanApi.FetchMovie("30183785");
            Assert.Equal("���Ǵ���", result.Name ?? "");

            result = await _doubanApi.FetchMovie("33400537");
            Assert.Equal("���Ǵ��� ��Ӱ��", result.Name ?? "");

            result = await _doubanApi.FetchMovie("27199894");
            Assert.Equal("��������ŷ�ֵܴ��Ӱ", result.Name ?? "");

            result = await _doubanApi.FetchMovie("26299465");
            Assert.Equal("̽��δ֪ ��һ��", result.Name ?? "");

            result = await _doubanApi.FetchMovie("35873969");
            Assert.Equal("����̽���ϣ���������Ӱ", result.Name ?? "");
        }

        [Fact]
        public async Task TestFetchMovieCelebrities()
        {
            var list = await _doubanApi.FetchMovieCelebrities("33400537");
            Assert.NotEmpty(list);
            Assert.Equal("������һ", list[0].Name ?? "");
        }

        [Fact]
        public async Task TestFetchMovieImages()
        {
            var list = await _doubanApi.FetchMovieImages("33400537", "R");
            Assert.NotEmpty(list);
        }
    }
}
