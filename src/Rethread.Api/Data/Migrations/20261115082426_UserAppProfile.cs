using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rethread.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserAppProfile : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // The user app's age groups are adult, teen and kid.
            migrationBuilder.Sql("UPDATE family_members SET age_group = 'kid' WHERE age_group IN ('toddler', 'child');");
            migrationBuilder.Sql("UPDATE family_members SET age_group = 'adult' WHERE age_group = 'senior';");

            migrationBuilder.AddColumn<string>(
                name: "purpose",
                table: "otp_challenges",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "icon",
                table: "garment_types",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "size_basis",
                table: "garment_types",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<DateOnly>(
                name: "date_of_birth",
                table: "family_members",
                type: "date",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "avatar_file_id",
                table: "customers",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at",
                table: "customers",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "city",
                table: "addresses",
                type: "character varying(80)",
                maxLength: 80,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "custom_label",
                table: "addresses",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "landmark",
                table: "addresses",
                type: "character varying(120)",
                maxLength: 120,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "notes",
                table: "addresses",
                type: "character varying(300)",
                maxLength: 300,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "unit_no",
                table: "addresses",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "purpose",
                table: "otp_challenges");

            migrationBuilder.DropColumn(
                name: "icon",
                table: "garment_types");

            migrationBuilder.DropColumn(
                name: "size_basis",
                table: "garment_types");

            migrationBuilder.DropColumn(
                name: "date_of_birth",
                table: "family_members");

            migrationBuilder.DropColumn(
                name: "avatar_file_id",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "deleted_at",
                table: "customers");

            migrationBuilder.DropColumn(
                name: "city",
                table: "addresses");

            migrationBuilder.DropColumn(
                name: "custom_label",
                table: "addresses");

            migrationBuilder.DropColumn(
                name: "landmark",
                table: "addresses");

            migrationBuilder.DropColumn(
                name: "notes",
                table: "addresses");

            migrationBuilder.DropColumn(
                name: "unit_no",
                table: "addresses");
        }
    }
}
