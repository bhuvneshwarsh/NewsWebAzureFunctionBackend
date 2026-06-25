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

public class EmployeeFunction
{
    private readonly ApplicationDbContext      _db;
    private readonly IJwtService               _jwt;
    private readonly IBlobService              _blob;
    private readonly ILogger<EmployeeFunction> _log;

    private static readonly JsonSerializerOptions JsonOpts =
        new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, PropertyNameCaseInsensitive = true };

    public EmployeeFunction(ApplicationDbContext db, IJwtService jwt,
        IBlobService blob, ILogger<EmployeeFunction> log)
    {
        _db   = db;
        _jwt  = jwt;
        _blob = blob;
        _log  = log;
    }

    // ── GET /api/employees  (public — returns card list) ─────────────────────
    [Function("GetEmployees")]
    public async Task<HttpResponseData> GetEmployees(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "employees")] HttpRequestData req)
    {
        var qs       = System.Web.HttpUtility.ParseQueryString(req.Url.Query);
        var adminAll = bool.TryParse(qs["all"], out var a) && a;

        // Admin view: all employees; Public view: only active
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        var isAdmin   = AuthHelper.HasRole(principal, "SuperAdmin", "Admin");

        IQueryable<Employee> query = _db.Employees;

        if (!adminAll || !isAdmin)
            query = query.Where(e => e.IsActive);

        var employees = await query
            .OrderBy(e => e.DisplayOrder)
            .ThenBy(e => e.FullName)
            .ToListAsync();

        if (adminAll && isAdmin)
        {
            var adminList = employees.Select(e => MapToAdmin(e)).ToList();
            return await OkJson(req, ApiResponse<List<EmployeeAdminDto>>.Ok(adminList));
        }

        var cardList = employees.Select(e => MapToCard(e)).ToList();
        return await OkJson(req, ApiResponse<List<EmployeeCardDto>>.Ok(cardList));
    }

    // ── GET /api/employees/{employeeId}  (public — returns detail) ───────────
    [Function("GetEmployeeById")]
    public async Task<HttpResponseData> GetEmployeeById(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "employees/{employeeId}")] HttpRequestData req,
        string employeeId)
    {
        var employee = await _db.Employees
            .FirstOrDefaultAsync(e => e.EmployeeId == employeeId && e.IsActive);

        if (employee == null)
            return await Fail(req, HttpStatusCode.NotFound, "Employee not found.");

        return await OkJson(req, ApiResponse<EmployeeDetailDto>.Ok(MapToDetail(employee)));
    }

    // ── POST /api/employees  [SuperAdmin] ─────────────────────────────────────
    [Function("CreateEmployee")]
    public async Task<HttpResponseData> CreateEmployee(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "employees")] HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin role required.");

        var body = await req.ReadAsStringAsync();
        var dto  = JsonSerializer.Deserialize<CreateEmployeeRequest>(body ?? "", JsonOpts);

        if (dto == null || string.IsNullOrWhiteSpace(dto.FullName) || string.IsNullOrWhiteSpace(dto.Designation))
            return await Fail(req, HttpStatusCode.BadRequest, "Full name and designation are required.");

        // Generate unique EmployeeId: EMP-YYYY-NNNNN
        var year  = DateTime.UtcNow.Year;
        var count = await _db.Employees.CountAsync(e => e.CreatedAt.Year == year) + 1;
        var empId = $"EMP-{year}-{count:D5}";

        var employee = new Employee
        {
            EmployeeId   = empId,
            FullName     = dto.FullName.Trim(),
            Designation  = dto.Designation.Trim(),
            Email        = dto.Email?.Trim(),
            Mobile       = dto.Mobile?.Trim(),
            Address      = dto.Address?.Trim(),
            GovtIdNumber = dto.GovtIdNumber?.Trim(),
            GovtIdType   = dto.GovtIdType?.Trim(),
            ImageUrl     = dto.ImageUrl?.Trim(),
            DisplayOrder = dto.DisplayOrder,
            DateOfBirth  = ParseDate(dto.DateOfBirth),
            ValidUpto    = ParseDate(dto.ValidUpto),
            IsActive     = true,
            CreatedAt    = DateTime.UtcNow,
            UpdatedAt    = DateTime.UtcNow
        };

        _db.Employees.Add(employee);
        await _db.SaveChangesAsync();

        _log.LogInformation("Employee created: {EmpId} — {Name}", empId, employee.FullName);
        return await Created(req, ApiResponse<EmployeeAdminDto>.Ok(MapToAdmin(employee), "Employee created successfully."));
    }

    // ── PUT /api/employees/{id:int}  [SuperAdmin] ─────────────────────────────
    [Function("UpdateEmployee")]
    public async Task<HttpResponseData> UpdateEmployee(
        [HttpTrigger(AuthorizationLevel.Anonymous, "put", Route = "employees/{id:int}")] HttpRequestData req,
        int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin role required.");

        var employee = await _db.Employees.FindAsync(id);
        if (employee == null)
            return await Fail(req, HttpStatusCode.NotFound, "Employee not found.");

        var body = await req.ReadAsStringAsync();
        var dto  = JsonSerializer.Deserialize<UpdateEmployeeRequest>(body ?? "", JsonOpts);
        if (dto == null)
            return await Fail(req, HttpStatusCode.BadRequest, "Invalid request.");

        employee.FullName     = dto.FullName.Trim();
        employee.Designation  = dto.Designation.Trim();
        employee.Email        = dto.Email?.Trim();
        employee.Mobile       = dto.Mobile?.Trim();
        employee.Address      = dto.Address?.Trim();
        employee.GovtIdNumber = dto.GovtIdNumber?.Trim();
        employee.GovtIdType   = dto.GovtIdType?.Trim();
        employee.DisplayOrder = dto.DisplayOrder;
        employee.IsActive     = dto.IsActive;
        employee.DateOfBirth  = ParseDate(dto.DateOfBirth);
        employee.ValidUpto    = ParseDate(dto.ValidUpto);
        employee.UpdatedAt    = DateTime.UtcNow;

        if (!string.IsNullOrEmpty(dto.ImageUrl))
            employee.ImageUrl = dto.ImageUrl.Trim();

        await _db.SaveChangesAsync();

        return await OkJson(req, ApiResponse<EmployeeAdminDto>.Ok(MapToAdmin(employee), "Employee updated."));
    }

    // ── DELETE /api/employees/{id:int}  [SuperAdmin] ─────────────────────────
    [Function("DeleteEmployee")]
    public async Task<HttpResponseData> DeleteEmployee(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "employees/{id:int}")] HttpRequestData req,
        int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin role required.");

        var employee = await _db.Employees.FindAsync(id);
        if (employee == null)
            return await Fail(req, HttpStatusCode.NotFound, "Employee not found.");

        // Soft delete — keeps record for audit
        employee.IsActive  = false;
        employee.UpdatedAt = DateTime.UtcNow;
        await _db.SaveChangesAsync();

        return await OkJson(req, ApiResponse<object>.Ok(new { id }, "Employee removed from team."));
    }

    // ── Hard delete [SuperAdmin] ──────────────────────────────────────────────
    [Function("HardDeleteEmployee")]
    public async Task<HttpResponseData> HardDeleteEmployee(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "employees/{id:int}/hard")] HttpRequestData req,
        int id)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Unauthorized, "SuperAdmin role required.");

        var employee = await _db.Employees.FindAsync(id);
        if (employee == null)
            return await Fail(req, HttpStatusCode.NotFound, "Employee not found.");

        if (!string.IsNullOrEmpty(employee.ImageUrl))
            await _blob.DeleteAsync(employee.ImageUrl);

        _db.Employees.Remove(employee);
        await _db.SaveChangesAsync();

        return await OkJson(req, ApiResponse<object>.Ok(new { id }, "Employee permanently deleted."));
    }

    // ── Mappers ───────────────────────────────────────────────────────────────
    private static EmployeeCardDto MapToCard(Employee e) => new()
    {
        Id          = e.Id,
        EmployeeId  = e.EmployeeId,
        FullName    = e.FullName,
        Designation = e.Designation,
        ImageUrl    = e.ImageUrl
    };

    private static EmployeeDetailDto MapToDetail(Employee e) => new()
    {
        Id          = e.Id,
        EmployeeId  = e.EmployeeId,
        FullName    = e.FullName,
        Designation = e.Designation,
        ImageUrl    = e.ImageUrl,
        Email       = e.Email,
        ValidUpto   = e.ValidUpto?.ToString("yyyy-MM-dd")
    };

    private static EmployeeAdminDto MapToAdmin(Employee e) => new()
    {
        Id           = e.Id,
        EmployeeId   = e.EmployeeId,
        FullName     = e.FullName,
        Designation  = e.Designation,
        ImageUrl     = e.ImageUrl,
        Email        = e.Email,
        Mobile       = e.Mobile,
        Address      = e.Address,
        GovtIdType   = e.GovtIdType,
        GovtIdNumber = e.GovtIdNumber,
        DateOfBirth  = e.DateOfBirth?.ToString("yyyy-MM-dd"),
        ValidUpto    = e.ValidUpto?.ToString("yyyy-MM-dd"),
        IsActive     = e.IsActive,
        DisplayOrder = e.DisplayOrder,
        CreatedAt    = e.CreatedAt.ToString("yyyy-MM-dd")
    };

    private static DateOnly? ParseDate(string? s) =>
        DateOnly.TryParse(s, out var d) ? d : null;

    // ── HTTP helpers ──────────────────────────────────────────────────────────
    private static async Task<HttpResponseData> OkJson(HttpRequestData req, object data)
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(data, JsonOpts));
        return res;
    }

    private static async Task<HttpResponseData> Created(HttpRequestData req, object data)
    {
        var res = req.CreateResponse(HttpStatusCode.Created);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(data, JsonOpts));
        return res;
    }

    private static async Task<HttpResponseData> Fail(HttpRequestData req, HttpStatusCode code, string msg)
    {
        var res = req.CreateResponse(code);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(ApiResponse<object>.Fail(msg), JsonOpts));
        return res;
    }
}
