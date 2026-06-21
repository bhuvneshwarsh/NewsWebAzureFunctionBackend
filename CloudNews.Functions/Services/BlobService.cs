using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace CloudNews.Functions.Services;

public interface IBlobService
{
    Task<string> UploadImageAsync(Stream stream, string fileName, string contentType);
    Task<string> UploadPdfAsync(Stream stream, string fileName);
    Task DeleteAsync(string blobUrl);
}

public class BlobService : IBlobService
{
    private readonly BlobServiceClient    _client;
    private readonly string               _cdnBase;
    private readonly ILogger<BlobService> _log;

    // Container names
    private const string ImagesContainer  = "uploads";
    private const string EPapersContainer = "epapers";

    public BlobService(IConfiguration config, ILogger<BlobService> log)
    {
        var connStr = config["AzureBlobConnectionString"]
            ?? throw new InvalidOperationException("AzureBlobConnectionString not configured");

        _client  = new BlobServiceClient(connStr);
        _cdnBase = config["CdnBaseUrl"] ?? string.Empty;   // e.g. https://media.yournews.com
        _log     = log;
    }

    // ── Upload article image ──────────────────────────────────────────────────
    public async Task<string> UploadImageAsync(Stream stream, string fileName, string contentType)
    {
        var container = _client.GetBlobContainerClient(ImagesContainer);
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var blobName = $"{Guid.NewGuid():N}_{SanitizeName(fileName)}";
        var blob     = container.GetBlobClient(blobName);

        await blob.UploadAsync(stream, new BlobHttpHeaders { ContentType = contentType });

        return BuildUrl(ImagesContainer, blobName);
    }

    // ── Upload E-Paper PDF ────────────────────────────────────────────────────
    public async Task<string> UploadPdfAsync(Stream stream, string fileName)
    {
        var container = _client.GetBlobContainerClient(EPapersContainer);
        await container.CreateIfNotExistsAsync(PublicAccessType.Blob);

        var blobName = $"{Guid.NewGuid():N}_{SanitizeName(fileName)}";
        var blob     = container.GetBlobClient(blobName);

        await blob.UploadAsync(stream, new BlobHttpHeaders { ContentType = "application/pdf" });

        return BuildUrl(EPapersContainer, blobName);
    }

    // ── Delete blob by full URL ───────────────────────────────────────────────
    public async Task DeleteAsync(string blobUrl)
    {
        try
        {
            var uri       = new Uri(blobUrl);
            var segments  = uri.AbsolutePath.TrimStart('/').Split('/', 2);
            if (segments.Length < 2) return;

            var container = _client.GetBlobContainerClient(segments[0]);
            await container.GetBlobClient(segments[1]).DeleteIfExistsAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to delete blob: {Url}", blobUrl);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string BuildUrl(string container, string blobName)
    {
        if (!string.IsNullOrEmpty(_cdnBase))
            return $"{_cdnBase.TrimEnd('/')}/{container}/{blobName}";

        // Fall back to direct Azure Blob URL
        var account = _client.AccountName;
        return $"https://{account}.blob.core.windows.net/{container}/{blobName}";
    }

    private static string SanitizeName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(
            Path.GetFileName(name).ToLowerInvariant(), @"[^a-z0-9._-]", "_");
}