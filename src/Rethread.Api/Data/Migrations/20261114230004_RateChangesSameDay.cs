using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rethread.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RateChangesSameDay : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_rate_sets_effective_from",
                table: "rate_sets");

            migrationBuilder.CreateIndex(
                name: "ix_rate_sets_effective_from_created_at",
                table: "rate_sets",
                columns: new[] { "effective_from", "created_at" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "ix_rate_sets_effective_from_created_at",
                table: "rate_sets");

            migrationBuilder.CreateIndex(
                name: "ix_rate_sets_effective_from",
                table: "rate_sets",
                column: "effective_from",
                unique: true);
        }
    }
}
