using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace JobScout.Migrations.Postgres.Migrations
{
    /// <inheritdoc />
    public partial class ManualJobAds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "OwnerUserId",
                table: "JobOffers",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "AddedManually",
                table: "JobMatches",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateIndex(
                name: "IX_JobOffers_OwnerUserId",
                table: "JobOffers",
                column: "OwnerUserId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_JobOffers_OwnerUserId",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "OwnerUserId",
                table: "JobOffers");

            migrationBuilder.DropColumn(
                name: "AddedManually",
                table: "JobMatches");
        }
    }
}
