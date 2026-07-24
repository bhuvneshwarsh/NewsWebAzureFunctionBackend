using System.Net;
using System.Text.Json;
using CloudNews.Functions.Data;
using CloudNews.Functions.DTOs;
using CloudNews.Functions.Models;
using CloudNews.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloudNews.Functions.Functions;

public class ArticleFunction
{
    private readonly ApplicationDbContext    _db;
    private readonly IJwtService             _jwt;
    private readonly ILogger<ArticleFunction> _log;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy  = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true };

    public ArticleFunction(ApplicationDbContext db, IJwtService jwt,
        ILogger<ArticleFunction> log)
    {
        _db  = db;
        _jwt = jwt;
        _log = log;
    }

    // ── GET /api/articles ─────────────────────────────────────────────────────
    // Public: only published + approved (or NotRequired)
    // Employee: only THEIR OWN articles (all statuses)
    // Admin/SuperAdmin: all articles
    [Function("GetArticles")]
    public async Task<HttpResponseData> GetArticles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles")]
        HttpRequestData req)
    {
        var qs        = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var page      = int.TryParse(qs["page"], out var p) ? Math.Max(1, p) : 1;
        var size      = int.TryParse(qs["size"], out var s) ? Math.Clamp(s, 1, 50) : 10;
        var category  = qs["category"];
        var showAll   = qs["all"] == "true";

        var principal = AuthHelper.GetPrincipal(req, _jwt);
        var isAdmin   = AuthHelper.HasRole(principal, "SuperAdmin", "Admin", "Reporter");
        var isEmp     = AuthHelper.HasRole(principal, "Employee");
        var userId    = AuthHelper.GetUserId(principal);

        var query = _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Author)
            .AsQueryable();

        if (isAdmin && showAll)
        {
            // Admin: see everything
        }
        else if (isEmp && userId.HasValue)
        {
            // ── FIX 1: Employee sees ONLY their own articles ──────────────────
            query = query.Where(a => a.AuthorId == userId.Value);
        }
        else
        {
            // Public: only published articles that are approved or not-required
            query = query.Where(a => a.IsPublished
                && (a.ApprovalStatus == ApprovalStatus.NotRequired
                 || a.ApprovalStatus == ApprovalStatus.Approved));
        }

        if (!string.IsNullOrEmpty(category))
            query = query.Where(a => a.Category!.Slug == category);

        var total    = await query.CountAsync();
        var articles = await query
            .OrderByDescending(a => a.PublishedAt ?? a.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(a => new ArticleListItem
            {
                Id             = a.Id,
                Title          = a.Title,
                Slug           = a.Slug,
                ThumbnailUrl   = a.ThumbnailUrl,
                CategoryName   = a.Category!.Name,
                CategoryId     = a.CategoryId,
                AuthorName     = a.Author!.FullName,
                IsPublished    = a.IsPublished,
                ApprovalStatus = a.ApprovalStatus,
                ApprovalNote   = a.ApprovalNote,
                Views          = a.Views,
                PublishedAt    = a.PublishedAt,
                CreatedAt      = a.CreatedAt,
            })
            .ToListAsync();

        return await OkJson(req, ApiResponse<PaginatedResult<ArticleListItem>>.Ok(
            new PaginatedResult<ArticleListItem>
            {
                Items      = articles,
                Page       = page,
                PageSize   = size,
                TotalCount = total,
            }));
    }

    // ── GET /api/articles/{slug} ──────────────────────────────────────────────
    [Function("GetArticleBySlug")]
    public async Task<HttpResponseData> GetArticleBySlug(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles/{slug}")]
        HttpRequestData req, string slug)
    {
        var article = await _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Author)
            .FirstOrDefaultAsync(a => a.Slug == slug
                && a.IsPublished
                && (a.ApprovalStatus == ApprovalStatus.NotRequired
                 || a.ApprovalStatus == ApprovalStatus.Approved));

        if (article == null)
            return await Fail(req, HttpStatusCode.NotFound, "Article not found.");

        return await OkJson(req, ApiResponse<ArticleDetail>.Ok(new ArticleDetail
        {
            Id             = article.Id,
            Title          = article.Title,
            Slug           = article.Slug,
            Content        = article.Content,
            ThumbnailUrl   = article.ThumbnailUrl,
            CategoryId     = article.CategoryId,
            CategoryName   = article.Category!.Name,
            AuthorId       = article.AuthorId,
            AuthorName     = article.Author!.FullName,
            IsPublished    = article.IsPublished,
            ApprovalStatus = article.ApprovalStatus,
            Views          = article.Views,
            PublishedAt    = article.PublishedAt,
            CreatedAt      = article.CreatedAt,
        }));
    }

    // ── POST /api/articles/{slug}/view ────────────────────────────────────────
    [Function("TrackArticleView")]
    public async Task<HttpResponseData> TrackArticleView(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "articles/{slug}/view")] HttpRequestData req, string slug)
    {
        try
        {
            await _db.Articles
                .Where(a => a.Slug == slug && a.IsPublished
                    && (a.ApprovalStatus == ApprovalStatus.NotRequired
                     || a.ApprovalStatus == ApprovalStatus.Approved))
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(a => a.Views, a => a.Views + 1));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "View tracking failed for {Slug}", slug);
        }
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    // ── POST /api/articles ────────────────────────────────────────────────────
    // Employee articles → ApprovalStatus = Pending, IsPublished = false
    // Admin/Reporter articles → ApprovalStatus = NotRequired, IsPublished per request
    [Function("CreateArticle")]
    public async Task<HttpResponseData> CreateArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "articles")]
        HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin", "Reporter", "Employee"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Login required.");

        var authorId = AuthHelper.GetUserId(principal);
        if (authorId == null)
            return await Fail(req, HttpStatusCode.Unauthorized, "Invalid token.");

        var body = await req.ReadAsStringAsync();
        var dto  = JsonSerializer.Deserialize<CreateArticleRequest>(body ?? "", JsonOpts);

        if (dto == null || string.IsNullOrWhiteSpace(dto.Title))
            return await Fail(req, HttpStatusCode.BadRequest, "Title is required.");
        if (string.IsNullOrWhiteSpace(dto.Content))
            return await Fail(req, HttpStatusCode.BadRequest, "Content is required.");
        if (!await _db.Categories.AnyAsync(c => c.Id == dto.CategoryId))
            return await Fail(req, HttpStatusCode.BadRequest, "Invalid category.");

        var isEmployee = AuthHelper.HasRole(principal, "Employee");

        // ── FIX 2: Employee articles always go to Pending for approval ────────
        var approvalStatus = isEmployee
            ? ApprovalStatus.Pending
            : ApprovalStatus.NotRequired;

        // Employee articles are NEVER published immediately — must be approved first
        var isPublished = !isEmployee && dto.Publish;

        var article = new Article
        {
            Title          = dto.Title.Trim(),
            Slug           = SlugService.Generate(dto.Title),
            Content        = dto.Content,
            ThumbnailUrl   = dto.ThumbnailUrl,
            CategoryId     = dto.CategoryId,
            AuthorId       = authorId.Value,
            IsPublished    = isPublished,
            PublishedAt    = isPublished ? DateTime.UtcNow : null,
            ApprovalStatus = approvalStatus,
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow,
        };

        _db.Articles.Add(article);
        await _db.SaveChangesAsync();

        var message = isEmployee
            ? "Article submitted for Super Admin approval. It will go live once approved."
            : "Article created successfully.";

        _log.LogInformation("Article created: {Id} by {Role} — Status: {Status}",
            article.Id, isEmployee ? "Employee" : "Admin", approvalStatus);

        return await OkJson(req,
            ApiResponse<object>.Ok(new { article.Id, article.Slug, approvalStatus }, message),
            HttpStatusCode.Created);
    }

    // ── PUT /api/articles/{id} ────────────────────────────────────────────────
    [Function("UpdateArticle")]
    public async Task<HttpResponseData> UpdateArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put",
            Route = "articles/{id:int}")] HttpRequestData req, int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin", "Reporter", "Employee"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Login required.");

        var userId     = AuthHelper.GetUserId(principal);
        var isEmployee = AuthHelper.HasRole(principal, "Employee");
        var isAdmin    = AuthHelper.HasRole(principal, "SuperAdmin", "Admin");

        var article = await _db.Articles.FindAsync(id);
        if (article == null)
            return await Fail(req, HttpStatusCode.NotFound, "Article not found.");

        // Employee can only edit their own articles
        if (isEmployee && article.AuthorId != userId)
            return await Fail(req, HttpStatusCode.Forbidden,
                "You can only edit your own articles.");

        var body = await req.ReadAsStringAsync();
        var dto  = JsonSerializer.Deserialize<UpdateArticleRequest>(body ?? "", JsonOpts);
        if (dto == null)
            return await Fail(req, HttpStatusCode.BadRequest, "Invalid request.");

        if (dto.Title        != null) article.Title        = dto.Title.Trim();
        if (dto.Content      != null) article.Content      = dto.Content;
        if (dto.ThumbnailUrl != null) article.ThumbnailUrl = dto.ThumbnailUrl;
        if (dto.CategoryId   != null) article.CategoryId   = dto.CategoryId.Value;
        article.UpdatedAt = DateTime.UtcNow;

        if (isEmployee)
        {
            // If employee edits a rejected article and resubmits → back to Pending
            if (article.ApprovalStatus == ApprovalStatus.Rejected)
            {
                article.ApprovalStatus = ApprovalStatus.Pending;
                article.ApprovalNote   = null;
                article.IsPublished    = false;
            }
        }
        else if (isAdmin)
        {
            // Admin can publish/unpublish freely
            if (dto.Publish == true && !article.IsPublished)
            {
                article.IsPublished = true;
                article.PublishedAt = DateTime.UtcNow;
            }
            else if (dto.Publish == false)
            {
                article.IsPublished = false;
            }
        }

        await _db.SaveChangesAsync();
        return await OkJson(req, ApiResponse<object>.Ok(
            new { article.Id, article.Slug }, "Article updated."));
    }

    // ── GET /api/articles/pending  [SuperAdmin] ───────────────────────────────
    // Returns all articles awaiting approval
    [Function("GetPendingArticles")]
    public async Task<HttpResponseData> GetPendingArticles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "articles/pending")] HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Admin role required.");

        var pending = await _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Author)
            .Where(a => a.ApprovalStatus == ApprovalStatus.Pending)
            .OrderBy(a => a.CreatedAt)   // oldest first — first in, first reviewed
            .Select(a => new ArticleListItem
            {
                Id             = a.Id,
                Title          = a.Title,
                Slug           = a.Slug,
                ThumbnailUrl   = a.ThumbnailUrl,
                CategoryName   = a.Category!.Name,
                CategoryId     = a.CategoryId,
                AuthorName     = a.Author!.FullName,
                IsPublished    = a.IsPublished,
                ApprovalStatus = a.ApprovalStatus,
                ApprovalNote   = a.ApprovalNote,
                Views          = a.Views,
                PublishedAt    = a.PublishedAt,
                CreatedAt      = a.CreatedAt,
            })
            .ToListAsync();

        return await OkJson(req, ApiResponse<List<ArticleListItem>>.Ok(pending));
    }

    // ── GET /api/articles/{id}/preview  [SuperAdmin] ──────────────────────────
    // Full article preview for approval review
    [Function("PreviewArticle")]
    public async Task<HttpResponseData> PreviewArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "articles/{id:int}/preview")] HttpRequestData req, int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Admin role required.");

        var article = await _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Author)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (article == null)
            return await Fail(req, HttpStatusCode.NotFound, "Article not found.");

        return await OkJson(req, ApiResponse<ArticleDetail>.Ok(new ArticleDetail
        {
            Id             = article.Id,
            Title          = article.Title,
            Slug           = article.Slug,
            Content        = article.Content,
            ThumbnailUrl   = article.ThumbnailUrl,
            CategoryId     = article.CategoryId,
            CategoryName   = article.Category!.Name,
            AuthorId       = article.AuthorId,
            AuthorName     = article.Author!.FullName,
            IsPublished    = article.IsPublished,
            ApprovalStatus = article.ApprovalStatus,
            ApprovalNote   = article.ApprovalNote,
            Views          = article.Views,
            PublishedAt    = article.PublishedAt,
            CreatedAt      = article.CreatedAt,
        }));
    }

    // ── POST /api/articles/{id}/approve  [SuperAdmin] ─────────────────────────
    [Function("ApproveArticle")]
    public async Task<HttpResponseData> ApproveArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "articles/{id:int}/approve")] HttpRequestData req, int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Admin role required.");

        var adminId = AuthHelper.GetUserId(principal);
        var article = await _db.Articles.FindAsync(id);

        if (article == null)
            return await Fail(req, HttpStatusCode.NotFound, "Article not found.");
        if (article.ApprovalStatus != ApprovalStatus.Pending)
            return await Fail(req, HttpStatusCode.BadRequest,
                $"Article is not pending approval (current status: {article.ApprovalStatus}).");

        // Approve → publish immediately
        article.ApprovalStatus = ApprovalStatus.Approved;
        article.ApprovalNote   = null;
        article.ApprovedById   = adminId;
        article.ApprovedAt     = DateTime.UtcNow;
        article.IsPublished    = true;
        article.PublishedAt    = DateTime.UtcNow;
        article.UpdatedAt      = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _log.LogInformation("Article {Id} approved by admin {AdminId}", id, adminId);
        return await OkJson(req, ApiResponse<object>.Ok(
            new { id, status = "Approved" },
            "Article approved and published successfully."));
    }

    // ── POST /api/articles/{id}/reject  [SuperAdmin] ──────────────────────────
    [Function("RejectArticle")]
    public async Task<HttpResponseData> RejectArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "articles/{id:int}/reject")] HttpRequestData req, int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Admin role required.");

        var adminId = AuthHelper.GetUserId(principal);
        var article = await _db.Articles.FindAsync(id);

        if (article == null)
            return await Fail(req, HttpStatusCode.NotFound, "Article not found.");
        if (article.ApprovalStatus != ApprovalStatus.Pending)
            return await Fail(req, HttpStatusCode.BadRequest,
                "Article is not pending approval.");

        // Read rejection note
        var body = await req.ReadAsStringAsync();
        using var doc  = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var note = doc.RootElement.TryGetProperty("note", out var noteProp)
            ? noteProp.GetString()?.Trim()
            : null;

        article.ApprovalStatus = ApprovalStatus.Rejected;
        article.ApprovalNote   = string.IsNullOrEmpty(note)
            ? "Your article needs revision. Please contact your editor for details."
            : note;
        article.ApprovedById   = adminId;
        article.ApprovedAt     = DateTime.UtcNow;
        article.IsPublished    = false;
        article.UpdatedAt      = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _log.LogInformation("Article {Id} rejected by admin {AdminId}. Note: {Note}",
            id, adminId, article.ApprovalNote);
        return await OkJson(req, ApiResponse<object>.Ok(
            new { id, status = "Rejected" },
            "Article rejected. Employee has been notified."));
    }

    // ── DELETE /api/articles/{id}  [SuperAdmin] ───────────────────────────────
    [Function("DeleteArticle")]
    public async Task<HttpResponseData> DeleteArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete",
            Route = "articles/{id:int}")] HttpRequestData req, int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin role required.");

        var article = await _db.Articles.FindAsync(id);
        if (article == null)
            return await Fail(req, HttpStatusCode.NotFound, "Article not found.");

        _db.Articles.Remove(article);
        await _db.SaveChangesAsync();
        return await OkJson(req, ApiResponse<object>.Ok(new { id }, "Article deleted."));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static async Task<HttpResponseData> OkJson(HttpRequestData req,
        object data, HttpStatusCode code = HttpStatusCode.OK)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(data, JsonOpts));
        return res;
    }

    private static async Task<HttpResponseData> Fail(HttpRequestData req,
        HttpStatusCode code, string msg)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(
            ApiResponse<object>.Fail(msg), JsonOpts));
        return res;
    }
}
