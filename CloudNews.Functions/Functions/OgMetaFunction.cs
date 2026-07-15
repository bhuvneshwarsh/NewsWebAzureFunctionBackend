using System.Net;
using CloudNews.Functions.Data;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloudNews.Functions.Functions;

/// <summary>
/// This function intercepts requests from social media crawlers
/// (WhatsApp, Facebook, Twitter, Telegram) to /news/{slug}
/// and returns a minimal HTML page with proper Open Graph meta tags.
///
/// Regular browsers get redirected to the React SPA.
/// Crawlers/bots get the pre-rendered HTML with title + image + description.
///
/// HOW IT WORKS:
/// - Route: /api/og/{slug}
/// - staticwebapp.config.json redirects /news/{slug} requests from bots → /api/og/{slug}
/// - The Azure Function queries the DB and returns HTML with OG tags
/// - WhatsApp/Facebook reads these OG tags and shows rich preview
/// </summary>
public class OgMetaFunction
{
    private readonly ApplicationDbContext   _db;
    private readonly ILogger<OgMetaFunction> _log;

    private const string SiteName   = "Prajatantr Ki Gunj";
    private const string SiteUrl    = "https://www.prajatantrkigunj.com";
    // Default fallback image — 1200x630px, must be publicly accessible
    private const string DefaultImg = "https://www.prajatantrkigunj.com/og-default.jpg";

    public OgMetaFunction(ApplicationDbContext db, ILogger<OgMetaFunction> log)
    {
        _db  = db;
        _log = log;
    }

    // ── GET /api/og/{slug} ────────────────────────────────────────────────────
    [Function("OgMeta")]
    public async Task<HttpResponseData> OgMeta(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "og/{slug}")]
        HttpRequestData req,
        string slug)
    {
        _log.LogInformation("OG meta requested for slug: {Slug}", slug);

        var article = await _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Author)
            .Where(a => a.Slug == slug && a.IsPublished)
            .Select(a => new {
                a.Title,
                a.Slug,
                a.Content,
                a.ThumbnailUrl,
                CategoryName = a.Category!.Name,
                AuthorName   = a.Author!.FullName,
                a.PublishedAt,
            })
            .FirstOrDefaultAsync();

        // If article not found → redirect to homepage
        if (article == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.Found);
            notFound.Headers.Add("Location", SiteUrl);
            return notFound;
        }

        // Clean description — strip HTML tags, limit 160 chars
        var rawDesc = System.Text.RegularExpressions.Regex
            .Replace(article.Content ?? "", "<[^>]+>", " ")
            .Replace("  ", " ").Trim();
        var description = rawDesc.Length > 160
            ? rawDesc[..157] + "…"
            : rawDesc;

        var imageUrl  = !string.IsNullOrEmpty(article.ThumbnailUrl)
            ? article.ThumbnailUrl
            : DefaultImg;

        var articleUrl = $"{SiteUrl}/news/{article.Slug}";
        var fullTitle  = $"{article.Title} | {SiteName}";

        // Check User-Agent — if real browser, redirect to React SPA
        req.Headers.TryGetValues("User-Agent", out var uaValues);
        var userAgent = uaValues?.FirstOrDefault() ?? "";
        var isCrawler = IsCrawler(userAgent);

        if (!isCrawler)
        {
            // Real browser — let React SPA handle it
            var redirect = req.CreateResponse(HttpStatusCode.Found);
            redirect.Headers.Add("Location", articleUrl);
            redirect.Headers.Add("Cache-Control", "no-cache");
            return redirect;
        }

        // ── Bot/Crawler — serve pre-rendered HTML with OG tags ────────────────
        var publishedDate = article.PublishedAt?.ToString("yyyy-MM-ddTHH:mm:ssZ") ?? "";

        var html = $@"<!DOCTYPE html>
<html lang=""hi"">
<head>
  <meta charset=""UTF-8"" />
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"" />

  <!-- Primary Meta -->
  <title>{EscapeHtml(fullTitle)}</title>
  <meta name=""description"" content=""{EscapeHtml(description)}"" />
  <meta name=""author""      content=""{EscapeHtml(article.AuthorName)}"" />
  <link rel=""canonical""    href=""{articleUrl}"" />

  <!-- Open Graph (WhatsApp, Facebook, LinkedIn, Telegram) -->
  <meta property=""og:type""               content=""article"" />
  <meta property=""og:url""                content=""{articleUrl}"" />
  <meta property=""og:title""              content=""{EscapeHtml(fullTitle)}"" />
  <meta property=""og:description""        content=""{EscapeHtml(description)}"" />
  <meta property=""og:image""              content=""{imageUrl}"" />
  <meta property=""og:image:width""        content=""1200"" />
  <meta property=""og:image:height""       content=""630"" />
  <meta property=""og:image:alt""          content=""{EscapeHtml(article.Title)}"" />
  <meta property=""og:site_name""          content=""{SiteName}"" />
  <meta property=""og:locale""             content=""hi_IN"" />

  <!-- Article specific -->
  <meta property=""article:author""        content=""{EscapeHtml(article.AuthorName)}"" />
  <meta property=""article:section""       content=""{EscapeHtml(article.CategoryName)}"" />
  {(string.IsNullOrEmpty(publishedDate) ? "" : $@"<meta property=""article:published_time"" content=""{publishedDate}"" />")}

  <!-- Twitter Card -->
  <meta name=""twitter:card""              content=""summary_large_image"" />
  <meta name=""twitter:title""             content=""{EscapeHtml(fullTitle)}"" />
  <meta name=""twitter:description""       content=""{EscapeHtml(description)}"" />
  <meta name=""twitter:image""             content=""{imageUrl}"" />
  <meta name=""twitter:site""              content=""@PrajatantrGunj"" />

  <!-- Redirect browsers to React SPA immediately -->
  <meta http-equiv=""refresh"" content=""0;url={articleUrl}"" />
</head>
<body>
  <p>Redirecting to <a href=""{articleUrl}"">{EscapeHtml(article.Title)}</a>...</p>
</body>
</html>";

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "text/html; charset=utf-8");
        // Cache for 1 hour — crawlers don't need fresh data every second
        response.Headers.Add("Cache-Control", "public, max-age=3600");
        await response.WriteStringAsync(html);
        return response;
    }

    // ── Detect social media crawlers by User-Agent ────────────────────────────
    private static bool IsCrawler(string ua)
    {
        if (string.IsNullOrEmpty(ua)) return false;
        var lower = ua.ToLowerInvariant();
        return lower.Contains("whatsapp")
            || lower.Contains("facebookexternalhit")
            || lower.Contains("twitterbot")
            || lower.Contains("telegrambot")
            || lower.Contains("linkedinbot")
            || lower.Contains("slackbot")
            || lower.Contains("discordbot")
            || lower.Contains("googlebot")
            || lower.Contains("bingbot")
            || lower.Contains("applebot")
            || lower.Contains("pinterest")
            || lower.Contains("instagram")
            || lower.Contains("sharechat")
            || lower.Contains("koo")
            || lower.Contains("bot")
            || lower.Contains("crawler")
            || lower.Contains("spider");
    }

    private static string EscapeHtml(string? text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        return text
            .Replace("&",  "&amp;")
            .Replace("<",  "&lt;")
            .Replace(">",  "&gt;")
            .Replace("\"", "&quot;")
            .Replace("'",  "&#39;");
    }
}
