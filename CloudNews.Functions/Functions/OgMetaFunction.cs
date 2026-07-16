using System.Net;
using CloudNews.Functions.Data;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloudNews.Functions.Functions;

/// <summary>
/// WHY OG PREVIEW WAS NOT WORKING:
///
/// 1. React SPA is client-side — WhatsApp/Facebook crawlers do NOT run JavaScript.
///    They see a blank index.html with no OG tags → no preview.
///
/// 2. The crawler hits /news/{slug} → Azure Static Web App serves index.html → blank page.
///
/// THE FIX:
/// - Every /news/{slug} URL is ALSO served by GET /api/og?slug={slug}
/// - When WhatsApp shares your link, it calls: https://www.prajatantrkigunj.com/api/og?slug=your-slug
/// - This function returns FULL HTML with all OG meta tags already in the page
/// - WhatsApp reads those tags and shows: Title + Image + Description
///
/// HOW TO USE IN SHARE BUTTON:
/// - When sharing on WhatsApp, share the URL: https://prajatantrkigunj.com/news/slug
/// - OR share: https://prajatantrkigunj.com/api/og?slug=slug (direct to OG endpoint)
/// - WhatsApp always fetches the URL to generate preview — this function handles it
///
/// IMPORTANT FOR IMAGE:
/// - og:image must be an ABSOLUTE URL (https://...)
/// - Image must be publicly accessible (no auth)
/// - Recommended size: 1200x630px
/// - Azure Blob images work perfectly as og:image
/// </summary>
public class OgMetaFunction
{
    private readonly ApplicationDbContext    _db;
    private readonly ILogger<OgMetaFunction> _log;

    private const string SiteName   = "Prajatantr Ki Gunj";
    private const string SiteUrl    = "https://www.prajatantrkigunj.com";
    private const string SiteApiUrl = "https://prajatantrkigunj-api-g6dcasebdbc4b7bd.centralindia-01.azurewebsites.net";

    public OgMetaFunction(ApplicationDbContext db, ILogger<OgMetaFunction> log)
    {
        _db  = db;
        _log = log;
    }

