using System.ComponentModel.DataAnnotations;

namespace CloudNews.Functions.Models;

public class ContactMessage
{
    public int Id { get; set; }

    [Required, MaxLength(200)]
    public string Name { get; set; } = string.Empty;

    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; set; } = string.Empty;

    [Required, MaxLength(500)]
    public string Subject { get; set; } = string.Empty;

    [Required, MaxLength(10000)]
    public string Message { get; set; } = string.Empty;

    public DateTime SubmittedAt { get; set; } = DateTime.UtcNow;
}
