using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garij.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddGarageIdMultiTenancy : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vehicles_LicensePlateNumber",
                table: "Vehicles");

            migrationBuilder.AddColumn<string>(
                name: "GarageId",
                table: "Vehicles",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GarageId",
                table: "StaffUsers",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GarageId",
                table: "ServiceJobs",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GarageId",
                table: "ProjectPurchases",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RefundedAt",
                table: "PaymentTransactions",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GarageId",
                table: "Parts",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GarageId",
                table: "Notifications",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Type",
                table: "Notifications",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "GarageId",
                table: "Invoices",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GarageId",
                table: "Customers",
                type: "TEXT",
                maxLength: 100,
                nullable: true);

            // Every row already in the database predates multi-tenancy, and the application reads
            // a null GarageId as "default-garij-master" everywhere. Writing that value out makes the
            // stored data say what the code already assumes, and it is required before the unique
            // indexes below: SQLite treats nulls as distinct, so a unique index over a null GarageId
            // would let duplicate plates and part numbers through.
            foreach (var table in new[] { "Customers", "Vehicles", "ServiceJobs", "Invoices", "Notifications", "Parts", "StaffUsers", "ProjectPurchases" })
            {
                migrationBuilder.Sql($"UPDATE \"{table}\" SET \"GarageId\" = 'default-garij-master' WHERE \"GarageId\" IS NULL;");
            }

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_GarageId",
                table: "Vehicles",
                column: "GarageId");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_GarageId_LicensePlateNumber",
                table: "Vehicles",
                columns: new[] { "GarageId", "LicensePlateNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_LicensePlateNumber",
                table: "Vehicles",
                column: "LicensePlateNumber");

            migrationBuilder.CreateIndex(
                name: "IX_StaffUsers_GarageId",
                table: "StaffUsers",
                column: "GarageId");

            migrationBuilder.CreateIndex(
                name: "IX_ServiceJobs_GarageId",
                table: "ServiceJobs",
                column: "GarageId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectPurchases_GarageId",
                table: "ProjectPurchases",
                column: "GarageId");

            migrationBuilder.CreateIndex(
                name: "IX_Parts_GarageId",
                table: "Parts",
                column: "GarageId");

            migrationBuilder.CreateIndex(
                name: "IX_Parts_GarageId_PartNumber",
                table: "Parts",
                columns: new[] { "GarageId", "PartNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Notifications_GarageId",
                table: "Notifications",
                column: "GarageId");

            migrationBuilder.CreateIndex(
                name: "IX_Invoices_GarageId",
                table: "Invoices",
                column: "GarageId");

            migrationBuilder.CreateIndex(
                name: "IX_Customers_GarageId",
                table: "Customers",
                column: "GarageId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_Vehicles_GarageId",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_GarageId_LicensePlateNumber",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_Vehicles_LicensePlateNumber",
                table: "Vehicles");

            migrationBuilder.DropIndex(
                name: "IX_StaffUsers_GarageId",
                table: "StaffUsers");

            migrationBuilder.DropIndex(
                name: "IX_ServiceJobs_GarageId",
                table: "ServiceJobs");

            migrationBuilder.DropIndex(
                name: "IX_ProjectPurchases_GarageId",
                table: "ProjectPurchases");

            migrationBuilder.DropIndex(
                name: "IX_Parts_GarageId",
                table: "Parts");

            migrationBuilder.DropIndex(
                name: "IX_Parts_GarageId_PartNumber",
                table: "Parts");

            migrationBuilder.DropIndex(
                name: "IX_Notifications_GarageId",
                table: "Notifications");

            migrationBuilder.DropIndex(
                name: "IX_Invoices_GarageId",
                table: "Invoices");

            migrationBuilder.DropIndex(
                name: "IX_Customers_GarageId",
                table: "Customers");

            migrationBuilder.DropColumn(
                name: "GarageId",
                table: "Vehicles");

            migrationBuilder.DropColumn(
                name: "GarageId",
                table: "StaffUsers");

            migrationBuilder.DropColumn(
                name: "GarageId",
                table: "ServiceJobs");

            migrationBuilder.DropColumn(
                name: "GarageId",
                table: "ProjectPurchases");

            migrationBuilder.DropColumn(
                name: "RefundedAt",
                table: "PaymentTransactions");

            migrationBuilder.DropColumn(
                name: "GarageId",
                table: "Parts");

            migrationBuilder.DropColumn(
                name: "GarageId",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Notifications");

            migrationBuilder.DropColumn(
                name: "GarageId",
                table: "Invoices");

            migrationBuilder.DropColumn(
                name: "GarageId",
                table: "Customers");

            migrationBuilder.CreateIndex(
                name: "IX_Vehicles_LicensePlateNumber",
                table: "Vehicles",
                column: "LicensePlateNumber",
                unique: true);
        }
    }
}