    // ── GET /api/og?slug={slug} ───────────────────────────────────────────────
    // This is what WhatsApp, Facebook, Twitter crawlers will call
    [Function("OgMeta")]
    public async Task<HttpResponseData> OgMeta(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "og")]
        HttpRequestData req)
    {
        var qs   = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var slug = qs["slug"]?.Trim();

        _log.LogInformation("OG meta requested. Slug: {Slug}, UA: {UA}",
            slug,
            req.Headers.TryGetValues("User-Agent", out var ua) ? ua.FirstOrDefault() : "none");

        if (string.IsNullOrEmpty(slug))
            return await RedirectTo(req, SiteUrl);

        // ── Fetch article from DB ─────────────────────────────────────────────
        var article = await _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Author)
            .Where(a => a.Slug == slug && a.IsPublished)
            .Select(a => new
            {
                a.Title,
                a.Slug,
                a.Content,
                a.ThumbnailUrl,
                CategoryName = a.Category!.Name,
                AuthorName   = a.Author!.FullName,
                a.PublishedAt,
            })
            .FirstOrDefaultAsync();

        if (article == null)
            return await RedirectTo(req, SiteUrl);

        // ── Build meta values ─────────────────────────────────────────────────
        var articleUrl = $"{SiteUrl}/news/{article.Slug}";

        // Clean description from HTML — first 160 chars
        var description = CleanHtml(article.Content ?? "").Trim();
        if (description.Length > 160) description = description[..157] + "…";
        if (string.IsNullOrEmpty(description))
            description = $"{article.CategoryName} — {SiteName}";

        // ── IMAGE URL — most critical part ────────────────────────────────────
        // Must be absolute HTTPS URL, publicly accessible, ideally 1200x630
        var imageUrl = GetAbsoluteImageUrl(article.ThumbnailUrl);

        var fullTitle     = $"{article.Title} | {SiteName}";
        var publishedTime = article.PublishedAt.HasValue
            ? article.PublishedAt.Value.ToString("yyyy-MM-ddTHH:mm:ssZ")
            : string.Empty;

        // ── Build the HTML with ALL required OG tags ──────────────────────────
        var html = BuildOgHtml(
            articleUrl:    articleUrl,
            fullTitle:     fullTitle,
            title:         article.Title,
            description:   description,
            imageUrl:      imageUrl,
            authorName:    article.AuthorName,
            categoryName:  article.CategoryName,
            publishedTime: publishedTime
        );

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type",  "text/html; charset=utf-8");
        // Cache 10 minutes — lets crawlers cache but refreshes when article updates
        response.Headers.Add("Cache-Control", "public, max-age=600");
        // Ensure no CORS issues for crawlers
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        await response.WriteStringAsync(html);
        return response;
    }

    // ── GET /api/og/{slug} — alternate route with slug in path ───────────────
    [Function("OgMetaPath")]
    public async Task<HttpResponseData> OgMetaPath(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "og/{slug}")]
        HttpRequestData req,
        string slug)
    {
        // Reuse same logic by forwarding to query-string version
        var qs   = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var article = await _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Author)
            .Where(a => a.Slug == slug && a.IsPublished)
            .Select(a => new
            {
                a.Title, a.Slug, a.Content, a.ThumbnailUrl,
                CategoryName = a.Category!.Name,
                AuthorName   = a.Author!.FullName,
                a.PublishedAt,
            })
            .FirstOrDefaultAsync();

        if (article == null)
            return await RedirectTo(req, SiteUrl);

        var articleUrl    = $"{SiteUrl}/news/{article.Slug}";
        var description   = CleanHtml(article.Content ?? "").Trim();
        if (description.Length > 160) description = description[..157] + "…";
        if (string.IsNullOrEmpty(description))
            description = $"{article.CategoryName} — {SiteName}";

        var imageUrl      = GetAbsoluteImageUrl(article.ThumbnailUrl);
        var fullTitle     = $"{article.Title} | {SiteName}";
        var publishedTime = article.PublishedAt.HasValue
            ? article.PublishedAt.Value.ToString("yyyy-MM-ddTHH:mm:ssZ")
            : string.Empty;

        var html = BuildOgHtml(articleUrl, fullTitle, article.Title,
            description, imageUrl, article.AuthorName, article.CategoryName, publishedTime);

        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type",  "text/html; charset=utf-8");
        response.Headers.Add("Cache-Control", "public, max-age=600");
        response.Headers.Add("Access-Control-Allow-Origin", "*");
        await response.WriteStringAsync(html);
        return response;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures image URL is absolute HTTPS.
    /// Azure Blob URLs are already absolute — this just validates them.
    /// If image is missing or relative, falls back to site logo.
    /// </summary>
    private static string GetAbsoluteImageUrl(string? imageUrl)
    {
        if (!string.IsNullOrEmpty(imageUrl))
        {
            // Already absolute HTTPS — use as is
            if (imageUrl.StartsWith("https://") || imageUrl.StartsWith("http://"))
                return imageUrl;
        }
        // Fallback: use a 1200x630 default image
        // IMPORTANT: Create this file and upload it to your Azure Blob
        // or place it in your React public/ folder
        return $"{SiteUrl}/og-default.jpg";
    }

    /// <summary>
    /// Strips HTML tags and normalizes whitespace for meta description.
    /// </summary>
    private static string CleanHtml(string html)
    {
        var noTags  = System.Text.RegularExpressions.Regex.Replace(html, "<[^>]+>", " ");
        var noSpace = System.Text.RegularExpressions.Regex.Replace(noTags, @"\s+", " ");
        return noSpace.Trim();
    }

    private static string EscHtml(string? t) => string.IsNullOrEmpty(t) ? "" : t
        .Replace("&", "&amp;").Replace("<", "&lt;")
        .Replace(">", "&gt;").Replace("\"", "&quot;").Replace("'", "&#39;");

    private static async Task<HttpResponseData> RedirectTo(HttpRequestData req, string url)
    {
        var r = req.CreateResponse(HttpStatusCode.Found);
        r.Headers.Add("Location", url);
        return r;
    }

    /// <summary>
    /// Builds complete HTML page with ALL Open Graph, Twitter Card,
    /// and WhatsApp-compatible meta tags.
    /// </summary>
    private static string BuildOgHtml(
        string articleUrl, string fullTitle, string title,
        string description, string imageUrl,
        string authorName, string categoryName, string publishedTime)
    {
        return $@"<!DOCTYPE html>
<html lang=""hi"" prefix=""og: https://ogp.me/ns# article: https://ogp.me/ns/article#"">
<head>
  <meta charset=""UTF-8"">
  <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
  <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">

  <!-- ══ PRIMARY META TAGS ══════════════════════════════════════════════ -->
  <title>{EscHtml(fullTitle)}</title>
  <meta name=""title""       content=""{EscHtml(fullTitle)}"">
  <meta name=""description"" content=""{EscHtml(description)}"">
  <meta name=""author""      content=""{EscHtml(authorName)}"">
  <meta name=""keywords""    content=""{EscHtml(categoryName)}, Prajatantr Ki Gunj, Hindi News"">
  <link rel=""canonical""    href=""{articleUrl}"">

  <!-- ══ OPEN GRAPH (WhatsApp, Facebook, LinkedIn, Telegram) ═══════════ -->
  <meta property=""og:type""               content=""article"">
  <meta property=""og:url""                content=""{articleUrl}"">
  <meta property=""og:title""              content=""{EscHtml(fullTitle)}"">
  <meta property=""og:description""        content=""{EscHtml(description)}"">
  <meta property=""og:site_name""          content=""Prajatantr Ki Gunj"">
  <meta property=""og:locale""             content=""hi_IN"">

  <!-- ══ OG IMAGE — WhatsApp reads these ═══════════════════════════════ -->
  <meta property=""og:image""              content=""{imageUrl}"">
  <meta property=""og:image:secure_url""   content=""{imageUrl}"">
  <meta property=""og:image:type""         content=""image/jpeg"">
  <meta property=""og:image:width""        content=""1200"">
  <meta property=""og:image:height""       content=""630"">
  <meta property=""og:image:alt""          content=""{EscHtml(title)}"">

  <!-- ══ ARTICLE SPECIFIC ══════════════════════════════════════════════ -->
  <meta property=""article:author""        content=""{EscHtml(authorName)}"">
  <meta property=""article:section""       content=""{EscHtml(categoryName)}"">
  <meta property=""article:tag""           content=""{EscHtml(categoryName)}"">
  {(string.IsNullOrEmpty(publishedTime) ? "" : $@"<meta property=""article:published_time"" content=""{publishedTime}"">")}

  <!-- ══ TWITTER CARD ══════════════════════════════════════════════════ -->
  <meta name=""twitter:card""              content=""summary_large_image"">
  <meta name=""twitter:site""              content=""@PrajatantrGunj"">
  <meta name=""twitter:creator""           content=""@PrajatantrGunj"">
  <meta name=""twitter:url""               content=""{articleUrl}"">
  <meta name=""twitter:title""             content=""{EscHtml(fullTitle)}"">
  <meta name=""twitter:description""       content=""{EscHtml(description)}"">
  <meta name=""twitter:image""             content=""{imageUrl}"">
  <meta name=""twitter:image:alt""         content=""{EscHtml(title)}"">

  <!-- ══ WHATSAPP SPECIFIC ═════════════════════════════════════════════ -->
  <!-- WhatsApp uses og: tags. Image must be:                            -->
  <!-- - Absolute HTTPS URL                                              -->
  <!-- - Publicly accessible (no auth)                                   -->
  <!-- - At least 300x200px (ideally 1200x630)                          -->
  <!-- - JPEG, PNG, or GIF                                               -->
  <!-- - Under 8MB                                                        -->

  <!-- Redirect real browsers immediately to React SPA -->
  <meta http-equiv=""refresh"" content=""0; url={articleUrl}"">

  <style>
    body {{
      font-family: system-ui, -apple-system, sans-serif;
      display: flex; align-items: center; justify-content: center;
      min-height: 100vh; margin: 0; background: #f9fafb;
      flex-direction: column; gap: 16px; padding: 20px;
    }}
    .card {{
      max-width: 500px; background: white; border-radius: 16px;
      overflow: hidden; box-shadow: 0 4px 24px rgba(0,0,0,0.1);
    }}
    img {{ width: 100%; height: 240px; object-fit: cover; display: block; }}
    .info {{ padding: 20px; }}
    h1 {{ font-size: 18px; margin: 0 0 8px; color: #111; }}
    p  {{ font-size: 13px; color: #666; margin: 0 0 12px; line-height: 1.5; }}
    a  {{ display: inline-block; background: #dc2626; color: white;
           padding: 10px 20px; border-radius: 8px; text-decoration: none;
           font-size: 13px; font-weight: 600; }}
  </style>
</head>
<body>
  <div class=""card"">
    <img src=""{imageUrl}"" alt=""{EscHtml(title)}"" onerror=""this.style.display='none'"">
    <div class=""info"">
      <h1>{EscHtml(title)}</h1>
      <p>{EscHtml(description)}</p>
      <a href=""{articleUrl}"">पूरी खबर पढ़ें →</a>
    </div>
  </div>
  <p style=""color:#999;font-size:12px"">Redirecting to article...</p>
</body>
</html>";
    }
}