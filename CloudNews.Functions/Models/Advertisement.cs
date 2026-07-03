using System.ComponentModel.DataAnnotations;

namespace CloudNews.Functions.Models;

public class Advertisement
{
    public int Id { get; set; }

    // Internal title — not shown on website
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    // Uploaded image URL (Azure Blob)
    [Required, MaxLength(1000)]
    public string AdImageUrl { get; set; } = string.Empty;

    // Optional click destination
    [MaxLength(1000)]
    public string? ClickUrl { get; set; }

    // Who placed the ad (for records)
    [MaxLength(200)]
    public string? AdvertiserName { get; set; }

    // Where on the page:
    // 'banner_top'    → full-width banner at top of homepage
    // 'sidebar'       → right sidebar on homepage / article pages
    // 'inline'        → between articles in the news feed
    // 'banner_bottom' → full-width banner at bottom of homepage
    [Required, MaxLength(50)]
    public string Placement { get; set; } = "sidebar";

    // Optional original dimensions (for aspect ratio hints)
    public int? Width  { get; set; }
    public int? Height { get; set; }

    // Scheduling
    public DateOnly? StartDate { get; set; }
    public DateOnly? EndDate   { get; set; }

    public bool IsActive     { get; set; } = true;
    public int  DisplayOrder { get; set; } = 0;

    // Basic analytics
    public int Impressions { get; set; } = 0;
    public int Clicks      { get; set; } = 0;

    // Internal notes
    [MaxLength(500)]
    public string? Notes { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
