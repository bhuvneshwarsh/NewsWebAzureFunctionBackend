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
    public DbSet<Employee> Employees  { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ── Users ──────────────────────────────────────────────────────────
        modelBuilder.Entity<User>(e =>
        {
            e.HasKey(u => u.Id);
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Role).HasDefaultValue("User");
            e.Property(u => u.MustChangePassword).HasDefaultValue(false);
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

        // ── Employees ──────────────────────────────────────────────────────
        modelBuilder.Entity<Employee>(e =>
        {
            e.HasKey(emp => emp.Id);
            e.HasIndex(emp => emp.EmployeeId).IsUnique();
            e.Property(emp => emp.IsActive).HasDefaultValue(true);
            e.Property(emp => emp.DisplayOrder).HasDefaultValue(0);
            e.Property(emp => emp.HasLoginAccess).HasDefaultValue(false);
        });

        // ── Seed: default categories ───────────────────────────────────────
        modelBuilder.Entity<Category>().HasData(
            new Category { Id = 1,  Name = "Politics",      Slug = "politics"      },
            new Category { Id = 2,  Name = "Sports",        Slug = "sports"        },
            new Category { Id = 3,  Name = "Business",      Slug = "business"      },
            new Category { Id = 4,  Name = "Tech",          Slug = "tech"          },
            new Category { Id = 5,  Name = "World",         Slug = "world"         },
            new Category { Id = 6,  Name = "Entertainment", Slug = "entertainment" },
            new Category { Id = 7,  Name = "Health",        Slug = "health"        },
            new Category { Id = 8,  Name = "India",         Slug = "india"         },
            new Category { Id = 9,  Name = "Lifestyle",     Slug = "lifestyle"     },
            new Category { Id = 10, Name = "Opinion",       Slug = "opinion"       },
            new Category { Id = 11, Name = "Science",       Slug = "science"       },
            new Category { Id = 12, Name = "Spiritual",     Slug = "spiritual"     }
        );
    }
}