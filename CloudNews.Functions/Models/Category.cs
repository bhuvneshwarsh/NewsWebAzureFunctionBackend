using System.ComponentModel.DataAnnotations;

namespace CloudNews.Functions.Models;

public class Category
{
    public int Id { get; set; }

    [Required, MaxLength(100)]
    public string Name { get; set; } = string.Empty;

    // URL-friendly slug e.g. "politics", "sports"
    [Required, MaxLength(100)]
    public string Slug { get; set; } = string.Empty;

    // Navigation
    public ICollection<Article> Articles { get; set; } = new List<Article>();
}
