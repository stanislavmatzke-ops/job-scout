using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobScout.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class UserAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Access",
                table: "Users",
                type: "TEXT",
                maxLength: 20,
                nullable: false,
                defaultValue: "Approved");

            migrationBuilder.AddColumn<string>(
                name: "AccessRequestMessage",
                table: "Users",
                type: "TEXT",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "AccessRequestedAt",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "ApprovedAt",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "LastSignInAt",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "SuspendedAt",
                table: "Users",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "SuspendedReason",
                table: "Users",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Access",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AccessRequestMessage",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "AccessRequestedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "LastSignInAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SuspendedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "SuspendedReason",
                table: "Users");
        }
    }
}
