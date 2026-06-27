using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using CloudNews.Functions.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;

namespace CloudNews.Functions.Services;

public interface IJwtService
{
    string GenerateToken(User user);
    string GenerateEmployeeToken(User user, Employee employee);
    ClaimsPrincipal? ValidateToken(string token);
}

public class JwtService : IJwtService
{
    private readonly string _secret;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int    _expiryHours;

    public JwtService(IConfiguration config)
    {
        _secret      = config["JwtSecret"]
            ?? throw new InvalidOperationException("JwtSecret not configured");
        _issuer      = config["JwtIssuer"]      ?? "CloudNewsAPI";
        _audience    = config["JwtAudience"]    ?? "CloudNewsClient";
        _expiryHours = int.TryParse(config["JwtExpiryHours"], out var h) ? h : 24;
    }

    // ── Standard token (SuperAdmin, Admin, Reporter, User) ───────────────────
    public string GenerateToken(User user)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email,          user.Email),
            new Claim(ClaimTypes.Name,           user.FullName),
            new Claim(ClaimTypes.Role,           user.Role),
            new Claim("userId",                  user.Id.ToString()),
        };
        return BuildToken(claims, _expiryHours);
    }

    // ── Employee token — includes employeeId claim, shorter expiry (12h) ─────
    public string GenerateEmployeeToken(User user, Employee employee)
    {
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new Claim(ClaimTypes.Email,          user.Email),
            new Claim(ClaimTypes.Name,           user.FullName),
            new Claim(ClaimTypes.Role,           "Employee"),
            new Claim("userId",                  user.Id.ToString()),
            new Claim("employeeId",              employee.EmployeeId),
            new Claim("designation",             employee.Designation),
            new Claim("mustChangePassword",      user.MustChangePassword.ToString().ToLower()),
        };
        return BuildToken(claims, 12);  // 12-hour session for employees
    }

    // ── Validate any token ────────────────────────────────────────────────────
    public ClaimsPrincipal? ValidateToken(string token)
    {
        try
        {
            var key     = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
            var handler = new JwtSecurityTokenHandler();

            return handler.ValidateToken(token, new TokenValidationParameters
            {
                ValidateIssuerSigningKey = true,
                IssuerSigningKey         = key,
                ValidateIssuer           = true,
                ValidIssuer              = _issuer,
                ValidateAudience         = true,
                ValidAudience            = _audience,
                ValidateLifetime         = true,
                ClockSkew                = TimeSpan.Zero
            }, out _);
        }
        catch { return null; }
    }

    // ── Private builder ───────────────────────────────────────────────────────
    private string BuildToken(Claim[] claims, int expiryHours)
    {
        var key   = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_secret));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var token = new JwtSecurityToken(
            issuer:             _issuer,
            audience:           _audience,
            claims:             claims,
            expires:            DateTime.UtcNow.AddHours(expiryHours),
            signingCredentials: creds
        );
        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
