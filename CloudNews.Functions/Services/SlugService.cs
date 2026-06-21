using System.Text;
using System.Text.RegularExpressions;

namespace CloudNews.Functions.Services;

public static class SlugService
{
    public static string Generate(string title)
    {
        var slug = title.ToLowerInvariant().Trim();

        // Replace spaces and special chars with hyphens
        slug = Regex.Replace(slug, @"[^a-z0-9\s-]", "");
        slug = Regex.Replace(slug, @"\s+", "-");
        slug = Regex.Replace(slug, @"-+", "-");
        slug = slug.Trim('-');

        // Append timestamp to ensure uniqueness
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        return $"{slug}-{timestamp}";
    }
}