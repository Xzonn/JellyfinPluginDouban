using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.Douban.Controller;

[ApiController]
[Route("Plugins/Douban")]
public class ApiController : ControllerBase
{
    private readonly HttpClient _httpClient;
    public HttpClient GetHttpClient() => _httpClient;
    private static readonly string[] REQUEST_HEADER_KEYS = [
            "Accept",
            "Accept-Encoding",
            "Accept-Language",
            "Cache-Control",
            "Connection",
            "Pragma",
            "User-Agent",
        ];

    private readonly ILogger<ApiController> _log;

    public ApiController(IHttpClientFactory httpClientFactory, ILogger<ApiController> log)
    {
        _httpClient = httpClientFactory.CreateClient();
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Referer", "https://movie.douban.com/");
        _httpClient.DefaultRequestHeaders.Add("Accept-Language", "zh-CN,zh;q=0.9,en;q=0.8");
        _httpClient.Timeout = TimeSpan.FromSeconds(5);
        _log = log;
    }

    [Route("Image")]
    [AllowAnonymous]
    [HttpGet]
    public async Task<Stream?> GetImage(string url, CancellationToken token)
    {
        if (string.IsNullOrEmpty(url)) { return null; }

        HttpResponseMessage response;
        var message = new HttpRequestMessage(HttpMethod.Get, $"{Helper.DEFAULT_CDN_SERVER}{url}");
        foreach (var key in REQUEST_HEADER_KEYS)
        {
            if (Request.Headers.TryGetValue(key, out var value))
            {
                message.Headers.Add(key, value.ToString());
            }
        }

        _log.LogDebug("Getting image url: {url}", url);
        response = await _httpClient.SendAsync(message, token).ConfigureAwait(false);

        Response.StatusCode = (int)response.StatusCode;
#if NET8_0_OR_GREATER
        Response.ContentType = response.Content.Headers.ContentType?.ToString();
#else
        if (response.Content.Headers.ContentType is not null) { Response.ContentType = response.Content.Headers.ContentType.ToString(); }
#endif
        Response.ContentLength = response.Content.Headers.ContentLength;

        foreach (var header in response.Headers)
        {
            var key = header.Key;
            foreach (var value in header.Value)
            {
                if (key.StartsWith("X-") || key == "Via") { continue; }
                Response.Headers.Append(key, value);
            }
        }

        return await response.Content.ReadAsStreamAsync(token);
    }
}
