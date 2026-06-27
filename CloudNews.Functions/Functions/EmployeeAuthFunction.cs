using System.Net;
using System.Text.Json;
using CloudNews.Functions.Data;
using CloudNews.Functions.DTOs;
using CloudNews.Functions.Models;
using CloudNews.Functions.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CloudNews.Functions.Functions;

public class EmployeeAuthFunction
{
    private readonly ApplicationDbContext         _db;
    private readonly IJwtService                  _jwt;
    private readonly ILogger<EmployeeAuthFunction> _log;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public EmployeeAuthFunction(ApplicationDbContext db, IJwtService jwt,
        ILogger<EmployeeAuthFunction> log)
    {
        _db  = db;
        _jwt = jwt;
        _log = log;
    }

    // ── POST /api/employees/{id}/grant-login  [SuperAdmin] ────────────────────
    // Creates a User account linked to the employee with temp password
    [Function("GrantEmployeeLogin")]
    public async Task<HttpResponseData> GrantEmployeeLogin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "employees/{id:int}/grant-login")] HttpRequestData req, int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin role required.");

        var employee = await _db.Employees.FindAsync(id);
        if (employee == null)
            return await Fail(req, HttpStatusCode.NotFound, "Employee not found.");

        if (!employee.IsActive)
            return await Fail(req, HttpStatusCode.BadRequest,
                "Cannot grant login to an inactive employee. Activate them first.");

        // Read optional override email
        var body       = await req.ReadAsStringAsync();
        var dto        = string.IsNullOrWhiteSpace(body)
            ? new GrantEmployeeLoginRequest()
            : JsonSerializer.Deserialize<GrantEmployeeLoginRequest>(body, JsonOpts)
              ?? new GrantEmployeeLoginRequest();

        var loginEmail = (!string.IsNullOrWhiteSpace(dto.LoginEmail)
            ? dto.LoginEmail
            : employee.Email)?.ToLower().Trim();

        if (string.IsNullOrEmpty(loginEmail))
            return await Fail(req, HttpStatusCode.BadRequest,
                "Employee has no email address. Please add an email to the employee profile first, " +
                "or provide a loginEmail in the request body.");

        // ── If already has login, just reset the password ─────────────────────
        if (employee.HasLoginAccess && employee.LinkedUserId.HasValue)
        {
            var existingUser = await _db.Users.FindAsync(employee.LinkedUserId.Value);
            if (existingUser != null)
            {
                var resetPwd  = GenerateTempPassword(employee.EmployeeId);
                existingUser.PasswordHash       = BCrypt.Net.BCrypt.HashPassword(resetPwd);
                existingUser.MustChangePassword = true;
                await _db.SaveChangesAsync();

                _log.LogInformation("Employee login password reset: {EmpId}", employee.EmployeeId);

                return await OkJson(req, ApiResponse<EmployeeLoginCreatedResponse>.Ok(
                    new EmployeeLoginCreatedResponse
                    {
                        EmployeeId   = employee.EmployeeId,
                        FullName     = employee.FullName,
                        LoginEmail   = existingUser.Email,
                        TempPassword = resetPwd,
                        Message      = "Password has been reset. Share these credentials with the employee. " +
                                       "They will be asked to change their password on next login."
                    }, "Password reset successfully."));
            }
        }

        // ── Check email not already taken by another account ──────────────────
        var emailTaken = await _db.Users.AnyAsync(u =>
            u.Email == loginEmail && u.Id != employee.LinkedUserId);
        if (emailTaken)
            return await Fail(req, HttpStatusCode.Conflict,
                $"The email '{loginEmail}' is already used by another account. " +
                "Provide a different loginEmail in the request body.");

        // ── Create User account ───────────────────────────────────────────────
        var tempPassword = GenerateTempPassword(employee.EmployeeId);

        var user = new User
        {
            FullName          = employee.FullName,
            Email             = loginEmail,
            PasswordHash      = BCrypt.Net.BCrypt.HashPassword(tempPassword),
            Role              = "Employee",
            MustChangePassword = true,
            CreatedAt         = DateTime.UtcNow
        };

        _db.Users.Add(user);
        await _db.SaveChangesAsync();   // get user.Id

        // Link employee ↔ user
        employee.LinkedUserId   = user.Id;
        employee.HasLoginAccess = true;
        employee.UpdatedAt      = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _log.LogInformation("Employee login created: {EmpId} → UserId {UserId}",
            employee.EmployeeId, user.Id);

        return await OkJson(req, ApiResponse<EmployeeLoginCreatedResponse>.Ok(
            new EmployeeLoginCreatedResponse
            {
                EmployeeId   = employee.EmployeeId,
                FullName     = employee.FullName,
                LoginEmail   = loginEmail,
                TempPassword = tempPassword,
                Message      = "Login access granted. Share these credentials with the employee. " +
                               "They must change their password on first login."
            }, "Employee login created successfully."), HttpStatusCode.Created);
    }

    // ── POST /api/auth/employee-login ─────────────────────────────────────────
    // Employee logs in using their EmployeeId + password
    [Function("EmployeeLogin")]
    public async Task<HttpResponseData> EmployeeLogin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "auth/employee-login")] HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();
        var dto  = JsonSerializer.Deserialize<EmployeeLoginRequest>(body ?? "", JsonOpts);

        if (dto == null || string.IsNullOrWhiteSpace(dto.EmployeeId)
                        || string.IsNullOrWhiteSpace(dto.Password))
            return await Fail(req, HttpStatusCode.BadRequest,
                "Employee ID and password are required.");

        // Find employee
        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.EmployeeId == dto.EmployeeId.Trim().ToUpper());

        if (employee == null || !employee.IsActive || !employee.HasLoginAccess
                              || !employee.LinkedUserId.HasValue)
            return await Fail(req, HttpStatusCode.Unauthorized,
                "Invalid Employee ID or login access not granted. Contact your administrator.");

        // Check validity date
        if (employee.ValidUpto.HasValue &&
            employee.ValidUpto.Value < DateOnly.FromDateTime(DateTime.UtcNow))
            return await Fail(req, HttpStatusCode.Unauthorized,
                "Your employee ID has expired. Please contact the administrator.");

        // Find linked user
        var user = await _db.Users.FindAsync(employee.LinkedUserId.Value);
        if (user == null || user.Role != "Employee")
            return await Fail(req, HttpStatusCode.Unauthorized,
                "Login account not found. Contact your administrator.");

        // Verify password
        if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            return await Fail(req, HttpStatusCode.Unauthorized,
                "Incorrect password. Please try again.");

        // Update last login
        user.LastLoginAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        // Generate JWT — include employeeId claim
        var token  = _jwt.GenerateEmployeeToken(user, employee);
        var expiry = DateTime.UtcNow.AddHours(12);

        _log.LogInformation("Employee login: {EmpId}", employee.EmployeeId);

        return await OkJson(req, ApiResponse<EmployeeAuthResponse>.Ok(
            new EmployeeAuthResponse
            {
                Token              = token,
                FullName           = user.FullName,
                Email              = user.Email,
                Role               = user.Role,
                EmployeeId         = employee.EmployeeId,
                Designation        = employee.Designation,
                ImageUrl           = employee.ImageUrl,
                MustChangePassword = user.MustChangePassword,
                Expiry             = expiry
            }, "Login successful."));
    }

    // ── POST /api/auth/change-password  [Employee] ────────────────────────────
    [Function("ChangePassword")]
    public async Task<HttpResponseData> ChangePassword(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post",
            Route = "auth/change-password")] HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "Employee", "SuperAdmin", "Admin", "Reporter"))
            return await Fail(req, HttpStatusCode.Unauthorized, "Login required.");

        var userId = AuthHelper.GetUserId(principal);
        if (userId == null)
            return await Fail(req, HttpStatusCode.Unauthorized, "Invalid token.");

        var user = await _db.Users.FindAsync(userId.Value);
        if (user == null)
            return await Fail(req, HttpStatusCode.NotFound, "User not found.");

        var body = await req.ReadAsStringAsync();
        var dto  = JsonSerializer.Deserialize<ChangePasswordRequest>(body ?? "", JsonOpts);

        if (dto == null || string.IsNullOrWhiteSpace(dto.NewPassword))
            return await Fail(req, HttpStatusCode.BadRequest, "New password is required.");

        if (dto.NewPassword.Length < 8)
            return await Fail(req, HttpStatusCode.BadRequest,
                "New password must be at least 8 characters.");

        // Verify current password
        if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            return await Fail(req, HttpStatusCode.BadRequest,
                "Current password is incorrect.");

        user.PasswordHash       = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.MustChangePassword = false;
        await _db.SaveChangesAsync();

        _log.LogInformation("Password changed for UserId: {Id}", userId);
        return await OkJson(req, ApiResponse<object>.Ok(new { }, "Password changed successfully."));
    }

    // ── DELETE /api/employees/{id}/revoke-login  [SuperAdmin] ─────────────────
    [Function("RevokeEmployeeLogin")]
    public async Task<HttpResponseData> RevokeEmployeeLogin(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete",
            Route = "employees/{id:int}/revoke-login")] HttpRequestData req, int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin role required.");

        var employee = await _db.Employees.FindAsync(id);
        if (employee == null)
            return await Fail(req, HttpStatusCode.NotFound, "Employee not found.");

        if (employee.LinkedUserId.HasValue)
        {
            var user = await _db.Users.FindAsync(employee.LinkedUserId.Value);
            if (user != null)
            {
                _db.Users.Remove(user);
            }
        }

        employee.HasLoginAccess = false;
        employee.LinkedUserId   = null;
        employee.UpdatedAt      = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        _log.LogInformation("Employee login revoked: {EmpId}", employee.EmployeeId);
        return await OkJson(req, ApiResponse<object>.Ok(new { id }, "Login access revoked."));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    // Temp password format: EMP-XXXX@PKG (predictable so admin can share verbally)
    private static string GenerateTempPassword(string employeeId)
    {
        // e.g. EMP-2026-00001 → Emp2026#Pkg!
        var parts = employeeId.Split('-');
        var year  = parts.Length > 1 ? parts[1] : "2024";
        var num   = parts.Length > 2 ? parts[2] : "00001";
        return $"Pkg@{year}#{num}";
    }

    private static async Task<HttpResponseData> OkJson(HttpRequestData req, object data,
        HttpStatusCode code = HttpStatusCode.OK)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(data, JsonOpts));
        return res;
    }

    private static async Task<HttpResponseData> Fail(HttpRequestData req,
        HttpStatusCode code, string msg)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(
            ApiResponse<object>.Fail(msg), JsonOpts));
        return res;
    }
}
