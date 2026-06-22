using System.Net;
using System.Text.Json;
using CloudNews.Functions.DTOs;
using CloudNews.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Logging;

namespace CloudNews.Functions.Functions;

public class MediaFunction
{
    private readonly IBlobService           _blob;
    private readonly IJwtService            _jwt;
    private readonly ILogger<MediaFunction> _log;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private static readonly HashSet<string> AllowedImageTypes =
        new(StringComparer.OrdinalIgnoreCase)
        { "image/jpeg", "image/png", "image/webp", "image/gif", "image/jpg" };

    public MediaFunction(IBlobService blob, IJwtService jwt, ILogger<MediaFunction> log)
    {
        _blob = blob;
        _jwt  = jwt;
        _log  = log;
    }

    // ── OPTIONS /api/media/upload — CORS preflight ────────────────────────────
    [Function("UploadMediaOptions")]
    public HttpResponseData UploadMediaOptions(
        [HttpTrigger(AuthorizationLevel.Anonymous, "options", Route = "media/upload")] HttpRequestData req)
    {
        var res = req.CreateResponse(HttpStatusCode.NoContent);
        return res;
    }

    // ── POST /api/media/upload  [Admin | Reporter] ────────────────────────────
    [Function("UploadMedia")]
    public async Task<HttpResponseData> UploadMedia(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "media/upload")] HttpRequestData req)
    {
        _log.LogInformation("Media upload request received. ContentLength: {Len}",
            req.Headers.TryGetValues("Content-Length", out var cl) ? cl.First() : "unknown");

        // ── Auth check ────────────────────────────────────────────────────────
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin", "Reporter"))
        {
            _log.LogWarning("Unauthorized media upload attempt.");
            return await Fail(req, HttpStatusCode.Unauthorized, "Login required.");
        }

        // ── Content-Type check ────────────────────────────────────────────────
        req.Headers.TryGetValues("Content-Type", out var ctValues);
        var contentType = ctValues?.FirstOrDefault() ?? "";
        _log.LogInformation("Content-Type: {CT}", contentType);

        if (!contentType.Contains("multipart/form-data", StringComparison.OrdinalIgnoreCase))
            return await Fail(req, HttpStatusCode.BadRequest,
                "Request must be multipart/form-data. Received: " + contentType);

        var boundary = ExtractBoundary(contentType);
        if (string.IsNullOrEmpty(boundary))
            return await Fail(req, HttpStatusCode.BadRequest, "Multipart boundary missing.");

        try
        {
            // ── Read body as raw bytes (preserves binary data) ────────────────
            using var ms = new MemoryStream();
            await req.Body.CopyToAsync(ms);
            var bodyBytes = ms.ToArray();
            _log.LogInformation("Body size: {Size} bytes", bodyBytes.Length);

            var (fileBytes, fileName, fileMimeType) = ParseMultipartBytes(bodyBytes, boundary);

            if (fileBytes == null || fileBytes.Length == 0)
            {
                _log.LogError("No file found in multipart body. Boundary used: {B}", boundary);
                return await Fail(req, HttpStatusCode.BadRequest,
                    "No file found in request. Ensure the form field is named 'file'.");
            }

            _log.LogInformation("Parsed file: {Name}, type: {Type}, size: {Size}",
                fileName, fileMimeType, fileBytes.Length);

            if (!AllowedImageTypes.Contains(fileMimeType))
                return await Fail(req, HttpStatusCode.BadRequest,
                    $"File type '{fileMimeType}' not allowed. Use JPEG, PNG, WebP, or GIF.");

            if (fileBytes.Length > 5 * 1024 * 1024)
                return await Fail(req, HttpStatusCode.BadRequest,
                    "File exceeds 5 MB limit.");

            using var fileStream = new MemoryStream(fileBytes);
            var url = await _blob.UploadImageAsync(fileStream, fileName, fileMimeType);

            return await OkJson(req, new { url, success = true });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Media upload failed");
            return await Fail(req, HttpStatusCode.InternalServerError,
                $"Upload failed: {ex.Message}. Check Application Insights logs for details.");
        }
    }

    // ── Binary-safe multipart parser ──────────────────────────────────────────
    // Uses bytes instead of strings so binary image data is never corrupted
    private static (byte[]? fileBytes, string fileName, string mimeType)
        ParseMultipartBytes(byte[] body, string boundary)
    {
        var boundaryBytes = System.Text.Encoding.ASCII.GetBytes("--" + boundary);
        var crlf          = new byte[] { 0x0D, 0x0A };
        var doubleCrlf    = new byte[] { 0x0D, 0x0A, 0x0D, 0x0A };

        // Find all boundary positions
        var positions = FindAll(body, boundaryBytes);
        if (positions.Count < 2) return (null, "", "");

        for (int i = 0; i < positions.Count - 1; i++)
        {
            var start = positions[i] + boundaryBytes.Length + 2; // skip \r\n after boundary
            var end   = positions[i + 1] - 2;                    // trim \r\n before next boundary
            if (start >= end) continue;

            var part = body[start..end];

            // Find header/body split
            var splitIdx = IndexOf(part, doubleCrlf);
            if (splitIdx < 0) continue;

            var headerBytes = part[..splitIdx];
            var fileBytes   = part[(splitIdx + 4)..];

            var headers = System.Text.Encoding.UTF8.GetString(headerBytes);

            // Must contain 'filename' to be a file part
            if (!headers.Contains("filename", StringComparison.OrdinalIgnoreCase)) continue;

            var fileName = ExtractHeaderValue(headers, "filename") ?? "upload.jpg";
            var mimeType = ExtractContentType(headers) ?? "image/jpeg";

            return (fileBytes, fileName, mimeType);
        }

        return (null, "", "");
    }

    private static List<int> FindAll(byte[] source, byte[] pattern)
    {
        var result = new List<int>();
        for (int i = 0; i <= source.Length - pattern.Length; i++)
        {
            bool match = true;
            for (int j = 0; j < pattern.Length; j++)
            {
                if (source[i + j] != pattern[j]) { match = false; break; }
            }
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
            {
                if (source[i + j] != pattern[j]) { match = false; break; }
            }
            if (match) return i;
        }
        return -1;
    }

    private static string? ExtractHeaderValue(string headers, string key)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            headers, $@"{key}=""([^""]+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value : null;
    }

    private static string? ExtractContentType(string headers)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            headers, @"Content-Type:\s*([^\r\n]+)",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success ? match.Groups[1].Value.Trim() : null;
    }

    private static string ExtractBoundary(string contentType)
    {
        var match = System.Text.RegularExpressions.Regex.Match(
            contentType, @"boundary=(?:""([^""]+)""|([^\s;]+))",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        return match.Success
            ? (match.Groups[1].Value.Length > 0 ? match.Groups[1].Value : match.Groups[2].Value)
            : "";
    }

    // ── Helpers ───────────────────────────────────────────────────────────────
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
        await res.WriteStringAsync(JsonSerializer.Serialize(
            ApiResponse<object>.Fail(msg), JsonOpts));
        return res;
    }
}