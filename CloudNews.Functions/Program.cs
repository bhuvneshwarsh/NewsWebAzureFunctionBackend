using CloudNews.Functions.Data;
using CloudNews.Functions.Services;
using Microsoft.Azure.Functions.Worker;
// Removed: Microsoft.Azure.Functions.Worker.Builder is not required or may not exist in this SDK version
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var host = new HostBuilder()
    .ConfigureFunctionsWebApplication(worker =>
    {
        // ── CORS middleware — must be FIRST ───────────────────────────────
        worker.UseMiddleware<CloudNews.Functions.Middleware.CorsMiddleware>();
    })
    .ConfigureAppConfiguration((context, config) =>
    {
        config
            .AddJsonFile("local.settings.json", optional: true, reloadOnChange: false)
            .AddEnvironmentVariables();
    })
    .ConfigureServices((context, services) =>
    {
        var config = context.Configuration;

        // ── EF Core → Azure SQL ───────────────────────────────────────────
        services.AddDbContext<ApplicationDbContext>(options =>
            options.UseSqlServer(
                config["SqlConnectionString"],
                sqlOptions => sqlOptions.EnableRetryOnFailure(
                    maxRetryCount: 3,
                    maxRetryDelay: TimeSpan.FromSeconds(5),
                    errorNumbersToAdd: null
                )
            )
        );

        // ── Services ──────────────────────────────────────────────────────
        services.AddSingleton<IJwtService, JwtService>();
        services.AddSingleton<IBlobService, BlobService>();

        // ── Application Insights ──────────────────────────────────────────
        // Note: Only enable if APPINSIGHTS_INSTRUMENTATIONKEY is set in production
        var appInsightsKey = config["APPINSIGHTS_INSTRUMENTATIONKEY"];
        if (!string.IsNullOrWhiteSpace(appInsightsKey))
        {
            services.AddApplicationInsightsTelemetryWorkerService();
            services.ConfigureFunctionsApplicationInsights();
        }
    })
    .Build();

// ── Auto-migrate on startup with error handling ───────────────────────────────
try
{
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Starting database migration...");
    
    using (var scope = host.Services.CreateScope())
    {
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        db.Database.SetCommandTimeout(TimeSpan.FromSeconds(30));
        await db.Database.MigrateAsync();
        logger.LogInformation("Database migration completed successfully.");
    }
}
catch (Exception ex)
{
    var logger = host.Services.GetRequiredService<ILogger<Program>>();
    logger.LogError(ex, "Database migration failed. Continuing startup anyway...");
    // Don't rethrow - allow startup to proceed even if migration fails
    // This prevents the entire function app from crashing due to DB connection issues
}

await host.RunAsync();