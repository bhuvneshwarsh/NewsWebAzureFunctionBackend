using CloudNews.Functions.Models;
using Microsoft.EntityFrameworkCore;

namespace CloudNews.Functions.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options) { }

    public DbSet<User>     Users      { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<Article>  Articles   { get; set; }
    public DbSet<EPaper>   EPapers    { get; set; }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            // Read from the live environment variables when running on Azure
            var connectionString = Environment.GetEnvironmentVariable("SqlConnectionString");
            optionsBuilder.UseSqlServer(connectionString);
        }
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Users ──────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Role)
             .HasDefaultValue("User");
        });

        // ── Categories ─────────────────────────────────────────────────────
        modelBuilder.Entity<Category>(e =>
        {
            e.HasKey(c => c.Id);
            e.HasIndex(c => c.Slug).IsUnique();
        });

        // ── Articles ───────────────────────────────────────────────────────
        modelBuilder.Entity<Article>(e =>
        {
            e.HasKey(a => a.Id);
            e.HasIndex(a => a.Slug).IsUnique();
            e.Property(a => a.Views).HasDefaultValue(0);
            e.Property(a => a.IsPublished).HasDefaultValue(false);

            e.HasOne(a => a.Category)
             .WithMany(c => c.Articles)
             .HasForeignKey(a => a.CategoryId)
             .OnDelete(DeleteBehavior.Restrict);

            e.HasOne(a => a.Author)
             .WithMany(u => u.Articles)
             .HasForeignKey(a => a.AuthorId)
             .OnDelete(DeleteBehavior.Restrict);
        });

        // ── EPapers ────────────────────────────────────────────────────────
        modelBuilder.Entity<EPaper>(e =>
        {
            e.HasKey(p => p.Id);
            e.HasIndex(p => p.Date).IsUnique();
        });

        // ── Seed: default categories ───────────────────────────────────────
        modelBuilder.Entity<Category>().HasData(
            new Category { Id = 1, Name = "Politics", Slug = "politics" },
            new Category { Id = 2, Name = "Sports",   Slug = "sports"   },
            new Category { Id = 3, Name = "Business", Slug = "business" },
            new Category { Id = 4, Name = "Tech",     Slug = "tech"     },
            new Category { Id = 5, Name = "World",    Slug = "world"    }
        );

        // NOTE: SuperAdmin is seeded via SQL script (see README) so the
        // password hash is set at deployment time, not hardcoded here.
    }

public class ApplicationDbContextFactory : Microsoft.EntityFrameworkCore.Design.IDesignTimeDbContextFactory<ApplicationDbContext>
{
    public ApplicationDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<ApplicationDbContext>();
        
        // Find the absolute path to your local.settings.json file cleanly
        string baseDir = AppDomain.CurrentDomain.BaseDirectory;
        string configPath = Path.Combine(baseDir, "local.settings.json");

        // Climb up out of compiled folders if needed to find your project root
        while (!File.Exists(configPath) && Directory.GetParent(baseDir) != null)
        {
            baseDir = Directory.GetParent(baseDir)!.FullName;
            configPath = Path.Combine(baseDir, "local.settings.json");
        }

        string connectionString = string.Empty;

        if (File.Exists(configPath))
        {
            var json = File.ReadAllText(configPath);
            using (var doc = System.Text.Json.JsonDocument.Parse(json))
            {
                if (doc.RootElement.TryGetProperty("Values", out var values) &&
                    values.TryGetProperty("SqlConnectionString", out var connProp))
                {
                    connectionString = connProp.GetString() ?? string.Empty;
                }
            }
        }

        optionsBuilder.UseSqlServer(connectionString);
        return new ApplicationDbContext(optionsBuilder.Options);
    }
}

}
