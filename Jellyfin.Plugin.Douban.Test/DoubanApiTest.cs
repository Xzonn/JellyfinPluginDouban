using MediaBrowser.Controller.Providers;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.DependencyInjection;
using System;
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
            var list = _doubanApi.SearchMovie("���Ǵ���").Result;
            Assert.NotEmpty(list);
            Assert.Equal("���Ǵ���", list[0].Name ?? "");

            list = _doubanApi.SearchMovie("tt8207660").Result;
            Assert.NotEmpty(list);
            Assert.Equal("���Ǵ���", list[0].Name ?? "");

            list = _doubanApi.SearchMovie("�����Ӧ (2023)").Result;
            Assert.NotEmpty(list);
            Assert.Equal("�����Ӧ", list[0].Name ?? "");
        }

        [Fact]
        public void TestGetMovieSearchResults()
        {
            var list = _doubanApi.GetMovieSearchResults(new MovieInfo()
            {
                Name = "���Ǵ���",
            }).Result;
            Assert.NotEmpty(list);
            Assert.Equal("33400537", list[0].GetProviderId(Constants.ProviderId));

            list = _doubanApi.GetMovieSearchResults(new SeasonInfo()
            {
                Name = "���",
                IndexNumber = 1,
            }).Result;
            Assert.NotEmpty(list);
            Assert.Equal("30331432", list[0].GetProviderId(Constants.ProviderId));
        }

        [Fact]
        public void TestFetchMovie()
        {
            var result = _doubanApi.FetchMovie("30183785").Result;
            Assert.Equal("���Ǵ���", result.Name ?? "");

            result = _doubanApi.FetchMovie("33400537").Result;
            Assert.Equal("���Ǵ��� ��Ӱ��", result.Name ?? "");

            result = _doubanApi.FetchMovie("27199894").Result;
            Assert.Equal("��������ŷ�ֵܴ��Ӱ", result.Name ?? "");

            result = _doubanApi.FetchMovie("26299465").Result;
            Assert.Equal("̽��δ֪ ��һ��", result.Name ?? "");

            result = _doubanApi.FetchMovie("35873969").Result;
            Assert.Equal("����̽���ϣ���������Ӱ", result.Name ?? "");
        }

        [Fact]
        public void TestFetchMovieCelebrities()
        {
            var list = _doubanApi.FetchMovieCelebrities("33400537").Result;
            Assert.NotEmpty(list);
            Assert.Equal("������һ", list[0].Name ?? "");
        }

        [Fact]
        public void TestFetchMovieImages()
        {
            var list = _doubanApi.FetchMovieImages("33400537", "R").Result;
            Assert.NotEmpty(list);
        }
    }
}
