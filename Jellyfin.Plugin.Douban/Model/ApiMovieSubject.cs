namespace Jellyfin.Plugin.Douban.Model;

public class ApiMovieSubject
{
    public string? Type { get; set; }
    public string? Name { get; set; }
    public string? OriginalName { get; set; }
    public decimal Rating { get; set; }
    public string? PosterId { get; set; }
    public string? Sid { get; set; }
    public int Year { get; set; }
    public string[]? Genre { get; set; }
    public string? Website { get; set; }
    public string[]? Country { get; set; }
    public DateTime? ScreenTime { get; set; }
    public string? ImdbId { get; set; }
    public string? Intro { get; set; }
    public int SeasonIndex { get; set; }
}
