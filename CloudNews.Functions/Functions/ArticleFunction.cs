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
    private readonly ApplicationDbContext   _db;
    private readonly IJwtService            _jwt;
    private readonly ILogger<ArticleFunction> _log;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNameCaseInsensitive = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public ArticleFunction(ApplicationDbContext db, IJwtService jwt, ILogger<ArticleFunction> log)
    {
        _db  = db;
        _jwt = jwt;
        _log = log;
    }

    // ── GET /api/articles?page=1&size=10&category=politics&published=true ─────
    [Function("GetArticles")]
    public async Task<HttpResponseData> GetArticles(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles")] HttpRequestData req)
    {
        var qs         = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var page       = int.TryParse(qs["page"],      out var p) ? Math.Max(1, p)  : 1;
        var size       = int.TryParse(qs["size"],      out var s) ? Math.Clamp(s, 1, 50) : 10;
        var category   = qs["category"];
        var onlyPublic = !bool.TryParse(qs["all"], out var all) || !all;

        var query = _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Author)
            .AsQueryable();

        if (onlyPublic)
            query = query.Where(a => a.IsPublished);

        if (!string.IsNullOrEmpty(category))
            query = query.Where(a => a.Category!.Slug == category);

        var total   = await query.CountAsync();
        var articles = await query
            .OrderByDescending(a => a.PublishedAt ?? a.CreatedAt)
            .Skip((page - 1) * size)
            .Take(size)
            .Select(a => new ArticleListItem
            {
                Id           = a.Id,
                Title        = a.Title,
                Slug         = a.Slug,
                ThumbnailUrl = a.ThumbnailUrl,
                CategoryName = a.Category!.Name,
                AuthorName   = a.Author!.FullName,
                IsPublished  = a.IsPublished,
                Views        = a.Views,
                PublishedAt  = a.PublishedAt,
                CreatedAt    = a.CreatedAt
            })
            .ToListAsync();

        var result = new PaginatedResult<ArticleListItem>
        {
            Items      = articles,
            Page       = page,
            PageSize   = size,
            TotalCount = total
        };

        return await OkJson(req, ApiResponse<PaginatedResult<ArticleListItem>>.Ok(result));
    }

    // ── GET /api/articles/{slug} ──────────────────────────────────────────────
    [Function("GetArticleBySlug")]
    public async Task<HttpResponseData> GetArticleBySlug(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "articles/{slug}")] HttpRequestData req,
        string slug)
    {
        var article = await _db.Articles
            .Include(a => a.Category)
            .Include(a => a.Author)
            .FirstOrDefaultAsync(a => a.Slug == slug && a.IsPublished);

        if (article == null)
            return await NotFound(req, "Article not found.");

        // Increment view count (fire and forget)
        _ = Task.Run(async () =>
        {
            await using var scope = _db.Database.GetDbConnection();
            article.Views++;
            await _db.SaveChangesAsync();
        });

        var detail = new ArticleDetail
        {
            Id           = article.Id,
            Title        = article.Title,
            Slug         = article.Slug,
            Content      = article.Content,
            ThumbnailUrl = article.ThumbnailUrl,
            CategoryId   = article.CategoryId,
            CategoryName = article.Category!.Name,
            AuthorId     = article.AuthorId,
            AuthorName   = article.Author!.FullName,
            IsPublished  = article.IsPublished,
            Views        = article.Views,
            PublishedAt  = article.PublishedAt,
            CreatedAt    = article.CreatedAt
        };

        return await OkJson(req, ApiResponse<ArticleDetail>.Ok(detail));
    }

    // ── POST /api/articles  [Admin | Reporter] ────────────────────────────────
    [Function("CreateArticle")]
    public async Task<HttpResponseData> CreateArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "articles")] HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin", "Reporter"))
            return await Unauthorized(req, "Admin or Reporter role required.");

        var body = await req.ReadAsStringAsync();
        var dto  = JsonSerializer.Deserialize<CreateArticleRequest>(body ?? "", JsonOpts);

        if (dto == null || string.IsNullOrWhiteSpace(dto.Title) || string.IsNullOrWhiteSpace(dto.Content))
            return await BadRequest(req, "Title and content are required.");

        if (!await _db.Categories.AnyAsync(c => c.Id == dto.CategoryId))
            return await BadRequest(req, "Invalid category.");

        var authorId = AuthHelper.GetUserId(principal);
        if (authorId == null)
            return await Unauthorized(req, "Invalid token.");

        var article = new Article
        {
            Title        = dto.Title.Trim(),
            Slug         = SlugService.Generate(dto.Title),
            Content      = dto.Content,
            ThumbnailUrl = dto.ThumbnailUrl,
            CategoryId   = dto.CategoryId,
            AuthorId     = authorId.Value,
            IsPublished  = dto.Publish,
            PublishedAt  = dto.Publish ? DateTime.UtcNow : null,
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow
        };

        _db.Articles.Add(article);
        await _db.SaveChangesAsync();

        _log.LogInformation("Article created: {Id} by user {AuthorId}", article.Id, authorId);
        return await Created(req, ApiResponse<object>.Ok(new { article.Id, article.Slug }, "Article created"));
    }

    // ── PUT /api/articles/{id}  [Admin | Reporter] ────────────────────────────
    [Function("UpdateArticle")]
    public async Task<HttpResponseData> UpdateArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "articles/{id:int}")] HttpRequestData req,
        int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin", "Reporter"))
            return await Unauthorized(req, "Admin or Reporter role required.");

        var article = await _db.Articles.FindAsync(id);
        if (article == null)
            return await NotFound(req, "Article not found.");

        var body = await req.ReadAsStringAsync();
        var dto  = JsonSerializer.Deserialize<UpdateArticleRequest>(body ?? "", JsonOpts);
        if (dto == null)
            return await BadRequest(req, "Invalid request body.");

        if (dto.Title        != null) article.Title        = dto.Title.Trim();
        if (dto.Content      != null) article.Content      = dto.Content;
        if (dto.ThumbnailUrl != null) article.ThumbnailUrl = dto.ThumbnailUrl;
        if (dto.CategoryId   != null) article.CategoryId   = dto.CategoryId.Value;

        if (dto.Publish == true && !article.IsPublished)
        {
            article.IsPublished = true;
            article.PublishedAt = DateTime.UtcNow;
        }
        else if (dto.Publish == false)
        {
            article.IsPublished = false;
        }

        article.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await OkJson(req, ApiResponse<object>.Ok(new { article.Id, article.Slug }, "Article updated"));
    }

    // ── DELETE /api/articles/{id}  [SuperAdmin] ───────────────────────────────
    [Function("DeleteArticle")]
    public async Task<HttpResponseData> DeleteArticle(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "articles/{id:int}")] HttpRequestData req,
        int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Unauthorized(req, "SuperAdmin role required.");

        var article = await _db.Articles.FindAsync(id);
        if (article == null)
            return await NotFound(req, "Article not found.");

        _db.Articles.Remove(article);
        await _db.SaveChangesAsync();

        return await OkJson(req, ApiResponse<object>.Ok(new { id }, "Article deleted"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static async Task<HttpResponseData> OkJson(HttpRequestData req, object data)
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(data, JsonOpts));
        return res;
    }

    private static async Task<HttpResponseData> Created(HttpRequestData req, object data)
    {
        var res = req.CreateResponse(HttpStatusCode.Created);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(data, JsonOpts));
        return res;
    }

    private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string msg)
    {
        var res = req.CreateResponse(HttpStatusCode.BadRequest);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(ApiResponse<object>.Fail(msg), JsonOpts));
        return res;
    }

    private static async Task<HttpResponseData> Unauthorized(HttpRequestData req, string msg)
    {
        var res = req.CreateResponse(HttpStatusCode.Unauthorized);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(ApiResponse<object>.Fail(msg), JsonOpts));
        return res;
    }

    private static async Task<HttpResponseData> NotFound(HttpRequestData req, string msg)
    {
        var res = req.CreateResponse(HttpStatusCode.NotFound);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(ApiResponse<object>.Fail(msg), JsonOpts));
        return res;
    }
}