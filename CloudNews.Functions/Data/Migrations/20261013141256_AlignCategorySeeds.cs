using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace CloudNews.Functions.Data.Migrations
{
    /// <inheritdoc />
    public partial class AlignCategorySeeds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                UPDATE [dbo].[Categories] SET [Name] = N'Sci-Tech' WHERE [Id] = 4;
                UPDATE [dbo].[Categories] SET [Name] = N'Health', [Slug] = N'health' WHERE [Id] = 8;
                UPDATE [dbo].[Categories] SET [Name] = N'Spiritual', [Slug] = N'spiritual' WHERE [Id] = 11;
                UPDATE [dbo].[Categories] SET [Name] = N'India', [Slug] = N'india' WHERE [Id] = 12;

                SET IDENTITY_INSERT [dbo].[Categories] ON;

                INSERT INTO [dbo].[Categories] ([Id], [Name], [Slug])
                SELECT seed.[Id], seed.[Name], seed.[Slug]
                FROM (VALUES
                    (13, N'State News', N'state-news'),
                    (14, N'Local News', N'local'),
                    (15, N'Crime', N'crime'),
                    (16, N'Education', N'education'),
                    (17, N'Lifestyle', N'lifestyle'),
                    (18, N'Law & Order', N'law-n-order'),
                    (19, N'Government', N'govt'),
                    (20, N'Editorial', N'editorial')
                ) AS seed ([Id], [Name], [Slug])
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM [dbo].[Categories] AS existing
                    WHERE existing.[Id] = seed.[Id]
                );

                SET IDENTITY_INSERT [dbo].[Categories] OFF;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // These category rows are shared data and may predate this migration.
        }
    }
}
