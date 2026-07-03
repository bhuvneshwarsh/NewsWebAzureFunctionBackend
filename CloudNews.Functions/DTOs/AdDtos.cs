using System.ComponentModel.DataAnnotations;

namespace CloudNews.Functions.DTOs;

// ── Public response — what the frontend renders ───────────────────────────────
public class AdPublicDto
{
    public int     Id           { get; set; }
    public string  AdImageUrl   { get; set; } = string.Empty;
    public string? ClickUrl     { get; set; }
    public string  Placement    { get; set; } = string.Empty;
    public int?    Width        { get; set; }
    public int?    Height       { get; set; }
}

// ── Admin response — full details ─────────────────────────────────────────────
public class AdAdminDto : AdPublicDto
{
    public string  Title          { get; set; } = string.Empty;
    public string? AdvertiserName { get; set; }
    public string? StartDate      { get; set; }
    public string? EndDate        { get; set; }
    public bool    IsActive       { get; set; }
    public int     DisplayOrder   { get; set; }
    public int     Impressions    { get; set; }
    public int     Clicks         { get; set; }
    public string? Notes          { get; set; }
    public string  CreatedAt      { get; set; } = string.Empty;
}

// ── Create / Update ───────────────────────────────────────────────────────────
public class CreateAdRequest
{
    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(1000)]
    public string AdImageUrl { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? ClickUrl { get; set; }

    [MaxLength(200)]
    public string? AdvertiserName { get; set; }

    // banner_top | sidebar | inline | banner_bottom
    [Required]
    public string Placement { get; set; } = "sidebar";

    public int? Width  { get; set; }
    public int? Height { get; set; }

    public string? StartDate { get; set; }   // YYYY-MM-DD or null
    public string? EndDate   { get; set; }   // YYYY-MM-DD or null

    public bool    IsActive     { get; set; } = true;
    public int     DisplayOrder { get; set; } = 0;

    [MaxLength(500)]
    public string? Notes { get; set; }
}

public class UpdateAdRequest : CreateAdRequest { }
