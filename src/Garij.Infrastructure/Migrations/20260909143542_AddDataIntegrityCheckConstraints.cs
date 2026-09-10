using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Garij.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddDataIntegrityCheckConstraints : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddCheckConstraint(
                name: "CK_Vehicle_Year",
                table: "Vehicles",
                sql: "\"Year\" BETWEEN 1900 AND 2100");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServiceCatalog_BasePrice",
                table: "ServiceCatalogs",
                sql: "\"BasePrice\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ServiceCatalog_EstimatedDurationMinutes",
                table: "ServiceCatalogs",
                sql: "\"EstimatedDurationMinutes\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PaymentTransaction_Amount",
                table: "PaymentTransactions",
                sql: "\"Amount\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Part_ReorderLevel",
                table: "Parts",
                sql: "\"ReorderLevel\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Part_UnitPrice",
                table: "Parts",
                sql: "\"UnitPrice\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobServiceDetail_PriceAtBooking",
                table: "JobServiceDetails",
                sql: "\"PriceAtBooking\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobServiceDetail_Quantity",
                table: "JobServiceDetails",
                sql: "\"Quantity\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobPartUsed_PriceAtUsage",
                table: "JobPartsUsed",
                sql: "\"PriceAtUsage\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_JobPartUsed_QuantityUsed",
                table: "JobPartsUsed",
                sql: "\"QuantityUsed\" > 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoice_SubTotal",
                table: "Invoices",
                sql: "\"SubTotal\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoice_TaxAmount",
                table: "Invoices",
                sql: "\"TaxAmount\" >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_Invoice_TotalAmount",
                table: "Invoices",
                sql: "\"TotalAmount\" >= 0");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_Vehicle_Year",
                table: "Vehicles");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServiceCatalog_BasePrice",
                table: "ServiceCatalogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ServiceCatalog_EstimatedDurationMinutes",
                table: "ServiceCatalogs");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PaymentTransaction_Amount",
                table: "PaymentTransactions");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Part_ReorderLevel",
                table: "Parts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Part_UnitPrice",
                table: "Parts");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobServiceDetail_PriceAtBooking",
                table: "JobServiceDetails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobServiceDetail_Quantity",
                table: "JobServiceDetails");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobPartUsed_PriceAtUsage",
                table: "JobPartsUsed");

            migrationBuilder.DropCheckConstraint(
                name: "CK_JobPartUsed_QuantityUsed",
                table: "JobPartsUsed");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoice_SubTotal",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoice_TaxAmount",
                table: "Invoices");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Invoice_TotalAmount",
                table: "Invoices");
        }
    }
}
