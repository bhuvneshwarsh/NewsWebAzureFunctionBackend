using System.ComponentModel.DataAnnotations;

namespace CloudNews.Functions.DTOs;

// ── Requests ──────────────────────────────────────────────────────────────────

public class CreateArticleRequest
{
    [Required, MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [Required]
    public string Content { get; set; } = string.Empty;

    [Required]
    public int CategoryId { get; set; }

    public string? ThumbnailUrl { get; set; }

    // If true, publishes immediately; if false, saves as draft
    public bool Publish { get; set; } = false;
}

public class UpdateArticleRequest
{
    [MaxLength(500)]
    public string? Title { get; set; }

    public string? Content { get; set; }

    public int? CategoryId { get; set; }

    public string? ThumbnailUrl { get; set; }

    // null = don't change, true = publish, false = unpublish
    public bool? Publish { get; set; }
}

// ── Responses ─────────────────────────────────────────────────────────────────

public class ArticleListItem
{
    public int     Id           { get; set; }
    public string  Title        { get; set; } = string.Empty;
    public string  Slug         { get; set; } = string.Empty;
    public string? ThumbnailUrl { get; set; }
    public string  CategoryName { get; set; } = string.Empty;
    public string  AuthorName   { get; set; } = string.Empty;
    public bool    IsPublished  { get; set; }
    public int     Views        { get; set; }
    public DateTime? PublishedAt { get; set; }
    public DateTime  CreatedAt   { get; set; }
}

public class ArticleDetail : ArticleListItem
{
    public string Content    { get; set; } = string.Empty;
    public int    CategoryId { get; set; }
    public int    AuthorId   { get; set; }
}

public class PaginatedResult<T>
{
    public List<T> Items       { get; set; } = new();
    public int     Page        { get; set; }
    public int     PageSize    { get; set; }
    public int     TotalCount  { get; set; }
    public int     TotalPages  => (int)Math.Ceiling((double)TotalCount / PageSize);
    public bool    HasNext     => Page < TotalPages;
    public bool    HasPrevious => Page > 1;
}