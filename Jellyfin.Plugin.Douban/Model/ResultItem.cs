namespace Jellyfin.Plugin.Douban.Model;

struct ResultItem
{
#pragma warning disable IDE1006 // 命名样式
    public string img { get; set; }
    public string title { get; set; }
    public string url { get; set; }
    public string sub_title { get; set; }
    public string type { get; set; }
    public string id { get; set; }
#pragma warning restore IDE1006 // 命名样式
}
