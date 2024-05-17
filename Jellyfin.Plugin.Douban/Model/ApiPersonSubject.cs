namespace Jellyfin.Plugin.Douban.Model;

public class ApiPersonSubject
{
    public string? Name { get; set; }
    public string? OriginalName { get; set; }
    public string? PosterUrl { get; set; }
    public string? CelebrityId { get; set; }
    public string? PersonageId { get; set; }
    public string? Gender { get; set; }
    public DateTime? Birthdate { get; set; }
    public DateTime? Deathdate { get; set; }
    public string[]? Birthplace { get; set; }
    public string? Website { get; set; }
    public string? ImdbId { get; set; }
    public string? Intro { get; set; }
}
