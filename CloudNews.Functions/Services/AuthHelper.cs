using System.Security.Claims;
using Microsoft.Azure.Functions.Worker.Http;

namespace CloudNews.Functions.Services;

public static class AuthHelper
{
    /// <summary>
    /// Extracts the Bearer token from the Authorization header and validates it.
    /// Returns the ClaimsPrincipal on success, null on failure.
    /// </summary>
    public static ClaimsPrincipal? GetPrincipal(HttpRequestData req, IJwtService jwtService)
    {
        if (!req.Headers.TryGetValues("Authorization", out var values))
            return null;

        var authHeader = values.FirstOrDefault();
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
            return null;

        var token = authHeader["Bearer ".Length..].Trim();
        return jwtService.ValidateToken(token);
    }

    /// <summary>
    /// Returns true if the principal has one of the allowed roles.
    /// </summary>
    public static bool HasRole(ClaimsPrincipal? principal, params string[] allowedRoles)
    {
        if (principal == null) return false;
        return allowedRoles.Any(role => principal.IsInRole(role));
    }

    /// <summary>
    /// Gets the userId claim value from the principal.
    /// </summary>
    public static int? GetUserId(ClaimsPrincipal? principal)
    {
        var claim = principal?.FindFirst("userId")?.Value;
        return int.TryParse(claim, out var id) ? id : null;
    }
}
