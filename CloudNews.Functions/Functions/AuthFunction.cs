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

public class AuthFunction
{
    private readonly ApplicationDbContext _db;
    private readonly IJwtService          _jwt;
    private readonly ILogger<AuthFunction> _log;

    public AuthFunction(ApplicationDbContext db, IJwtService jwt, ILogger<AuthFunction> log)
    {
        _db  = db;
        _jwt = jwt;
        _log = log;
    }

    // ── POST /api/auth/login ──────────────────────────────────────────────────
    [Function("Login")]
    public async Task<HttpResponseData> Login(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/login")] HttpRequestData req)
    {
        try
        {
            var body = await req.ReadAsStringAsync();
            var dto  = JsonSerializer.Deserialize<LoginRequest>(body ?? "",
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (dto == null || string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.Password))
                return await BadRequest(req, "Email and password are required.");

            var user = await _db.Users
                .FirstOrDefaultAsync(u => u.Email == dto.Email.ToLower().Trim());

            if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
                return await Unauthorized(req, "Invalid email or password.");

            var token  = _jwt.GenerateToken(user);
            var expiry = DateTime.UtcNow.AddHours(24);

            var response = ApiResponse<AuthResponse>.Ok(new AuthResponse
            {
                Token    = token,
                FullName = user.FullName,
                Email    = user.Email,
                Role     = user.Role,
                Expiry   = expiry
            }, "Login successful");

            return await Ok(req, response);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Login error");
            return await ServerError(req, "An error occurred during login.");
        }
    }

    // ── POST /api/auth/register  (public user sign-up) ───────────────────────
    [Function("Register")]
    public async Task<HttpResponseData> Register(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "auth/register")] HttpRequestData req)
    {
        try
        {
            var body = await req.ReadAsStringAsync();
            var dto  = JsonSerializer.Deserialize<RegisterRequest>(body ?? "",
                           new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (dto == null || string.IsNullOrEmpty(dto.Email) || string.IsNullOrEmpty(dto.Password))
                return await BadRequest(req, "Name, email and password are required.");

            var email = dto.Email.ToLower().Trim();
            if (await _db.Users.AnyAsync(u => u.Email == email))
                return await BadRequest(req, "An account with this email already exists.");

            var user = new User
            {
                FullName     = dto.FullName.Trim(),
                Email        = email,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
                Role         = "User",
                CreatedAt    = DateTime.UtcNow
            };

            _db.Users.Add(user);
            await _db.SaveChangesAsync();

            var token = _jwt.GenerateToken(user);

            return await Ok(req, ApiResponse<AuthResponse>.Ok(new AuthResponse
            {
                Token    = token,
                FullName = user.FullName,
                Email    = user.Email,
                Role     = user.Role,
                Expiry   = DateTime.UtcNow.AddHours(24)
            }, "Registration successful"));
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Register error");
            return await ServerError(req, "An error occurred during registration.");
        }
    }

    // ── Helper methods ────────────────────────────────────────────────────────

    private static async Task<HttpResponseData> Ok(HttpRequestData req, object data)
    {
        var res = req.CreateResponse(HttpStatusCode.OK);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(data));
        return res;
    }

    private static async Task<HttpResponseData> BadRequest(HttpRequestData req, string message)
    {
        var res = req.CreateResponse(HttpStatusCode.BadRequest);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(
            ApiResponse<object>.Fail(message)));
        return res;
    }

    private static async Task<HttpResponseData> Unauthorized(HttpRequestData req, string message)
    {
        var res = req.CreateResponse(HttpStatusCode.Unauthorized);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(
            ApiResponse<object>.Fail(message)));
        return res;
    }

    private static async Task<HttpResponseData> ServerError(HttpRequestData req, string message)
    {
        var res = req.CreateResponse(HttpStatusCode.InternalServerError);
        res.Headers.Add("Content-Type", "application/json");
        await res.WriteStringAsync(JsonSerializer.Serialize(
            ApiResponse<object>.Fail(message)));
        return res;
    }
}
