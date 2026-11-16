using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Rethread.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class UploadsInDatabase : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "path",
                table: "files");

            migrationBuilder.CreateTable(
                name: "file_contents",
                columns: table => new
                {
                    file_id = table.Column<string>(type: "text", nullable: false),
                    bytes = table.Column<byte[]>(type: "bytea", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_file_contents", x => x.file_id);
                    table.ForeignKey(
                        name: "fk_file_contents_files_file_id",
                        column: x => x.file_id,
                        principalTable: "files",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "file_contents");

            migrationBuilder.AddColumn<string>(
                name: "path",
                table: "files",
                type: "text",
                nullable: false,
                defaultValue: "");
        }
    }
}
