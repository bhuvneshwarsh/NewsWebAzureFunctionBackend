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
        { "image/jpeg", "image/png", "image/webp", "image/gif" };

    public MediaFunction(IBlobService blob, IJwtService jwt, ILogger<MediaFunction> log)
    {
        _blob = blob;
        _jwt  = jwt;
        _log  = log;
    }

    // ── POST /api/media/upload  [Admin | Reporter] ────────────────────────────
    // Accepts: multipart/form-data with field "file"
    // Returns: { url: "https://..." }
    [Function("UploadMedia")]
    public async Task<HttpResponseData> UploadMedia(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "media/upload")] HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin", "Admin", "Reporter"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Login required.");

        // Parse multipart form data
        if (!req.Headers.TryGetValues("Content-Type", out var ctValues))
            return await Fail(req, HttpStatusCode.BadRequest, "Content-Type header missing.");

        var contentType = ctValues.FirstOrDefault() ?? "";
        if (!contentType.Contains("multipart/form-data"))
            return await Fail(req, HttpStatusCode.BadRequest, "Request must be multipart/form-data.");

        // Extract boundary
        var boundary = contentType.Split("boundary=").LastOrDefault()?.Trim();
        if (string.IsNullOrEmpty(boundary))
            return await Fail(req, HttpStatusCode.BadRequest, "Multipart boundary missing.");

        try
        {
            using var bodyStream = req.Body;
            var (fileStream, fileName, fileMimeType) = await ParseMultipart(bodyStream, boundary);

            if (fileStream == null)
                return await Fail(req, HttpStatusCode.BadRequest, "No file found in request.");

            if (!AllowedImageTypes.Contains(fileMimeType))
                return await Fail(req, HttpStatusCode.BadRequest,
                    "Only JPEG, PNG, WebP, and GIF images are allowed.");

            // Max 5 MB
            if (fileStream.Length > 5 * 1024 * 1024)
                return await Fail(req, HttpStatusCode.BadRequest, "File size exceeds 5 MB limit.");

            var url = await _blob.UploadImageAsync(fileStream, fileName, fileMimeType);

            _log.LogInformation("Media uploaded: {Url}", url);
            return await OkJson(req, new { url });
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Media upload failed");
            return await Fail(req, HttpStatusCode.InternalServerError, "Upload failed.");
        }
    }

    // ── Minimal multipart parser ──────────────────────────────────────────────
    private static async Task<(MemoryStream? stream, string fileName, string mimeType)>
        ParseMultipart(Stream body, string boundary)
    {
        using var reader = new StreamReader(body);
        var content = await reader.ReadToEndAsync();

        var parts = content.Split($"--{boundary}");
        foreach (var part in parts)
        {
            if (!part.Contains("Content-Disposition")) continue;
            if (!part.Contains("filename="))           continue;

            // Extract filename
            var headerEnd  = part.IndexOf("\r\n\r\n");
            if (headerEnd < 0) continue;
            var headers    = part[..headerEnd];
            var fileData   = part[(headerEnd + 4)..];

            // Remove trailing boundary marker
            var endIdx = fileData.LastIndexOf("\r\n");
            if (endIdx > 0) fileData = fileData[..endIdx];

            var fileName = "upload.jpg";
            var fnMatch  = System.Text.RegularExpressions.Regex.Match(headers, @"filename=""([^""]+)""");
            if (fnMatch.Success) fileName = fnMatch.Groups[1].Value;

            var mimeType = "image/jpeg";
            var mtMatch  = System.Text.RegularExpressions.Regex.Match(headers, @"Content-Type:\s*(\S+)");
            if (mtMatch.Success) mimeType = mtMatch.Groups[1].Value.Trim();

            var bytes  = System.Text.Encoding.Latin1.GetBytes(fileData);
            var stream = new MemoryStream(bytes);
            return (stream, fileName, mimeType);
        }

        return (null, "", "");
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
        await res.WriteStringAsync(JsonSerializer.Serialize(ApiResponse<object>.Fail(msg), JsonOpts));
        return res;
    }
}