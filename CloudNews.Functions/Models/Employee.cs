using System.ComponentModel.DataAnnotations;

namespace CloudNews.Functions.Models;

public class Employee
{
    public int Id { get; set; }

    // Auto-generated unique Employee ID like "EMP-2024-00001"
    [Required, MaxLength(30)]
    public string EmployeeId { get; set; } = string.Empty;

    [Required, MaxLength(200)]
    public string FullName { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string Designation { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Email { get; set; }

    [MaxLength(20)]
    public string? Mobile { get; set; }

    [MaxLength(500)]
    public string? Address { get; set; }

    public DateOnly? DateOfBirth { get; set; }

    // Profile photo — stored in Azure Blob
    [MaxLength(1000)]
    public string? ImageUrl { get; set; }

    // Govt ID (Aadhaar / PAN / Passport etc.)
    [MaxLength(100)]
    public string? GovtIdNumber { get; set; }

    [MaxLength(50)]
    public string? GovtIdType { get; set; }  // "Aadhaar", "PAN", "Passport", etc.

    // Employment validity
    public DateOnly? ValidUpto { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; } = 0;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
