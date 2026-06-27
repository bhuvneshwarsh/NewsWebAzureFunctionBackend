using System.ComponentModel.DataAnnotations;

namespace CloudNews.Functions.Models;

public class Employee
{
    public int Id { get; set; }

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

    [MaxLength(1000)]
    public string? ImageUrl { get; set; }

    [MaxLength(100)]
    public string? GovtIdNumber { get; set; }

    [MaxLength(50)]
    public string? GovtIdType { get; set; }

    public DateOnly? ValidUpto { get; set; }

    public bool IsActive { get; set; } = true;

    public int DisplayOrder { get; set; } = 0;

    // ── Login access ──────────────────────────────────────────────────────────
    // Whether this employee has been granted portal login access
    public bool HasLoginAccess { get; set; } = false;

    // FK to Users table — set when login is created
    public int? LinkedUserId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
