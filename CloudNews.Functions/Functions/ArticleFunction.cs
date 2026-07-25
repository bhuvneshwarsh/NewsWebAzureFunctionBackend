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
    private readonly ApplicationDbContext     _db;
    private readonly IJwtService              _jwt;
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
    //
    // BUG 1 FIX: Public homepage was showing employee pending articles
    // because the employee JWT was being sent with every request via the
    // axios interceptor. The fix: public route NEVER uses the JWT to change
    // what articles are shown. Only ?all=true with Admin/Reporter shows drafts.
    // Employee with ?mine=true shows only their own articles.
    //
    [Function("GetArticles")]
    public async Task<HttpResponseData> GetArticles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles")]
        HttpRequestData req)
    {
        var qs       = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var page     = int.TryParse(qs["page"], out var p) ? Math.Max(1, p) : 1;
        var size     = int.TryParse(qs["size"], out var s) ? Math.Clamp(s, 1, 50) : 10;
        var category = qs["category"];
        var showAll  = qs["all"]  == "true";
        var mineOnly = qs["mine"] == "true";  // ← NEW: employee-specific param

        var principal = AuthHelper.GetPrincipal(req, _jwt);
        var isAdmin   = AuthHelper.HasRole(principal, "SuperAdmin", "Admin", "Reporter");
        var isEmp     = AuthHelper.HasRole(principal, "Employee");
        var userId    = AuthHelper.GetUserId(principal);

        var query = _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Author)
            .AsQueryable();

        if (mineOnly && isEmp && userId.HasValue)
        {
            // ── Employee "My Articles" — their own only, all statuses ─────────
            query = query.Where(a => a.AuthorId == userId.Value);
        }
        else if (showAll && isAdmin)
        {
            // ── Admin "all articles" — everything including drafts ─────────────
            // No extra filter
        }
        else
        {
            // ── PUBLIC homepage / category page ───────────────────────────────
            // FIX: ALWAYS show only published + approved articles
            // NEVER change this based on who is logged in
            // This is what visitors, employees on other tabs, everyone sees
            query = query.Where(a =>
                a.IsPublished &&
                (a.ApprovalStatus == "NotRequired" || a.ApprovalStatus == "Approved"));
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
                // TotalPages = (int)Math.Ceiling((double)total / size),
            }));
    }

    // ── GET /api/articles/slug/{slug} ─────────────────────────────────────────
    // NOTE: Changed route from "articles/{slug}" to "articles/slug/{slug}"
    // to avoid conflict with "articles/{id:int}/preview" etc.
    // Public — only published + approved
    [Function("GetArticleBySlug")]
    public async Task<HttpResponseData> GetArticleBySlug(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "articles/slug/{slug}")] HttpRequestData req,
        string slug)
    {
        var article = await _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Author)
            .FirstOrDefaultAsync(a =>
                a.Slug == slug &&
                a.IsPublished &&
                (a.ApprovalStatus == "NotRequired" || a.ApprovalStatus == "Approved"));

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

    // ── POST /api/articles/slug/{slug}/view ───────────────────────────────────
    [Function("TrackArticleView")]
    public async Task<HttpResponseData> TrackArticleView(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "articles/slug/{slug}/view")] HttpRequestData req,
        string slug)
    {
        try
        {
            await _db.Articles
                .Where(a => a.Slug == slug && a.IsPublished &&
                    (a.ApprovalStatus == "NotRequired" || a.ApprovalStatus == "Approved"))
                .ExecuteUpdateAsync(s =>
                    s.SetProperty(a => a.Views, a => a.Views + 1));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "View tracking failed for {Slug}", slug);
        }
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    // ── GET /api/articles/pending  [SuperAdmin/Admin] ─────────────────────────
    //
    // BUG 2 FIX: Previously this route conflicted with "articles/{slug}"
    // because Azure Functions matched "pending" as a slug value.
    // FIX: Moved slug routes to "articles/slug/{slug}" — now "articles/pending"
    // is unambiguously its own route and will always hit this function.
    //
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
            .Where(a => a.ApprovalStatus == "Pending")
            .OrderBy(a => a.CreatedAt)   // oldest first — first submitted, first reviewed
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

        _log.LogInformation("Pending articles fetched: {Count}", pending.Count);
        return await OkJson(req, ApiResponse<List<ArticleListItem>>.Ok(pending));
    }

    // ── GET /api/articles/{id}/preview  [SuperAdmin/Admin] ────────────────────
    [Function("PreviewArticle")]
    public async Task<HttpResponseData> PreviewArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get",
            Route = "articles/{id:int}/preview")] HttpRequestData req,
        int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin", "Employee"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Login required.");

        var isEmp  = AuthHelper.HasRole(principal, "Employee");
        var userId = AuthHelper.GetUserId(principal);

        var article = await _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Author)
            .FirstOrDefaultAsync(a => a.Id == id);

        if (article == null)
            return await Fail(req, HttpStatusCode.NotFound, "Article not found.");

        // Employee can only preview their own articles
        if (isEmp && article.AuthorId != userId)
            return await Fail(req, HttpStatusCode.Forbidden,
                "You can only view your own articles.");

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

    // ── POST /api/articles ────────────────────────────────────────────────────
    [Function("CreateArticle")]
    public async Task<HttpResponseData> CreateArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "articles")]
        HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin", "Reporter", "Employee"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Login required.");

        var authorId   = AuthHelper.GetUserId(principal);
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

        // Employee articles always go to Pending — never published immediately
        var approvalStatus = isEmployee ? "Pending" : "NotRequired";
        var isPublished    = !isEmployee && dto.Publish;

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
            ? "Article submitted for approval. It will go live once SuperAdmin approves it."
            : "Article created successfully.";

        _log.LogInformation("Article {Id} created. Status={Status} IsPublished={Pub}",
            article.Id, approvalStatus, isPublished);

        return await OkJson(req,
            ApiResponse<object>.Ok(new { article.Id, article.Slug, approvalStatus }, message),
            HttpStatusCode.Created);
    }

    // ── PUT /api/articles/{id} ────────────────────────────────────────────────
    [Function("UpdateArticle")]
    public async Task<HttpResponseData> UpdateArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put",
            Route = "articles/{id:int}")] HttpRequestData req,
        int id)
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
            // Editing a rejected article → resubmit for approval
            if (article.ApprovalStatus == "Rejected")
            {
                article.ApprovalStatus = "Pending";
                article.ApprovalNote   = null;
                article.IsPublished    = false;
            }
        }
        else if (isAdmin)
        {
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

    // ── POST /api/articles/{id}/approve  [SuperAdmin/Admin] ──────────────────
    [Function("ApproveArticle")]
    public async Task<HttpResponseData> ApproveArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "articles/{id:int}/approve")] HttpRequestData req,
        int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Admin role required.");

        var adminId = AuthHelper.GetUserId(principal);
        var article = await _db.Articles.FindAsync(id);

        if (article == null)
            return await Fail(req, HttpStatusCode.NotFound, "Article not found.");
        if (article.ApprovalStatus != "Pending")
            return await Fail(req, HttpStatusCode.BadRequest,
                $"Article is not pending (current: {article.ApprovalStatus}).");

        article.ApprovalStatus = "Approved";
        article.ApprovalNote   = null;
        article.ApprovedById   = adminId;
        article.ApprovedAt     = DateTime.UtcNow;
        article.IsPublished    = true;
        article.PublishedAt    = DateTime.UtcNow;
        article.UpdatedAt      = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _log.LogInformation("Article {Id} approved by {AdminId}", id, adminId);
        return await OkJson(req, ApiResponse<object>.Ok(
            new { id, status = "Approved" },
            "Article approved and published on website."));
    }

    // ── POST /api/articles/{id}/reject  [SuperAdmin/Admin] ───────────────────
    [Function("RejectArticle")]
    public async Task<HttpResponseData> RejectArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "articles/{id:int}/reject")] HttpRequestData req,
        int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Admin role required.");

        var adminId = AuthHelper.GetUserId(principal);
        var article = await _db.Articles.FindAsync(id);

        if (article == null)
            return await Fail(req, HttpStatusCode.NotFound, "Article not found.");
        if (article.ApprovalStatus != "Pending")
            return await Fail(req, HttpStatusCode.BadRequest,
                "Article is not pending approval.");

        var body = await req.ReadAsStringAsync();
        using var doc  = JsonDocument.Parse(string.IsNullOrWhiteSpace(body) ? "{}" : body);
        var note = doc.RootElement.TryGetProperty("note", out var noteProp)
            ? noteProp.GetString()?.Trim()
            : null;

        article.ApprovalStatus = "Rejected";
        article.ApprovalNote   = string.IsNullOrEmpty(note)
            ? "Your article needs revision. Please contact your editor."
            : note;
        article.ApprovedById   = adminId;
        article.ApprovedAt     = DateTime.UtcNow;
        article.IsPublished    = false;
        article.UpdatedAt      = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        _log.LogInformation("Article {Id} rejected by {AdminId}. Note: {Note}",
            id, adminId, article.ApprovalNote);
        return await OkJson(req, ApiResponse<object>.Ok(
            new { id, status = "Rejected" }, "Article rejected."));
    }

    // ── DELETE /api/articles/{id}  [SuperAdmin] ───────────────────────────────
    [Function("DeleteArticle")]
    public async Task<HttpResponseData> DeleteArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete",
            Route = "articles/{id:int}")] HttpRequestData req,
        int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin required.");

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
