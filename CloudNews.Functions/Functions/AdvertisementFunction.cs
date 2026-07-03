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

public class AdvertisementFunction
{
    private readonly ApplicationDbContext          _db;
    private readonly IJwtService                   _jwt;
    private readonly IBlobService                  _blob;
    private readonly ILogger<AdvertisementFunction> _log;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true };

    private static readonly HashSet<string> ValidPlacements =
        new() { "banner_top", "sidebar", "inline", "banner_bottom" };

    public AdvertisementFunction(ApplicationDbContext db, IJwtService jwt,
        IBlobService blob, ILogger<AdvertisementFunction> log)
    {
        _db   = db;
        _jwt  = jwt;
        _blob = blob;
        _log  = log;
    }

    // ── GET /api/ads?placement=sidebar  (public) ──────────────────────────────
    // Returns only active ads valid for today for a given placement
    [Function("GetAds")]
    public async Task<HttpResponseData> GetAds(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ads")] HttpRequestData req)
    {
        var qs        = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var placement = qs["placement"];
        var today     = DateOnly.FromDateTime(DateTime.UtcNow);

        var query = _db.Advertisements
            .Where(a => a.IsActive)
            .Where(a => a.StartDate == null || a.StartDate <= today)
            .Where(a => a.EndDate   == null || a.EndDate   >= today)
            .OrderBy(a => a.DisplayOrder)
            .ThenBy(a => a.CreatedAt);

        if (!string.IsNullOrEmpty(placement))
            query = (IOrderedQueryable<Advertisement>)query
                .Where(a => a.Placement == placement);

        var ads = await query
            .Select(a => new AdPublicDto
            {
                Id         = a.Id,
                AdImageUrl = a.AdImageUrl,
                ClickUrl   = a.ClickUrl,
                Placement  = a.Placement,
                Width      = a.Width,
                Height     = a.Height,
            })
            .ToListAsync();

        return await OkJson(req, ApiResponse<List<AdPublicDto>>.Ok(ads));
    }

    // ── GET /api/ads/admin  [SuperAdmin] — all ads with stats ────────────────
    [Function("GetAdsAdmin")]
    public async Task<HttpResponseData> GetAdsAdmin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ads/admin")] HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Admin role required.");

        var ads = await _db.Advertisements
            .OrderBy(a => a.Placement)
            .ThenBy(a => a.DisplayOrder)
            .Select(a => new AdAdminDto
            {
                Id             = a.Id,
                Title          = a.Title,
                AdImageUrl     = a.AdImageUrl,
                ClickUrl       = a.ClickUrl,
                AdvertiserName = a.AdvertiserName,
                Placement      = a.Placement,
                Width          = a.Width,
                Height         = a.Height,
                StartDate      = a.StartDate.HasValue ? a.StartDate.Value.ToString("yyyy-MM-dd") : null,
                EndDate        = a.EndDate.HasValue   ? a.EndDate.Value.ToString("yyyy-MM-dd")   : null,
                IsActive       = a.IsActive,
                DisplayOrder   = a.DisplayOrder,
                Impressions    = a.Impressions,
                Clicks         = a.Clicks,
                Notes          = a.Notes,
                CreatedAt      = a.CreatedAt.ToString("yyyy-MM-dd"),
            })
            .ToListAsync();

        return await OkJson(req, ApiResponse<List<AdAdminDto>>.Ok(ads));
    }

    // ── POST /api/ads  [SuperAdmin] ───────────────────────────────────────────
    [Function("CreateAd")]
    public async Task<HttpResponseData> CreateAd(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "ads")] HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin role required.");

        var body = await req.ReadAsStringAsync();
        var dto  = JsonSerializer.Deserialize<CreateAdRequest>(body ?? "", JsonOpts);

        if (dto == null || string.IsNullOrWhiteSpace(dto.Title))
            return await Fail(req, HttpStatusCode.BadRequest, "Title is required.");
        if (string.IsNullOrWhiteSpace(dto.AdImageUrl))
            return await Fail(req, HttpStatusCode.BadRequest, "Ad image is required.");
        if (!ValidPlacements.Contains(dto.Placement))
            return await Fail(req, HttpStatusCode.BadRequest,
                $"Invalid placement. Valid values: {string.Join(", ", ValidPlacements)}");

        var ad = new Advertisement
        {
            Title          = dto.Title.Trim(),
            AdImageUrl     = dto.AdImageUrl.Trim(),
            ClickUrl       = dto.ClickUrl?.Trim(),
            AdvertiserName = dto.AdvertiserName?.Trim(),
            Placement      = dto.Placement,
            Width          = dto.Width,
            Height         = dto.Height,
            StartDate      = ParseDate(dto.StartDate),
            EndDate        = ParseDate(dto.EndDate),
            IsActive       = dto.IsActive,
            DisplayOrder   = dto.DisplayOrder,
            Notes          = dto.Notes?.Trim(),
            CreatedAt      = DateTime.UtcNow,
            UpdatedAt      = DateTime.UtcNow,
        };

        _db.Advertisements.Add(ad);
        await _db.SaveChangesAsync();

        _log.LogInformation("Ad created: {Id} — {Title} [{Placement}]",
            ad.Id, ad.Title, ad.Placement);

        return await OkJson(req, ApiResponse<AdAdminDto>.Ok(MapToAdmin(ad),
            "Advertisement created successfully."), HttpStatusCode.Created);
    }

    // ── PUT /api/ads/{id}  [SuperAdmin] ───────────────────────────────────────
    [Function("UpdateAd")]
    public async Task<HttpResponseData> UpdateAd(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "ads/{id:int}")] HttpRequestData req,
        int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin role required.");

        var ad = await _db.Advertisements.FindAsync(id);
        if (ad == null)
            return await Fail(req, HttpStatusCode.NotFound, "Advertisement not found.");

        var body = await req.ReadAsStringAsync();
        var dto  = JsonSerializer.Deserialize<UpdateAdRequest>(body ?? "", JsonOpts);
        if (dto == null)
            return await Fail(req, HttpStatusCode.BadRequest, "Invalid request body.");

        ad.Title          = dto.Title.Trim();
        ad.AdImageUrl     = dto.AdImageUrl.Trim();
        ad.ClickUrl       = dto.ClickUrl?.Trim();
        ad.AdvertiserName = dto.AdvertiserName?.Trim();
        ad.Placement      = dto.Placement;
        ad.Width          = dto.Width;
        ad.Height         = dto.Height;
        ad.StartDate      = ParseDate(dto.StartDate);
        ad.EndDate        = ParseDate(dto.EndDate);
        ad.IsActive       = dto.IsActive;
        ad.DisplayOrder   = dto.DisplayOrder;
        ad.Notes          = dto.Notes?.Trim();
        ad.UpdatedAt      = DateTime.UtcNow;

        await _db.SaveChangesAsync();
        return await OkJson(req, ApiResponse<AdAdminDto>.Ok(MapToAdmin(ad), "Advertisement updated."));
    }

    // ── DELETE /api/ads/{id}  [SuperAdmin] ────────────────────────────────────
    [Function("DeleteAd")]
    public async Task<HttpResponseData> DeleteAd(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "ads/{id:int}")] HttpRequestData req,
        int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin role required.");

        var ad = await _db.Advertisements.FindAsync(id);
        if (ad == null)
            return await Fail(req, HttpStatusCode.NotFound, "Advertisement not found.");

        // Delete image from blob
        if (!string.IsNullOrEmpty(ad.AdImageUrl))
            await _blob.DeleteAsync(ad.AdImageUrl);

        _db.Advertisements.Remove(ad);
        await _db.SaveChangesAsync();

        return await OkJson(req, ApiResponse<object>.Ok(new { id }, "Advertisement deleted."));
    }

    // ── POST /api/ads/{id}/impression  (public — fire & forget) ──────────────
    [Function("TrackImpression")]
    public async Task<HttpResponseData> TrackImpression(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "ads/{id:int}/impression")] HttpRequestData req, int id)
    {
        // Fire and forget — best effort, no error if fails
        try
        {
            await _db.Advertisements
                .Where(a => a.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Impressions, a => a.Impressions + 1));
        }
        catch { /* ignore */ }

        var res = req.CreateResponse(HttpStatusCode.NoContent);
        return res;
    }

    // ── POST /api/ads/{id}/click  (public) ───────────────────────────────────
    [Function("TrackClick")]
    public async Task<HttpResponseData> TrackClick(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "ads/{id:int}/click")] HttpRequestData req, int id)
    {
        try
        {
            await _db.Advertisements
                .Where(a => a.Id == id)
                .ExecuteUpdateAsync(s => s.SetProperty(a => a.Clicks, a => a.Clicks + 1));
        }
        catch { /* ignore */ }

        var res = req.CreateResponse(HttpStatusCode.NoContent);
        return res;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, out var d) ? d : null;

    private static AdAdminDto MapToAdmin(Advertisement a) => new()
    {
        Id             = a.Id,
        Title          = a.Title,
        AdImageUrl     = a.AdImageUrl,
        ClickUrl       = a.ClickUrl,
        AdvertiserName = a.AdvertiserName,
        Placement      = a.Placement,
        Width          = a.Width,
        Height         = a.Height,
        StartDate      = a.StartDate?.ToString("yyyy-MM-dd"),
        EndDate        = a.EndDate?.ToString("yyyy-MM-dd"),
        IsActive       = a.IsActive,
        DisplayOrder   = a.DisplayOrder,
        Impressions    = a.Impressions,
        Clicks         = a.Clicks,
        Notes          = a.Notes,
        CreatedAt      = a.CreatedAt.ToString("yyyy-MM-dd"),
    };

    private static async Task<HttpResponseData> OkJson(HttpRequestData req, object data,
        HttpStatusCode code = HttpStatusCode.OK)
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
