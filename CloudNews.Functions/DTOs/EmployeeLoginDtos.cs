namespace CloudNews.Functions.DTOs;

// ── Request: SuperAdmin grants login access to an employee ────────────────────
public class GrantEmployeeLoginRequest
{
    // Optional: override the email used for login (defaults to employee's registered email)
    public string? LoginEmail { get; set; }
}

// ── Request: Employee login ───────────────────────────────────────────────────
public class EmployeeLoginRequest
{
    public string EmployeeId { get; set; } = string.Empty;  // e.g. EMP-2026-00001
    public string Password   { get; set; } = string.Empty;
}

// ── Request: Employee changes their password ──────────────────────────────────
public class ChangePasswordRequest
{
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword     { get; set; } = string.Empty;
}

// ── Response: shown to SuperAdmin once after granting access ─────────────────
public class EmployeeLoginCreatedResponse
{
    public string EmployeeId    { get; set; } = string.Empty;
    public string FullName      { get; set; } = string.Empty;
    public string LoginEmail    { get; set; } = string.Empty;
    public string TempPassword  { get; set; } = string.Empty;  // shown once only
    public string Message       { get; set; } = string.Empty;
}

// ── Response: after successful employee login ────────────────────────────────
public class EmployeeAuthResponse
{
    public string  Token              { get; set; } = string.Empty;
    public string  FullName           { get; set; } = string.Empty;
    public string  Email              { get; set; } = string.Empty;
    public string  Role               { get; set; } = string.Empty;
    public string  EmployeeId         { get; set; } = string.Empty;
    public string  Designation        { get; set; } = string.Empty;
    public string? ImageUrl           { get; set; }
    public bool    MustChangePassword { get; set; }
    public DateTime Expiry            { get; set; }
}
