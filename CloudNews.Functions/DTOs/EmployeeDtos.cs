using System.ComponentModel.DataAnnotations;

namespace CloudNews.Functions.DTOs;

// ── Public card view ──────────────────────────────────────────────────────────
public class EmployeeCardDto
{
    public int    Id          { get; set; }
    public string EmployeeId  { get; set; } = string.Empty;
    public string FullName    { get; set; } = string.Empty;
    public string Designation { get; set; } = string.Empty;
    public string? ImageUrl   { get; set; }
}

// ── Public detail view (shown on card click) ──────────────────────────────────
public class EmployeeDetailDto : EmployeeCardDto
{
    public string? Email     { get; set; }
    public string? ValidUpto { get; set; }   // "YYYY-MM-DD"
}

// ── Full admin view ───────────────────────────────────────────────────────────
public class EmployeeAdminDto : EmployeeDetailDto
{
    public string? Mobile       { get; set; }
    public string? Address      { get; set; }
    public string? DateOfBirth  { get; set; }
    public string? GovtIdType   { get; set; }
    public string? GovtIdNumber { get; set; }
    public bool    IsActive     { get; set; }
    public int     DisplayOrder { get; set; }
    public string  CreatedAt    { get; set; } = string.Empty;
}

// ── Create / Update requests ──────────────────────────────────────────────────
public class CreateEmployeeRequest
{
    [Required, MaxLength(200)]
    public string FullName    { get; set; } = string.Empty;

    [Required, MaxLength(150)]
    public string Designation { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? Email        { get; set; }
    [MaxLength(20)]
    public string? Mobile       { get; set; }
    [MaxLength(500)]
    public string? Address      { get; set; }
    public string? DateOfBirth  { get; set; }   // "YYYY-MM-DD"
    public string? ValidUpto    { get; set; }   // "YYYY-MM-DD"
    [MaxLength(100)]
    public string? GovtIdNumber { get; set; }
    [MaxLength(50)]
    public string? GovtIdType   { get; set; }
    public string? ImageUrl     { get; set; }
    public int     DisplayOrder { get; set; } = 0;
}

public class UpdateEmployeeRequest : CreateEmployeeRequest
{
    public bool IsActive { get; set; } = true;
}
