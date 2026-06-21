using System.ComponentModel.DataAnnotations;

namespace CloudNews.Functions.Models;

public class EPaper
{
    public int Id { get; set; }

    // Only one paper per date
    public DateOnly Date { get; set; }

    [Required, MaxLength(1000)]
    public string PdfUrl { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? ThumbnailUrl { get; set; }

    public DateTime UploadedAt { get; set; } = DateTime.UtcNow;
}
