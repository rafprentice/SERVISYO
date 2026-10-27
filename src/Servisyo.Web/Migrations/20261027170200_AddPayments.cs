using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddPayments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PAYMENTS",
                columns: table => new
                {
                    PAYMENT_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BOOKING_ID = table.Column<int>(type: "int", nullable: false),
                    AMOUNT = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    PAYMENT_METHOD = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PAYMENT_STATUS = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TRANSACTION_REFERENCE = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    PAID_AT = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYMENTS", x => x.PAYMENT_ID);
                    table.CheckConstraint("CK_PAYMENTS_AMOUNT", "[AMOUNT] >= 0");
                    table.CheckConstraint("CK_PAYMENTS_METHOD", "[PAYMENT_METHOD] IN ('CashOnCompletion','MockEWallet')");
                    table.CheckConstraint("CK_PAYMENTS_PAID_AT", "[PAYMENT_STATUS] = 'PendingCollection' OR [PAID_AT] IS NOT NULL");
                    table.CheckConstraint("CK_PAYMENTS_PENDING_CASH", "[PAYMENT_STATUS] <> 'PendingCollection' OR [PAYMENT_METHOD] = 'CashOnCompletion'");
                    table.CheckConstraint("CK_PAYMENTS_STATUS", "[PAYMENT_STATUS] IN ('PendingCollection','Paid','Refunded')");
                    table.ForeignKey(
                        name: "FK_PAYMENTS_BOOKINGS_BOOKING_ID",
                        column: x => x.BOOKING_ID,
                        principalTable: "BOOKINGS",
                        principalColumn: "BOOKING_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CASH_PAYMENT_DETAILS",
                columns: table => new
                {
                    PAYMENT_ID = table.Column<int>(type: "int", nullable: false),
                    IS_COLLECTED_BY_PROVIDER = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    COLLECTED_AT = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CASH_PAYMENT_DETAILS", x => x.PAYMENT_ID);
                    table.CheckConstraint("CK_CASH_COLLECTED", "([IS_COLLECTED_BY_PROVIDER] = 0 AND [COLLECTED_AT] IS NULL) OR ([IS_COLLECTED_BY_PROVIDER] = 1 AND [COLLECTED_AT] IS NOT NULL)");
                    table.ForeignKey(
                        name: "FK_CASH_PAYMENT_DETAILS_PAYMENTS_PAYMENT_ID",
                        column: x => x.PAYMENT_ID,
                        principalTable: "PAYMENTS",
                        principalColumn: "PAYMENT_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENTS_BOOKING_ID",
                table: "PAYMENTS",
                column: "BOOKING_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENTS_TRANSACTION_REFERENCE",
                table: "PAYMENTS",
                column: "TRANSACTION_REFERENCE",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CASH_PAYMENT_DETAILS");

            migrationBuilder.DropTable(
                name: "PAYMENTS");
        }
    }
}
