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

public class EditorProfileFunction
{
    private readonly ApplicationDbContext          _db;
    private readonly IJwtService                   _jwt;
    private readonly IBlobService                  _blob;
    private readonly ILogger<EditorProfileFunction> _log;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true };

    public EditorProfileFunction(ApplicationDbContext db, IJwtService jwt,
        IBlobService blob, ILogger<EditorProfileFunction> log)
    {
        _db   = db;
        _jwt  = jwt;
        _blob = blob;
        _log  = log;
    }

    // ── GET /api/editors  (public) ────────────────────────────────────────────
    [Function("GetEditors")]
    public async Task<HttpResponseData> GetEditors(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "editors")]
        HttpRequestData req)
    {
        var editors = await _db.EditorProfiles
            .Where(e => e.IsActive)
            .OrderBy(e => e.DisplayOrder)
            .ThenBy(e => e.FullName)
            .Select(e => MapToDto(e))
            .ToListAsync();

        return await OkJson(req, ApiResponse<List<EditorProfileDto>>.Ok(editors));
    }

    // ── GET /api/editors/all  [Admin] — includes inactive ────────────────────
    [Function("GetEditorsAdmin")]
    public async Task<HttpResponseData> GetEditorsAdmin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "editors/all")]
        HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Admin role required.");

        var editors = await _db.EditorProfiles
            .OrderBy(e => e.DisplayOrder)
            .ThenBy(e => e.FullName)
            .Select(e => MapToDto(e))
            .ToListAsync();

        return await OkJson(req, ApiResponse<List<EditorProfileDto>>.Ok(editors));
    }

    // ── GET /api/editors/{id}  (public) ───────────────────────────────────────
    [Function("GetEditorById")]
    public async Task<HttpResponseData> GetEditorById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "editors/{id:int}")]
        HttpRequestData req, int id)
    {
        var editor = await _db.EditorProfiles
            .FirstOrDefaultAsync(e => e.Id == id && e.IsActive);

        if (editor == null)
            return await Fail(req, HttpStatusCode.NotFound, "Editor not found.");

        return await OkJson(req, ApiResponse<EditorProfileDto>.Ok(MapToDto(editor)));
    }

    // ── POST /api/editors  [SuperAdmin] ───────────────────────────────────────
    [Function("CreateEditor")]
    public async Task<HttpResponseData> CreateEditor(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "editors")]
        HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin role required.");

        var body = await req.ReadAsStringAsync();
        var dto  = JsonSerializer.Deserialize<CreateEditorProfileRequest>(body ?? "", JsonOpts);

        if (dto == null || string.IsNullOrWhiteSpace(dto.FullName))
            return await Fail(req, HttpStatusCode.BadRequest, "Full name is required.");
        if (string.IsNullOrWhiteSpace(dto.ShortBio))
            return await Fail(req, HttpStatusCode.BadRequest, "Short bio is required.");
        if (string.IsNullOrWhiteSpace(dto.FullBio))
            return await Fail(req, HttpStatusCode.BadRequest, "Full bio is required.");

        var editor = new EditorProfile
        {
            FullName     = dto.FullName.Trim(),
            Title        = dto.Title.Trim(),
            ImageUrl     = dto.ImageUrl?.Trim(),
            ShortBio     = dto.ShortBio.Trim(),
            FullBio      = dto.FullBio.Trim(),
            Experience   = dto.Experience?.Trim(),
            Education    = dto.Education?.Trim(),
            Awards       = dto.Awards?.Trim(),
            Email        = dto.Email?.Trim(),
            Phone        = dto.Phone?.Trim(),
            TwitterUrl   = dto.TwitterUrl?.Trim(),
            FacebookUrl  = dto.FacebookUrl?.Trim(),
            LinkedInUrl  = dto.LinkedInUrl?.Trim(),
            IsActive     = dto.IsActive,
            DisplayOrder = dto.DisplayOrder,
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow,
        };

        _db.EditorProfiles.Add(editor);
        await _db.SaveChangesAsync();

        _log.LogInformation("Editor profile created: {Id} — {Name}", editor.Id, editor.FullName);
        return await OkJson(req, ApiResponse<EditorProfileDto>.Ok(MapToDto(editor),
            "Editor profile created successfully."), HttpStatusCode.Created);
    }

    // ── PUT /api/editors/{id}  [SuperAdmin] ───────────────────────────────────
    [Function("UpdateEditor")]
    public async Task<HttpResponseData> UpdateEditor(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "editors/{id:int}")]
        HttpRequestData req, int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin role required.");

        var editor = await _db.EditorProfiles.FindAsync(id);
        if (editor == null)
            return await Fail(req, HttpStatusCode.NotFound, "Editor not found.");

        var body = await req.ReadAsStringAsync();
        var dto  = JsonSerializer.Deserialize<CreateEditorProfileRequest>(body ?? "", JsonOpts);
        if (dto == null)
            return await Fail(req, HttpStatusCode.BadRequest, "Invalid request.");

        editor.FullName     = dto.FullName.Trim();
        editor.Title        = dto.Title.Trim();
        editor.ShortBio     = dto.ShortBio.Trim();
        editor.FullBio      = dto.FullBio.Trim();
        editor.Experience   = dto.Experience?.Trim();
        editor.Education    = dto.Education?.Trim();
        editor.Awards       = dto.Awards?.Trim();
        editor.Email        = dto.Email?.Trim();
        editor.Phone        = dto.Phone?.Trim();
        editor.TwitterUrl   = dto.TwitterUrl?.Trim();
        editor.FacebookUrl  = dto.FacebookUrl?.Trim();
        editor.LinkedInUrl  = dto.LinkedInUrl?.Trim();
        editor.IsActive     = dto.IsActive;
        editor.DisplayOrder = dto.DisplayOrder;
        editor.UpdatedAt    = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(dto.ImageUrl))
            editor.ImageUrl = dto.ImageUrl.Trim();

        await _db.SaveChangesAsync();
        return await OkJson(req, ApiResponse<EditorProfileDto>.Ok(MapToDto(editor),
            "Editor profile updated."));
    }

    // ── DELETE /api/editors/{id}  [SuperAdmin] ────────────────────────────────
    [Function("DeleteEditor")]
    public async Task<HttpResponseData> DeleteEditor(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "editors/{id:int}")]
        HttpRequestData req, int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin role required.");

        var editor = await _db.EditorProfiles.FindAsync(id);
        if (editor == null)
            return await Fail(req, HttpStatusCode.NotFound, "Editor not found.");

        if (!string.IsNullOrEmpty(editor.ImageUrl))
            await _blob.DeleteAsync(editor.ImageUrl);

        _db.EditorProfiles.Remove(editor);
        await _db.SaveChangesAsync();

        return await OkJson(req, ApiResponse<object>.Ok(new { id }, "Editor deleted."));
    }

    // ── Mapper ────────────────────────────────────────────────────────────────
    private static EditorProfileDto MapToDto(EditorProfile e) => new()
    {
        Id           = e.Id,
        FullName     = e.FullName,
        Title        = e.Title,
        ImageUrl     = e.ImageUrl,
        ShortBio     = e.ShortBio,
        FullBio      = e.FullBio,
        Experience   = e.Experience,
        Education    = e.Education,
        Awards       = e.Awards,
        Email        = e.Email,
        Phone        = e.Phone,
        TwitterUrl   = e.TwitterUrl,
        FacebookUrl  = e.FacebookUrl,
        LinkedInUrl  = e.LinkedInUrl,
        DisplayOrder = e.DisplayOrder,
    };

    // ── HTTP helpers ──────────────────────────────────────────────────────────
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
