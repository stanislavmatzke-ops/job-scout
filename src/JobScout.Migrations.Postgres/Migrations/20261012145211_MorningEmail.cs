using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobScout.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class MorningEmail : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "MorningEmailOff",
                table: "Users",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "MorningEmailOff",
                table: "Users");
        }
    }
}
