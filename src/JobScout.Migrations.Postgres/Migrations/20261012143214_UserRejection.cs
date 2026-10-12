using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobScout.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class UserRejection : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "RejectedAt",
                table: "Users",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedReason",
                table: "Users",
                type: "character varying(500)",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "Users");

            migrationBuilder.DropColumn(
                name: "RejectedReason",
                table: "Users");
        }
    }
}
