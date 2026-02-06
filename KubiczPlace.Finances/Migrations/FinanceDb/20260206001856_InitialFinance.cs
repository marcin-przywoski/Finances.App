using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace KubiczPlace.Finances.Migrations.FinanceDb
{
    /// <inheritdoc />
    public partial class InitialFinance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Services",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    BasePrice = table.Column<decimal>(type: "decimal(18, 2)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Services", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Workers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    DefaultCommissionPercentage = table.Column<decimal>(type: "decimal(5, 2)", nullable: false),
                    ApplicationUserId = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workers", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ServiceRecords",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    WorkerId = table.Column<int>(type: "INTEGER", nullable: false),
                    ServiceId = table.Column<int>(type: "INTEGER", nullable: false),
                    DatePerformed = table.Column<DateTime>(type: "TEXT", nullable: false),
                    AmountPaid = table.Column<decimal>(type: "decimal(18, 2)", nullable: false),
                    CommissionPercentageApplied = table.Column<decimal>(type: "decimal(5, 2)", nullable: false),
                    Tips = table.Column<decimal>(type: "decimal(18, 2)", nullable: false),
                    ClientName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: true),
                    Notes = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ServiceRecords", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ServiceRecords_Services_ServiceId",
                        column: x => x.ServiceId,
                        principalTable: "Services",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ServiceRecords_Workers_WorkerId",
                        column: x => x.WorkerId,
                        principalTable: "Workers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "Services",
                columns: new[] { "Id", "BasePrice", "Name" },
                values: new object[,]
                {
                    { 1, 50m, "Strzyżenie męskie" },
                    { 2, 80m, "Strzyżenie damskie" },
                    { 3, 30m, "Broda" },
                    { 4, 150m, "Koloryzacja" },
                    { 5, 70m, "Strzyżenie + Broda" }
                });

            migrationBuilder.InsertData(
                table: "Workers",
                columns: new[] { "Id", "ApplicationUserId", "DefaultCommissionPercentage", "Name" },
                values: new object[,]
                {
                    { 1, null, 50m, "Jan Kowalski" },
                    { 2, null, 45m, "Anna Nowak" },
                    { 3, null, 55m, "Piotr Wiśniewski" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRecords_ServiceId",
                table: "ServiceRecords",
                column: "ServiceId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceRecords_WorkerId",
                table: "ServiceRecords",
                column: "WorkerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ServiceRecords");

            migrationBuilder.DropTable(
                name: "Services");

            migrationBuilder.DropTable(
                name: "Workers");
        }
    }
}
