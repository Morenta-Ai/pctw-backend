using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rethread.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UserAppEngagement : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateSequence(
                name: "ticket_number_seq",
                startValue: 1000L);

            migrationBuilder.CreateTable(
                name: "devices",
                columns: table => new
                {
                    token = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    customer_id = table.Column<string>(type: "text", nullable: false),
                    platform = table.Column<string>(type: "text", nullable: false),
                    app_version = table.Column<string>(type: "text", nullable: true),
                    locale = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_devices", x => x.token);
                });

            migrationBuilder.CreateTable(
                name: "notification_preferences",
                columns: table => new
                {
                    customer_id = table.Column<string>(type: "text", nullable: false),
                    orders = table.Column<bool>(type: "boolean", nullable: false),
                    payments = table.Column<bool>(type: "boolean", nullable: false),
                    offers = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_notification_preferences", x => x.customer_id);
                });

            migrationBuilder.CreateTable(
                name: "support_tickets",
                columns: table => new
                {
                    id = table.Column<string>(type: "text", nullable: false),
                    reference = table.Column<string>(type: "text", nullable: false),
                    customer_id = table.Column<string>(type: "text", nullable: false),
                    category = table.Column<string>(type: "text", nullable: false),
                    pickup_id = table.Column<string>(type: "text", nullable: true),
                    message = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                    screenshot_file_id = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_support_tickets", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ix_devices_customer_id",
                table: "devices",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_customer_id",
                table: "support_tickets",
                column: "customer_id");

            migrationBuilder.CreateIndex(
                name: "ix_support_tickets_reference",
                table: "support_tickets",
                column: "reference",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "devices");

            migrationBuilder.DropTable(
                name: "notification_preferences");

            migrationBuilder.DropTable(
                name: "support_tickets");

            migrationBuilder.DropSequence(
                name: "ticket_number_seq");
        }
    }
}
