using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garij.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectPurchases : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "ProjectPurchases",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    LicenseKey = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false),
                    IdentityUserId = table.Column<string>(type: "TEXT", maxLength: 450, nullable: true),
                    BuyerName = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    BuyerEmail = table.Column<string>(type: "TEXT", maxLength: 256, nullable: false),
                    WorkshopName = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true),
                    Amount = table.Column<decimal>(type: "decimal(18,2)", nullable: false),
                    Currency = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    PaymentMethod = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    TransactionReference = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    PurchasedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    Status = table.Column<int>(type: "INTEGER", nullable: false),
                    IsActive = table.Column<bool>(type: "INTEGER", nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectPurchases", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPurchases_BuyerEmail",
                table: "ProjectPurchases",
                column: "BuyerEmail");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPurchases_IdentityUserId",
                table: "ProjectPurchases",
                column: "IdentityUserId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPurchases_LicenseKey",
                table: "ProjectPurchases",
                column: "LicenseKey",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ProjectPurchases");
        }
    }
}
