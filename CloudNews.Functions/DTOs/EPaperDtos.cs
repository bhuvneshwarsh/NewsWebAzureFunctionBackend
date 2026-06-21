namespace CloudNews.Functions.DTOs;

public class EPaperResponse
{
    public int     Id           { get; set; }
    public string  Date         { get; set; } = string.Empty;  // "YYYY-MM-DD"
    public string  PdfUrl       { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public DateTime UploadedAt  { get; set; }
}