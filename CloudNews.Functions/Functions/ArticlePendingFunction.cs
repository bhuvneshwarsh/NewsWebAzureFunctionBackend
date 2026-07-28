using System.Net;
using System.Text.Json;
using CloudNews.Functions.Data;
using CloudNews.Functions.DTOs;
using CloudNews.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloudNews.Functions.Functions;

/// <summary>
/// STANDALONE FIX — Add this as a NEW file in your Functions folder.
/// Do NOT modify ArticleFunction.cs at all.
///
/// ROOT CAUSE:
/// GET /api/articles/pending was being matched by Azure Functions as
/// Route = "articles/{slug}" with slug = "pending"
/// So the pending articles function was NEVER called.
///
/// FIX:
/// New dedicated route: GET /api/articles-pending
/// (hyphen instead of slash — completely avoids the route conflict)
/// Frontend ArticleApprovals.tsx calls /articles-pending instead of /articles/pending
/// </summary>
public class ArticlePendingFunction
{
    private readonly ApplicationDbContext          _db;
    private readonly IJwtService                   _jwt;
    private readonly ILogger<ArticlePendingFunction> _log;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true };

    public ArticlePendingFunction(ApplicationDbContext db, IJwtService jwt,
        ILogger<ArticlePendingFunction> log)
    {
        _db  = db;
        _jwt = jwt;
        _log = log;
    }

    // ── GET /api/articles-pending  [SuperAdmin / Admin] ───────────────────────
    // New unambiguous route — no conflict with articles/{slug}
    [Function("GetPendingArticles")]
    public async Task<HttpResponseData> GetPendingArticles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "articles-pending")] HttpRequestData req)
    {
        // Auth check
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin"))
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            unauth.Headers.Add("Content-Type", "application/json");
            await unauth.WriteStringAsync(JsonSerializer.Serialize(
                new { success = false, message = "Admin role required." }, JsonOpts));
            return unauth;
        }

        // Fetch all articles with ApprovalStatus = 'Pending'
        var pending = await _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Author)
            .Where(a => a.ApprovalStatus == "Pending")
            .OrderBy(a => a.CreatedAt)   // oldest first — review in order submitted
            .Select(a => new
            {
                id             = a.Id,
                title          = a.Title,
                slug           = a.Slug,
                thumbnailUrl   = a.ThumbnailUrl,
                categoryName   = a.Category != null ? a.Category.Name : "",
                categoryId     = a.CategoryId,
                authorName     = a.Author != null ? a.Author.FullName : "",
                isPublished    = a.IsPublished,
                approvalStatus = a.ApprovalStatus,
                approvalNote   = a.ApprovalNote,
                views          = a.Views,
                publishedAt    = a.PublishedAt,
                createdAt      = a.CreatedAt,
            })
            .ToListAsync();

        _log.LogInformation("Pending articles fetched via /articles-pending: {Count}",
            pending.Count);

        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(
            new { success = true, message = "OK", data = pending }, JsonOpts));
        return res;
    }

    // ── GET /api/articles-preview/{id}  [SuperAdmin / Admin / Employee] ───────
    // Also fixes preview route conflict — same issue as pending
    [Function("PreviewArticleById")]
    public async Task<HttpResponseData> PreviewArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "articles-preview/{id:int}")] HttpRequestData req,
        int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin", "Employee"))
        {
            var unauth = req.CreateResponse(HttpStatusCode.Unauthorized);
            unauth.Headers.Add("Content-Type", "application/json");
            await unauth.WriteStringAsync(JsonSerializer.Serialize(
                new { success = false, message = "Login required." }, JsonOpts));
            return unauth;
        }

        var isEmp  = AuthHelper.HasRole(principal, "Employee");
        var userId = AuthHelper.GetUserId(principal);

        var article = await _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Author)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (article == null)
        {
            var nf = req.CreateResponse(HttpStatusCode.NotFound);
            nf.Headers.Add("Content-Type", "application/json");
            await nf.WriteStringAsync(JsonSerializer.Serialize(
                new { success = false, message = "Article not found." }, JsonOpts));
            return nf;
        }

        // Employee can only preview their own articles
        if (isEmp && article.AuthorId != userId)
        {
            var forbidden = req.CreateResponse(HttpStatusCode.Forbidden);
            forbidden.Headers.Add("Content-Type", "application/json");
            await forbidden.WriteStringAsync(JsonSerializer.Serialize(
                new { success = false, message = "You can only view your own articles." },
                JsonOpts));
            return forbidden;
        }

        var data = new
        {
            id             = article.Id,
            title          = article.Title,
            slug           = article.Slug,
            content        = article.Content,
            thumbnailUrl   = article.ThumbnailUrl,
            categoryId     = article.CategoryId,
            categoryName   = article.Category?.Name ?? "",
            authorId       = article.AuthorId,
            authorName     = article.Author?.FullName ?? "",
            isPublished    = article.IsPublished,
            approvalStatus = article.ApprovalStatus,
            approvalNote   = article.ApprovalNote,
            views          = article.Views,
            publishedAt    = article.PublishedAt,
            createdAt      = article.CreatedAt,
        };

        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(
            new { success = true, message = "OK", data }, JsonOpts));
        return res;
    }
}
