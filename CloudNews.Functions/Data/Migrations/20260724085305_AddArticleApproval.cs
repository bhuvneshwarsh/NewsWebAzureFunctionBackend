using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace CloudNews.Functions.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddArticleApproval : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Articles",
                type: "nvarchar(600)",
                maxLength: 600,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(500)",
                oldMaxLength: 500);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalNote",
                table: "Articles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovalStatus",
                table: "Articles",
                type: "nvarchar(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "Articles",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ApprovedById",
                table: "Articles",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_Articles_ApprovedById",
                table: "Articles",
                column: "ApprovedById");

            migrationBuilder.AddForeignKey(
                name: "FK_Articles_Users_ApprovedById",
                table: "Articles",
                column: "ApprovedById",
                principalTable: "Users",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Articles_Users_ApprovedById",
                table: "Articles");

            migrationBuilder.DropIndex(
                name: "IX_Articles_ApprovedById",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "ApprovalNote",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "ApprovalStatus",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "Articles");

            migrationBuilder.DropColumn(
                name: "ApprovedById",
                table: "Articles");

            migrationBuilder.AlterColumn<string>(
                name: "Slug",
                table: "Articles",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(600)",
                oldMaxLength: 600);
        }
    }
}
