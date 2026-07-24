using System.ComponentModel.DataAnnotations;

namespace CloudNews.Functions.Models;

public class EditorProfile
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    // e.g. "मुख्य संपादक / Chief Editor"
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? ImageUrl { get; set; }

    // Short one-line intro shown on About page card
    [Required, MaxLength(500)]
    public string ShortBio { get; set; } = string.Empty;

    // Full life journey — shown in expanded section
    [Required]
    public string FullBio { get; set; } = string.Empty;

    // e.g. "25+ वर्षों का अनुभव"
    [MaxLength(200)]
    public string? Experience { get; set; }

    [MaxLength(500)]
    public string? Education { get; set; }

    // Awards and achievements (plain text, one per line)
    public string? Awards { get; set; }

    [MaxLength(300)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Phone { get; set; }

    [MaxLength(500)]
    public string? TwitterUrl { get; set; }

    [MaxLength(500)]
    public string? FacebookUrl { get; set; }

    [MaxLength(500)]
    public string? LinkedInUrl { get; set; }

    public bool IsActive     { get; set; } = true;
    public int  DisplayOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
