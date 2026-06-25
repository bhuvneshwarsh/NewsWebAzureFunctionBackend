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
    private BlobServiceClient?              _client;
    private readonly string?                _connStr;
    private readonly string                 _cdnBase;
    private readonly ILogger<BlobService>   _log;
    private readonly object                 _lockObj = new();

    private const string ImagesContainer  = "uploads";
    private const string EPapersContainer = "epapers";

    public BlobService(IConfiguration config, ILogger<BlobService> log)
    {
        _log     = log;
        _connStr = config["AzureBlobConnectionString"];
        _cdnBase = config["CdnBaseUrl"] ?? string.Empty;

        // ── Defer connection initialization to avoid startup crashes ──────────────────────
        // If the connection string is a placeholder or invalid, we'll catch it on first use
        if (string.IsNullOrWhiteSpace(_connStr) || _connStr.Contains("YOUR_STORAGE_ACCOUNT"))
        {
            _log.LogWarning("AzureBlobConnectionString is not configured or is a placeholder. " +
                            "Blob operations will fail until properly configured in Azure Function App → Configuration.");
        }
    }

    // ── Lazy-initialize the blob client on first use ───────────────────────────────────
    private BlobServiceClient GetClient()
    {
        if (_client != null)
            return _client;

        lock (_lockObj)
        {
            if (_client != null)
                return _client;

            if (string.IsNullOrWhiteSpace(_connStr) || _connStr.Contains("YOUR_STORAGE_ACCOUNT"))
            {
                _log.LogError("FATAL: AzureBlobConnectionString is missing or is a placeholder. " +
                              "Add it to Azure Function App → Configuration → Application Settings.");
                throw new InvalidOperationException(
                    "AzureBlobConnectionString is not configured. " +
                    "Go to Azure Portal → Your Function App → Configuration → Application Settings " +
                    "and add the key 'AzureBlobConnectionString' with your storage connection string.");
            }

            _client = new BlobServiceClient(_connStr);
            _log.LogInformation("BlobService connected. Account: {Account}, CDN: {Cdn}",
                _client.AccountName, string.IsNullOrEmpty(_cdnBase) ? "none (direct blob)" : _cdnBase);
            return _client;
        }
    }

    // ── Upload article image ──────────────────────────────────────────────────
    public async Task<string> UploadImageAsync(Stream stream, string fileName, string contentType)
    {
        try
        {
            var client    = GetClient();
            var container = client.GetBlobContainerClient(ImagesContainer);
            await container.CreateIfNotExistsAsync(PublicAccessType.Blob);

            var blobName = $"{Guid.NewGuid():N}_{SanitizeName(fileName)}";
            var blob     = container.GetBlobClient(blobName);

            stream.Position = 0;
            await blob.UploadAsync(stream, new BlobHttpHeaders { ContentType = contentType });

            var url = BuildUrl(client, ImagesContainer, blobName);
            _log.LogInformation("Image uploaded successfully: {Url}", url);
            return url;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to upload image '{FileName}' to container '{Container}'",
                fileName, ImagesContainer);
            throw;
        }
    }

    // ── Upload E-Paper PDF ────────────────────────────────────────────────────
    public async Task<string> UploadPdfAsync(Stream stream, string fileName)
    {
        try
        {
            var client    = GetClient();
            var container = client.GetBlobContainerClient(EPapersContainer);
            await container.CreateIfNotExistsAsync(PublicAccessType.Blob);

            var blobName = $"{Guid.NewGuid():N}_{SanitizeName(fileName)}";
            var blob     = container.GetBlobClient(blobName);

            stream.Position = 0;
            await blob.UploadAsync(stream, new BlobHttpHeaders { ContentType = "application/pdf" });

            var url = BuildUrl(client, EPapersContainer, blobName);
            _log.LogInformation("PDF uploaded successfully: {Url}", url);
            return url;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Failed to upload PDF '{FileName}' to container '{Container}'",
                fileName, EPapersContainer);
            throw;
        }
    }

    // ── Delete blob ───────────────────────────────────────────────────────────
    public async Task DeleteAsync(string blobUrl)
    {
        try
        {
            var client   = GetClient();
            var uri      = new Uri(blobUrl);
            var segments = uri.AbsolutePath.TrimStart('/').Split('/', 2);
            if (segments.Length < 2) return;

            var container = client.GetBlobContainerClient(segments[0]);
            await container.GetBlobClient(segments[1]).DeleteIfExistsAsync();
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Failed to delete blob: {Url}", blobUrl);
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private string BuildUrl(BlobServiceClient client, string container, string blobName)
    {
        if (!string.IsNullOrEmpty(_cdnBase))
            return $"{_cdnBase.TrimEnd('/')}/{container}/{blobName}";

        return $"https://{client.AccountName}.blob.core.windows.net/{container}/{blobName}";
    }

    private static string SanitizeName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(
            Path.GetFileName(name).ToLowerInvariant(), @"[^a-z0-9._-]", "_");
}