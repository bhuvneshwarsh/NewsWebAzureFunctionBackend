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

public class AdminFunction
{
    private readonly ApplicationDbContext    _db;
    private readonly IJwtService             _jwt;
    private readonly ILogger<AdminFunction>  _log;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public AdminFunction(ApplicationDbContext db, IJwtService jwt, ILogger<AdminFunction> log)
    {
        _db  = db;
        _jwt = jwt;
        _log = log;
    }

    // ── GET /api/admin/stats  [Admin] ─────────────────────────────────────────
    [Function("GetAdminStats")]
    public async Task<HttpResponseData> GetAdminStats(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "admin/stats")] HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin"))
        {
            var denied = req.CreateResponse(HttpStatusCode.Unauthorized);
            await denied.WriteStringAsync("{\"success\":false,\"message\":\"Admin role required.\"}");
            return denied;
        }

        var stats = new
        {
            totalUsers        = await _db.Users.CountAsync(),
            totalArticles     = await _db.Articles.CountAsync(),
            publishedArticles = await _db.Articles.CountAsync(a => a.IsPublished),
            draftArticles     = await _db.Articles.CountAsync(a => !a.IsPublished),
            totalViews        = await _db.Articles.SumAsync(a => (long)a.Views),
            totalEPapers      = await _db.EPapers.CountAsync(),
            recentArticles    = await _db.Articles
                .Include(a => a.Author)
                .Include(a => a.Category)
                .OrderByDescending(a => a.CreatedAt)
                .Take(5)
                .Select(a => new
                {
                    a.Id, a.Title, a.IsPublished,
                    a.Views, a.CreatedAt,
                    author   = a.Author!.FullName,
                    category = a.Category!.Name
                })
                .ToListAsync()
        };

        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(
            ApiResponse<object>.Ok(stats), JsonOpts));
        return res;
    }
}