using CloudNews.Functions.Data;
using CloudNews.Functions.Services;
using Microsoft.Azure.Functions.Worker;
// Removed: Microsoft.Azure.Functions.Worker.Builder is not required or may not exist in this SDK version
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
        services.AddApplicationInsightsTelemetryWorkerService();
        services.ConfigureFunctionsApplicationInsights();
    })
    .Build();

// ── Auto-migrate on startup ───────────────────────────────────────────────────
using (var scope = host.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    await db.Database.MigrateAsync();
}

await host.RunAsync();