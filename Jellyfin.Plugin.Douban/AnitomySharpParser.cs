using static AnitomySharp.Element;

namespace Jellyfin.Plugin.Douban;

internal static class AnitomySharpParser
{
    public static string? Parse(string name, ElementCategory category)
    {
        try
        {
            var element = AnitomySharp.AnitomySharp.Parse(name)?.FirstOrDefault(p => p.Category == category);
            return element?.Value;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
