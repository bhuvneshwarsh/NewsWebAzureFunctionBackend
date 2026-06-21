using System.ComponentModel.DataAnnotations;

namespace CloudNews.Functions.Models;

public class User
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required, MaxLength(300)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    // Values: SuperAdmin | Admin | Reporter | User
    [Required, MaxLength(50)]
    public string Role { get; set; } = "User";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation
    public ICollection<Article> Articles { get; set; } = new List<Article>();
}
