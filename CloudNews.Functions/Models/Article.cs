using System.ComponentModel.DataAnnotations;

namespace CloudNews.Functions.Models;

public class Article
{
    public int Id { get; set; }

    [Required, MaxLength(500)]
    public string Title { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Slug { get; set; } = string.Empty;

    // HTML content from WYSIWYG editor
    public string Content { get; set; } = string.Empty;

    [MaxLength(1000)]
    public string? ThumbnailUrl { get; set; }

    // Foreign keys
    public int CategoryId { get; set; }
    public int AuthorId { get; set; }

    public bool IsPublished { get; set; } = false;
    public int Views { get; set; } = 0;

    public DateTime? PublishedAt { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public Category? Category { get; set; }
    public User? Author { get; set; }
}
