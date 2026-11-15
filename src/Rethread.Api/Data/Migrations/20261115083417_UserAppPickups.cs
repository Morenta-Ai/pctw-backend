using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rethread.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserAppPickups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "address_label",
                table: "pickups",
                type: "character varying(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "home");

            migrationBuilder.AddColumn<string>(
                name: "customer_cancel_reason",
                table: "pickups",
                type: "character varying(20)",
                maxLength: 20,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "declared_bag_count",
                table: "pickups",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "piece_count",
                table: "pickups",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "pickup_status",
                table: "notifications",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "idempotency_records",
                columns: table => new
                {
                    customer_id = table.Column<string>(type: "text", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false),
                    key = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    request_hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    resource_id = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_idempotency_records", x => new { x.customer_id, x.scope, x.key });
                });

            migrationBuilder.CreateTable(
                name: "pickup_preferred_dates",
                columns: table => new
                {
                    pickup_id = table.Column<string>(type: "character varying(20)", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    slot = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_pickup_preferred_dates", x => new { x.pickup_id, x.priority });
                    table.ForeignKey(
                        name: "fk_pickup_preferred_dates_pickups_pickup_id",
                        column: x => x.pickup_id,
                        principalTable: "pickups",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_pickup_preferred_dates_date_slot",
                table: "pickup_preferred_dates",
                columns: new[] { "date", "slot" });

            // Pickups booked before preferred dates existed: their day, then the next two, in the same slot.
            migrationBuilder.Sql("""
                INSERT INTO pickup_preferred_dates (pickup_id, priority, date, slot)
                SELECT p.id, n, p.scheduled_date + (n - 1), p.slot
                FROM pickups p CROSS JOIN generate_series(1, 3) AS n
                WHERE NOT EXISTS (SELECT 1 FROM pickup_preferred_dates d WHERE d.pickup_id = p.id);
                """);
            migrationBuilder.Sql("UPDATE pickups SET piece_count = round(estimated_kg / 0.3) WHERE method = 'bulk' AND piece_count IS NULL;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "idempotency_records");

            migrationBuilder.DropTable(
                name: "pickup_preferred_dates");

            migrationBuilder.DropColumn(
                name: "address_label",
                table: "pickups");

            migrationBuilder.DropColumn(
                name: "customer_cancel_reason",
                table: "pickups");

            migrationBuilder.DropColumn(
                name: "declared_bag_count",
                table: "pickups");

            migrationBuilder.DropColumn(
                name: "piece_count",
                table: "pickups");

            migrationBuilder.DropColumn(
                name: "pickup_status",
                table: "notifications");
        }
    }
}
