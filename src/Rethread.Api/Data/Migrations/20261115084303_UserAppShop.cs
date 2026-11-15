using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rethread.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserAppShop : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "active",
                table: "vendors",
                type: "boolean",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "opening_hours",
                table: "vendors",
                type: "text",
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "phone",
                table: "vendors",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "chest_cm",
                table: "products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "condition_grade",
                table: "products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: false,
                defaultValue: "excellent");

            migrationBuilder.AddColumn<string>(
                name: "gender",
                table: "products",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<decimal>(
                name: "impact_kg",
                table: "products",
                type: "numeric(6,2)",
                precision: 6,
                scale: 2,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "length_cm",
                table: "products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "material",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "rejuvenated_by",
                table: "products",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "waist_cm",
                table: "products",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_mobile",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "contact_name",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "payment_reference",
                table: "orders",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("UPDATE vendors SET opening_hours = 'Mon–Sat 9:00–19:00' WHERE opening_hours = '';");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "active",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "opening_hours",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "phone",
                table: "vendors");

            migrationBuilder.DropColumn(
                name: "chest_cm",
                table: "products");

            migrationBuilder.DropColumn(
                name: "condition_grade",
                table: "products");

            migrationBuilder.DropColumn(
                name: "gender",
                table: "products");

            migrationBuilder.DropColumn(
                name: "impact_kg",
                table: "products");

            migrationBuilder.DropColumn(
                name: "length_cm",
                table: "products");

            migrationBuilder.DropColumn(
                name: "material",
                table: "products");

            migrationBuilder.DropColumn(
                name: "rejuvenated_by",
                table: "products");

            migrationBuilder.DropColumn(
                name: "waist_cm",
                table: "products");

            migrationBuilder.DropColumn(
                name: "contact_mobile",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "contact_name",
                table: "orders");

            migrationBuilder.DropColumn(
                name: "payment_reference",
                table: "orders");
        }
    }
}
