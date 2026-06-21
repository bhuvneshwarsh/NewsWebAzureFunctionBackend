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
    private readonly ApplicationDbContext     _db;
    private readonly IBlobService             _blob;
    private readonly IJwtService              _jwt;
    private readonly ILogger<EPaperFunction>  _log;

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

    // ── GET /api/epaper?date=2024-06-15  (public) ─────────────────────────────
    [Function("GetEPaper")]
    public async Task<HttpResponseData> GetEPaper(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "epaper")] HttpRequestData req)
    {
        var qs       = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var dateStr  = qs["date"];

        EPaper? paper;

        if (!string.IsNullOrEmpty(dateStr) && DateOnly.TryParse(dateStr, out var date))
        {
            paper = await _db.EPapers.FirstOrDefaultAsync(p => p.Date == date);
        }
        else
        {
            // Default: latest issue
            paper = await _db.EPapers.OrderByDescending(p => p.Date).FirstOrDefaultAsync();
        }

        if (paper == null)
            return await Fail(req, HttpStatusCode.NotFound, "No e-paper found for this date.");

        return await OkJson(req, ApiResponse<EPaperResponse>.Ok(MapToDto(paper)));
    }

    // ── GET /api/epaper/list  (public) — last 30 issues ──────────────────────
    [Function("ListEPapers")]
    public async Task<HttpResponseData> ListEPapers(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "epaper/list")] HttpRequestData req)
    {
        var papers = await _db.EPapers
            .OrderByDescending(p => p.Date)
            .Take(30)
            .Select(p => new EPaperResponse
            {
                Id           = p.Id,
                Date         = p.Date.ToString("yyyy-MM-dd"),
                PdfUrl       = p.PdfUrl,
                ThumbnailUrl = p.ThumbnailUrl,
                UploadedAt   = p.UploadedAt
            })
            .ToListAsync();

        return await OkJson(req, ApiResponse<List<EPaperResponse>>.Ok(papers));
    }

    // ── POST /api/epaper  [Admin] — multipart/form-data ───────────────────────
    // Fields: file (PDF), date (YYYY-MM-DD), thumbnailUrl (optional)
    [Function("UploadEPaper")]
    public async Task<HttpResponseData> UploadEPaper(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "epaper")] HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Admin role required.");

        if (!req.Headers.TryGetValues("Content-Type", out var ctValues))
            return await Fail(req, HttpStatusCode.BadRequest, "Content-Type header missing.");

        var contentType = ctValues.FirstOrDefault() ?? "";
        if (!contentType.Contains("multipart/form-data"))
            return await Fail(req, HttpStatusCode.BadRequest, "Request must be multipart/form-data.");

        var boundary = contentType.Split("boundary=").LastOrDefault()?.Trim();
        if (string.IsNullOrEmpty(boundary))
            return await Fail(req, HttpStatusCode.BadRequest, "Boundary missing.");

        try
        {
            using var bodyStream = req.Body;
            var (pdfStream, fileName, dateStr, thumbnailUrl) =
                await ParseEPaperMultipart(bodyStream, boundary);

            if (pdfStream == null)
                return await Fail(req, HttpStatusCode.BadRequest, "No PDF file found in request.");

            if (!DateOnly.TryParse(dateStr, out var paperDate))
                return await Fail(req, HttpStatusCode.BadRequest, "Invalid date. Use YYYY-MM-DD.");

            // Check if an issue already exists for this date
            var existing = await _db.EPapers.FirstOrDefaultAsync(p => p.Date == paperDate);
            if (existing != null)
            {
                // Replace the old PDF
                await _blob.DeleteAsync(existing.PdfUrl);
                var newUrl = await _blob.UploadPdfAsync(pdfStream, fileName);
                existing.PdfUrl       = newUrl;
                existing.ThumbnailUrl = thumbnailUrl ?? existing.ThumbnailUrl;
                existing.UploadedAt   = DateTime.UtcNow;
                await _db.SaveChangesAsync();

                return await OkJson(req, ApiResponse<EPaperResponse>.Ok(MapToDto(existing),
                    "E-paper updated for " + paperDate));
            }

            var pdfUrl = await _blob.UploadPdfAsync(pdfStream, fileName);

            var paper = new EPaper
            {
                Date         = paperDate,
                PdfUrl       = pdfUrl,
                ThumbnailUrl = thumbnailUrl,
                UploadedAt   = DateTime.UtcNow
            };

            _db.EPapers.Add(paper);
            await _db.SaveChangesAsync();

            _log.LogInformation("EPaper uploaded for {Date}", paperDate);
            return await OkJson(req, ApiResponse<EPaperResponse>.Ok(MapToDto(paper),
                "E-paper uploaded for " + paperDate));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "EPaper upload failed");
            return await Fail(req, HttpStatusCode.InternalServerError, "Upload failed.");
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
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin role required.");

        var paper = await _db.EPapers.FindAsync(id);
        if (paper == null)
            return await Fail(req, HttpStatusCode.NotFound, "E-paper not found.");

        await _blob.DeleteAsync(paper.PdfUrl);
        _db.EPapers.Remove(paper);
        await _db.SaveChangesAsync();

        return await OkJson(req, ApiResponse<object>.Ok(new { id }, "E-paper deleted"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static EPaperResponse MapToDto(EPaper p) => new()
    {
        Id           = p.Id,
        Date         = p.Date.ToString("yyyy-MM-dd"),
        PdfUrl       = p.PdfUrl,
        ThumbnailUrl = p.ThumbnailUrl,
        UploadedAt   = p.UploadedAt
    };

    private static async Task<(MemoryStream? stream, string fileName, string date, string? thumbnailUrl)>
        ParseEPaperMultipart(Stream body, string boundary)
    {
        using var reader = new StreamReader(body);
        var content = await reader.ReadToEndAsync();

        MemoryStream? pdfStream  = null;
        string        fileName   = "epaper.pdf";
        string        dateStr    = DateOnly.FromDateTime(DateTime.Today).ToString("yyyy-MM-dd");
        string?       thumbnail  = null;

        var parts = content.Split($"--{boundary}");
        foreach (var part in parts)
        {
            if (!part.Contains("Content-Disposition")) continue;
            var headerEnd = part.IndexOf("\r\n\r\n");
            if (headerEnd < 0) continue;

            var headers  = part[..headerEnd];
            var partData = part[(headerEnd + 4)..];
            var endIdx   = partData.LastIndexOf("\r\n");
            if (endIdx > 0) partData = partData[..endIdx];

            var nameMatch = System.Text.RegularExpressions.Regex.Match(headers, @"name=""([^""]+)""");
            if (!nameMatch.Success) continue;
            var fieldName = nameMatch.Groups[1].Value;

            if (fieldName == "file")
            {
                var fnMatch = System.Text.RegularExpressions.Regex.Match(headers, @"filename=""([^""]+)""");
                if (fnMatch.Success) fileName = fnMatch.Groups[1].Value;
                var bytes = System.Text.Encoding.Latin1.GetBytes(partData);
                pdfStream = new MemoryStream(bytes);
            }
            else if (fieldName == "date")
            {
                dateStr = partData.Trim();
            }
            else if (fieldName == "thumbnailUrl")
            {
                thumbnail = partData.Trim();
            }
        }

        return (pdfStream, fileName, dateStr, thumbnail);
    }

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