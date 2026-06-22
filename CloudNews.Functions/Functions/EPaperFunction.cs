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

public class EPaperFunction
{
    private readonly ApplicationDbContext    _db;
    private readonly IBlobService            _blob;
    private readonly IJwtService             _jwt;
    private readonly ILogger<EPaperFunction> _log;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public EPaperFunction(ApplicationDbContext db, IBlobService blob, IJwtService jwt,
        ILogger<EPaperFunction> log)
    {
        _db   = db;
        _blob = blob;
        _jwt  = jwt;
        _log  = log;
    }

    // ── OPTIONS preflight ─────────────────────────────────────────────────────
    [Function("EPaperOptions")]
    public HttpResponseData EPaperOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "epaper")] HttpRequestData req)
        => req.CreateResponse(HttpStatusCode.NoContent);

    // ── GET /api/epaper?date=YYYY-MM-DD  (public) ─────────────────────────────
    [Function("GetEPaper")]
    public async Task<HttpResponseData> GetEPaper(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "epaper")] HttpRequestData req)
    {
        var qs      = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var dateStr = qs["date"];

        EPaper? paper;
        if (!string.IsNullOrEmpty(dateStr) && DateOnly.TryParse(dateStr, out var date))
            paper = await _db.EPapers.FirstOrDefaultAsync(p => p.Date == date);
        else
            paper = await _db.EPapers.OrderByDescending(p => p.Date).FirstOrDefaultAsync();

        if (paper == null)
            return await Fail(req, HttpStatusCode.NotFound, "No e-paper found for this date.");

        return await OkJson(req, ApiResponse<EPaperResponse>.Ok(MapToDto(paper)));
    }

    // ── GET /api/epaper/list  (public) ────────────────────────────────────────
    [Function("ListEPapers")]
    public async Task<HttpResponseData> ListEPapers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "epaper/list")] HttpRequestData req)
    {
        var papers = await _db.EPapers
            .OrderByDescending(p => p.Date)
            .Take(30)
            .Select(p => new EPaperResponse
            {
                Id = p.Id, Date = p.Date.ToString("yyyy-MM-dd"),
                PdfUrl = p.PdfUrl, ThumbnailUrl = p.ThumbnailUrl, UploadedAt = p.UploadedAt
            })
            .ToListAsync();

        return await OkJson(req, ApiResponse<List<EPaperResponse>>.Ok(papers));
    }

    // ── POST /api/epaper  [Admin] ─────────────────────────────────────────────
    [Function("UploadEPaper")]
    public async Task<HttpResponseData> UploadEPaper(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "epaper")] HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Admin role required.");

        req.Headers.TryGetValues("Content-Type", out var ctValues);
        var contentType = ctValues?.FirstOrDefault() ?? "";
        var boundary    = ExtractBoundary(contentType);

        if (string.IsNullOrEmpty(boundary))
            return await Fail(req, HttpStatusCode.BadRequest, "Multipart boundary missing.");

        try
        {
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms);
            var bodyBytes = ms.ToArray();

            var (pdfBytes, fileName, dateStr) = ParseEPaperBytes(bodyBytes, boundary);

            if (pdfBytes == null || pdfBytes.Length == 0)
                return await Fail(req, HttpStatusCode.BadRequest, "No PDF file found in request.");

            if (!DateOnly.TryParse(dateStr, out var paperDate))
                return await Fail(req, HttpStatusCode.BadRequest, "Invalid date. Use YYYY-MM-DD.");

            using var pdfStream = new MemoryStream(pdfBytes);
            var pdfUrl = await _blob.UploadPdfAsync(pdfStream, fileName);

            var existing = await _db.EPapers.FirstOrDefaultAsync(p => p.Date == paperDate);
            if (existing != null)
            {
                await _blob.DeleteAsync(existing.PdfUrl);
                existing.PdfUrl    = pdfUrl;
                existing.UploadedAt = DateTime.UtcNow;
                await _db.SaveChangesAsync();
                return await OkJson(req, ApiResponse<EPaperResponse>.Ok(MapToDto(existing), "E-paper updated."));
            }

            var paper = new EPaper { Date = paperDate, PdfUrl = pdfUrl, UploadedAt = DateTime.UtcNow };
            _db.EPapers.Add(paper);
            await _db.SaveChangesAsync();

            return await OkJson(req, ApiResponse<EPaperResponse>.Ok(MapToDto(paper), "E-paper uploaded."));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "EPaper upload failed");
            return await Fail(req, HttpStatusCode.InternalServerError, $"Upload failed: {ex.Message}");
        }
    }

    // ── DELETE /api/epaper/{id}  [SuperAdmin] ─────────────────────────────────
    [Function("DeleteEPaper")]
    public async Task<HttpResponseData> DeleteEPaper(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "epaper/{id:int}")] HttpRequestData req,
        int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin required.");

        var paper = await _db.EPapers.FindAsync(id);
        if (paper == null) return await Fail(req, HttpStatusCode.NotFound, "Not found.");

        await _blob.DeleteAsync(paper.PdfUrl);
        _db.EPapers.Remove(paper);
        await _db.SaveChangesAsync();

        return await OkJson(req, ApiResponse<object>.Ok(new { id }, "Deleted."));
    }

    // ── Binary-safe multipart parser for PDF ──────────────────────────────────
    private static (byte[]? pdfBytes, string fileName, string date)
        ParseEPaperBytes(byte[] body, string boundary)
    {
        var boundaryBytes = System.Text.Encoding.ASCII.GetBytes("--" + boundary);
        var doubleCrlf    = new byte[] { 0x0D, 0x0A, 0x0D, 0x0A };

        byte[]? pdfBytes = null;
        string  fileName = "epaper.pdf";
        string  dateStr  = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");

        var positions = FindAll(body, boundaryBytes);
        for (int i = 0; i < positions.Count - 1; i++)
        {
            var start = positions[i] + boundaryBytes.Length + 2;
            var end   = positions[i + 1] - 2;
            if (start >= end) continue;

            var part     = body[start..end];
            var splitIdx = IndexOf(part, doubleCrlf);
            if (splitIdx < 0) continue;

            var headers  = System.Text.Encoding.UTF8.GetString(part[..splitIdx]);
            var partData = part[(splitIdx + 4)..];

            var nameMatch = System.Text.RegularExpressions.Regex.Match(
                headers, @"name=""([^""]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (!nameMatch.Success) continue;

            var fieldName = nameMatch.Groups[1].Value;
            if (fieldName.Equals("file", StringComparison.OrdinalIgnoreCase))
            {
                var fnMatch = System.Text.RegularExpressions.Regex.Match(
                    headers, @"filename=""([^""]+)""", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                if (fnMatch.Success) fileName = fnMatch.Groups[1].Value;
                pdfBytes = partData;
            }
            else if (fieldName.Equals("date", StringComparison.OrdinalIgnoreCase))
            {
                dateStr = System.Text.Encoding.UTF8.GetString(partData).Trim();
            }
        }

        return (pdfBytes, fileName, dateStr);
    }

    // ── Shared byte helpers ───────────────────────────────────────────────────
    private static List<int> FindAll(byte[] source, byte[] pattern)
    {
        var result = new List<int>();
        for (int i = 0; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
                if (source[i + j] != pattern[j]) { match = false; break; }
            if (match) result.Add(i);
        }
        return result;
    }

    private static int IndexOf(byte[] source, byte[] pattern)
    {
        for (int i = 0; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
                if (source[i + j] != pattern[j]) { match = false; break; }
            if (match) return i;
        }
        return -1;
    }

    private static string ExtractBoundary(string contentType)
    {
        var m = System.Text.RegularExpressions.Regex.Match(
            contentType, @"boundary=(?:""([^""]+)""|([^\s;]+))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return m.Success ? (m.Groups[1].Value.Length > 0 ? m.Groups[1].Value : m.Groups[2].Value) : "";
    }

    private static EPaperResponse MapToDto(EPaper p) => new()
    {
        Id = p.Id, Date = p.Date.ToString("yyyy-MM-dd"),
        PdfUrl = p.PdfUrl, ThumbnailUrl = p.ThumbnailUrl, UploadedAt = p.UploadedAt
    };

    private static async Task<HttpResponseData> OkJson(HttpRequestData req, object data)
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(data, JsonOpts));
        return res;
    }

    private static async Task<HttpResponseData> Fail(HttpRequestData req, HttpStatusCode code, string msg)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(ApiResponse<object>.Fail(msg), JsonOpts));
        return res;
    }
}