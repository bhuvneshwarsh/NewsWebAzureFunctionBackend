using System.ComponentModel.DataAnnotations;

namespace CloudNews.Functions.DTOs;

// ── Public response ───────────────────────────────────────────────────────────
public class EditorProfileDto
{
    public int     Id           { get; set; }
    public string  FullName     { get; set; } = string.Empty;
    public string  Title        { get; set; } = string.Empty;
    public string? ImageUrl     { get; set; }
    public string  ShortBio     { get; set; } = string.Empty;
    public string  FullBio      { get; set; } = string.Empty;
    public string? Experience   { get; set; }
    public string? Education    { get; set; }
    public string? Awards       { get; set; }
    public string? Email        { get; set; }
    public string? Phone        { get; set; }
    public string? TwitterUrl   { get; set; }
    public string? FacebookUrl  { get; set; }
    public string? LinkedInUrl  { get; set; }
    public int     DisplayOrder { get; set; }
}

// ── Create / Update ───────────────────────────────────────────────────────────
public class CreateEditorProfileRequest
{
    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string Title { get; set; } = string.Empty;

    public string? ImageUrl { get; set; }

    [Required, MaxLength(500)]
    public string ShortBio { get; set; } = string.Empty;

    [Required]
    public string FullBio { get; set; } = string.Empty;

    [MaxLength(200)]
    public string? Experience  { get; set; }

    [MaxLength(500)]
    public string? Education   { get; set; }

    public string? Awards      { get; set; }

    [MaxLength(300)]
    public string? Email       { get; set; }

    [MaxLength(20)]
    public string? Phone       { get; set; }

    [MaxLength(500)]
    public string? TwitterUrl  { get; set; }

    [MaxLength(500)]
    public string? FacebookUrl { get; set; }

    [MaxLength(500)]
    public string? LinkedInUrl { get; set; }

    public bool IsActive     { get; set; } = true;
    public int  DisplayOrder { get; set; } = 0;
}
