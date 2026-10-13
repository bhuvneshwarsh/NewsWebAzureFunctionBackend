using System.ComponentModel.DataAnnotations;
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

public class ContactMessageFunction
{
    private readonly ApplicationDbContext _db;
    private readonly IJwtService _jwt;
    private readonly ILogger<ContactMessageFunction> _log;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public ContactMessageFunction(
        ApplicationDbContext db,
        IJwtService jwt,
        ILogger<ContactMessageFunction> log)
    {
        _db = db;
        _jwt = jwt;
        _log = log;
    }

    [Function("ContactMessages")]
    public async Task<HttpResponseData> HandleContactMessages(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", "post", "options",
            Route = "contact-messages")] HttpRequestData req)
    {
        if (req.Method.Equals("OPTIONS", StringComparison.OrdinalIgnoreCase))
            return req.CreateResponse(HttpStatusCode.NoContent);

        if (req.Method.Equals("POST", StringComparison.OrdinalIgnoreCase))
            return await Submit(req);

        return await List(req);
    }

    private async Task<HttpResponseData> Submit(HttpRequestData req)
    {
        var body = await req.ReadAsStringAsync();
        CreateContactMessageRequest? request;
        try
        {
            request = JsonSerializer.Deserialize<CreateContactMessageRequest>(body ?? string.Empty, JsonOpts);
        }
        catch (JsonException)
        {
            return await Fail(req, HttpStatusCode.BadRequest, "Request body must be valid JSON.");
        }

        if (request == null)
            return await Fail(req, HttpStatusCode.BadRequest, "Name, email, subject, and message are required.");

        if (string.IsNullOrWhiteSpace(request.Name)
            || string.IsNullOrWhiteSpace(request.Email)
            || string.IsNullOrWhiteSpace(request.Subject)
            || string.IsNullOrWhiteSpace(request.Message))
        {
            return await Fail(req, HttpStatusCode.BadRequest,
                "Name, email, subject, and message are required.");
        }

        request.Name = request.Name.Trim();
        request.Email = request.Email.Trim().ToLowerInvariant();
        request.Subject = request.Subject.Trim();
        request.Message = request.Message.Trim();

        var validationResults = new List<ValidationResult>();
        if (!Validator.TryValidateObject(
                request, new ValidationContext(request), validationResults, validateAllProperties: true))
        {
            var validationMessage = string.Join(" ", validationResults.Select(result => result.ErrorMessage));
            return await Fail(req, HttpStatusCode.BadRequest, validationMessage);
        }

        var message = new ContactMessage
        {
            Name = request.Name,
            Email = request.Email,
            Subject = request.Subject,
            Message = request.Message,
            SubmittedAt = DateTime.UtcNow
        };

        _db.ContactMessages.Add(message);
        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _log.LogError(ex, "Failed to save contact message.");
            return await Fail(req, HttpStatusCode.InternalServerError, "Unable to submit your message.");
        }

        return await OkJson(req, ApiResponse<object>.Ok(
            new { id = message.Id }, "Contact message submitted."));
    }

    private async Task<HttpResponseData> List(HttpRequestData req)
    {
        var principal = AuthHelper.GetPrincipal(req, _jwt);
        if (principal == null)
            return await Fail(req, HttpStatusCode.Unauthorized, "A valid login is required.");

        if (!AuthHelper.HasRole(principal, "SuperAdmin"))
            return await Fail(req, HttpStatusCode.Forbidden, "SuperAdmin role required.");

        var messages = await _db.ContactMessages
            .OrderByDescending(message => message.SubmittedAt)
            .ToListAsync();

        return await OkJson(req, ApiResponse<List<ContactMessage>>.Ok(
            messages, "Contact messages loaded."));
    }

    private static async Task<HttpResponseData> OkJson(HttpRequestData req, object data)
    {
        var response = req.CreateResponse(HttpStatusCode.OK);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(data, JsonOpts));
        return response;
    }

    private static async Task<HttpResponseData> Fail(
        HttpRequestData req, HttpStatusCode statusCode, string message)
    {
        var response = req.CreateResponse(statusCode);
        response.Headers.Add("Content-Type", "application/json");
        await response.WriteStringAsync(JsonSerializer.Serialize(
            ApiResponse<object>.Fail(message), JsonOpts));
        return response;
    }
}
