using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace KubiczPlace.Finances.Migrations
{
    /// <inheritdoc />
    public partial class AddWorkerDefaultShare : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DefaultSharePct",
                table: "Workers",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DefaultSharePct",
                table: "Workers");
        }
    }
}
