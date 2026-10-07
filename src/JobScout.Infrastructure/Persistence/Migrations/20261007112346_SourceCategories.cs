using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class SourceCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SourceCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Key = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    Label = table.Column<string>(type: "TEXT", maxLength: 200, nullable: false),
                    CategoryId = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    CheckedAt = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SourceCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SourceCategories_Source_Key",
                table: "SourceCategories",
                columns: new[] { "Source", "Key" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SourceCategories");
        }
    }
}
