using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Azure.Functions.Worker.Middleware;
using Microsoft.Extensions.Configuration;
using System.Net;

namespace CloudNews.Functions.Middleware;

/// <summary>
/// CORS middleware for .NET 8 Isolated Azure Functions.
/// Handles OPTIONS preflight and injects Access-Control headers on every response.
/// </summary>
public class CorsMiddleware : IFunctionsWorkerMiddleware
{
    // ── List of allowed origins ───────────────────────────────────────────────
    // Add your production domain here (e.g. https://prajatantrkigunj.com)
    private static readonly HashSet<string> AllowedOrigins = new(StringComparer.OrdinalIgnoreCase)
    {
        "http://localhost:3000",
        "http://localhost:5173",
        "https://prajatantrkigunj.com",
        "https://www.prajatantrkigunj.com",
        // Azure Static Web Apps default domain — replace with yours:
        "https://prajatantrkigunj.azurestaticapps.net",
    };

    public async Task Invoke(FunctionContext context, FunctionExecutionDelegate next)
    {
        var requestData = await context.GetHttpRequestDataAsync();
        if (requestData == null)
        {
            await next(context);
            return;
        }

        // Determine the requesting origin
        requestData.Headers.TryGetValues("Origin", out var originValues);
        var origin = originValues?.FirstOrDefault() ?? "";

        // Use the specific origin if allowed, otherwise use the first allowed one
        var responseOrigin = AllowedOrigins.Contains(origin) ? origin : AllowedOrigins.First();

        // ── Handle OPTIONS preflight immediately ──────────────────────────────
        if (requestData.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            var preflight = requestData.CreateResponse(HttpStatusCode.NoContent);
            AddCorsHeaders(preflight, responseOrigin);
            context.GetInvocationResult().Value = preflight;
            return;   // skip the actual function
        }

        // ── Run the actual function ───────────────────────────────────────────
        await next(context);

        // ── Inject CORS headers into the real response ────────────────────────
        var result = context.GetInvocationResult();
        if (result.Value is HttpResponseData response)
        {
            AddCorsHeaders(response, responseOrigin);
        }
    }

    private static void AddCorsHeaders(HttpResponseData response, string origin)
    {
        // Remove any existing CORS headers first to avoid duplicates
        response.Headers.Remove("Access-Control-Allow-Origin");
        response.Headers.Remove("Access-Control-Allow-Methods");
        response.Headers.Remove("Access-Control-Allow-Headers");
        response.Headers.Remove("Access-Control-Allow-Credentials");
        response.Headers.Remove("Access-Control-Max-Age");

        response.Headers.Add("Access-Control-Allow-Origin",      origin);
        response.Headers.Add("Access-Control-Allow-Methods",     "GET, POST, PUT, DELETE, OPTIONS");
        response.Headers.Add("Access-Control-Allow-Headers",     "Content-Type, Authorization, Accept");
        response.Headers.Add("Access-Control-Allow-Credentials", "true");
        response.Headers.Add("Access-Control-Max-Age",           "86400"); // cache preflight 24h
    }
}