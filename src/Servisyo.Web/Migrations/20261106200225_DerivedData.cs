using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

#pragma warning disable CA1814 // Prefer jagged arrays over multidimensional

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class DerivedData : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // database v2.3: save every payment before EF changes PAYMENTS (docs\sql\DatabaseDesign-v2.sql)
            migrationBuilder.Sql(Servisyo.Web.Data.DerivedDataSql.StagePayments);

            migrationBuilder.DropForeignKey(
                name: "FK_PAYMENTS_BOOKINGS_BOOKING_ID",
                table: "PAYMENTS");

            migrationBuilder.DropTable(
                name: "BOOKING_REPORTS");

            migrationBuilder.DropTable(
                name: "CASH_PAYMENT_DETAILS");

            migrationBuilder.DropTable(
                name: "EMAIL_OTPS");

            migrationBuilder.DropTable(
                name: "NOTIFICATIONS");

            migrationBuilder.DropIndex(
                name: "IX_PAYMENTS_BOOKING_ID",
                table: "PAYMENTS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PAYMENTS_AMOUNT",
                table: "PAYMENTS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PAYMENTS_METHOD",
                table: "PAYMENTS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PAYMENTS_PAID_AT",
                table: "PAYMENTS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PAYMENTS_PENDING_CASH",
                table: "PAYMENTS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PAYMENTS_STATUS",
                table: "PAYMENTS");

            migrationBuilder.DropColumn(
                name: "AMOUNT",
                table: "PAYMENTS");

            migrationBuilder.DropColumn(
                name: "BOOKING_ID",
                table: "PAYMENTS");

            migrationBuilder.DropColumn(
                name: "CREATED_AT",
                table: "PAYMENTS");

            migrationBuilder.DropColumn(
                name: "PAID_AT",
                table: "PAYMENTS");

            migrationBuilder.DropColumn(
                name: "PAYMENT_METHOD",
                table: "PAYMENTS");

            migrationBuilder.DropColumn(
                name: "PAYMENT_STATUS",
                table: "PAYMENTS");

            migrationBuilder.RenameColumn(
                name: "TRANSACTION_REFERENCE",
                table: "PAYMENTS",
                newName: "METHOD_NAME");

            migrationBuilder.RenameIndex(
                name: "IX_PAYMENTS_TRANSACTION_REFERENCE",
                table: "PAYMENTS",
                newName: "IX_PAYMENTS_METHOD_NAME");

            migrationBuilder.AddColumn<DateTime>(
                name: "NOTIFICATIONS_SEEN_AT",
                table: "USERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PAYMENTS_DTL",
                columns: table => new
                {
                    PAYMENT_DTL_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BOOKING_ID = table.Column<int>(type: "int", nullable: false),
                    PAYMENT_ID = table.Column<int>(type: "int", nullable: false),
                    PAYMENT_STATUS = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TRANSACTION_REFERENCE = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    PAID_AT = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PAYMENTS_DTL", x => x.PAYMENT_DTL_ID);
                    table.CheckConstraint("CK_PAYMENTS_DTL_PAID_AT", "[PAYMENT_STATUS] = 'PendingCollection' OR [PAID_AT] IS NOT NULL");
                    table.CheckConstraint("CK_PAYMENTS_DTL_PENDING_CASH", "[PAYMENT_STATUS] <> 'PendingCollection' OR [PAYMENT_ID] = 1");
                    table.CheckConstraint("CK_PAYMENTS_DTL_STATUS", "[PAYMENT_STATUS] IN ('PendingCollection','Paid','Refunded')");
                    table.ForeignKey(
                        name: "FK_PAYMENTS_DTL_BOOKINGS_BOOKING_ID",
                        column: x => x.BOOKING_ID,
                        principalTable: "BOOKINGS",
                        principalColumn: "BOOKING_ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PAYMENTS_DTL_PAYMENTS_PAYMENT_ID",
                        column: x => x.PAYMENT_ID,
                        principalTable: "PAYMENTS",
                        principalColumn: "PAYMENT_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.InsertData(
                table: "PAYMENTS",
                columns: new[] { "PAYMENT_ID", "METHOD_NAME" },
                values: new object[,]
                {
                    { 1, "Cash on Completion" },
                    { 2, "GCash" },
                    { 3, "Maya" }
                });

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENTS_DTL_BOOKING_ID",
                table: "PAYMENTS_DTL",
                column: "BOOKING_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENTS_DTL_PAYMENT_ID",
                table: "PAYMENTS_DTL",
                column: "PAYMENT_ID");

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENTS_DTL_TRANSACTION_REFERENCE",
                table: "PAYMENTS_DTL",
                column: "TRANSACTION_REFERENCE",
                unique: true);
        
            // database v2.3: the saved payments go into PAYMENTS_DTL, then the procedures, view and triggers
            migrationBuilder.Sql(Servisyo.Web.Data.DerivedDataSql.FillPaymentDetails);
            foreach (var sql in Servisyo.Web.Data.DerivedDataSql.CreateAll)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Servisyo.Web.Data.DerivedDataSql.StageOld);

            migrationBuilder.DropTable(
                name: "PAYMENTS_DTL");

            migrationBuilder.DeleteData(
                table: "PAYMENTS",
                keyColumn: "PAYMENT_ID",
                keyValue: 1);

            migrationBuilder.DeleteData(
                table: "PAYMENTS",
                keyColumn: "PAYMENT_ID",
                keyValue: 2);

            migrationBuilder.DeleteData(
                table: "PAYMENTS",
                keyColumn: "PAYMENT_ID",
                keyValue: 3);

            migrationBuilder.DropColumn(
                name: "NOTIFICATIONS_SEEN_AT",
                table: "USERS");

            migrationBuilder.RenameColumn(
                name: "METHOD_NAME",
                table: "PAYMENTS",
                newName: "TRANSACTION_REFERENCE");

            migrationBuilder.RenameIndex(
                name: "IX_PAYMENTS_METHOD_NAME",
                table: "PAYMENTS",
                newName: "IX_PAYMENTS_TRANSACTION_REFERENCE");

            migrationBuilder.AddColumn<decimal>(
                name: "AMOUNT",
                table: "PAYMENTS",
                type: "decimal(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "BOOKING_ID",
                table: "PAYMENTS",
                type: "int",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "CREATED_AT",
                table: "PAYMENTS",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.AddColumn<DateTime>(
                name: "PAID_AT",
                table: "PAYMENTS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PAYMENT_METHOD",
                table: "PAYMENTS",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "PAYMENT_STATUS",
                table: "PAYMENTS",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "BOOKING_REPORTS",
                columns: table => new
                {
                    REPORT_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BOOKING_ID = table.Column<int>(type: "int", nullable: false),
                    REPORTED_BY_USER_ID = table.Column<int>(type: "int", nullable: false),
                    RESOLVED_BY_USER_ID = table.Column<int>(type: "int", nullable: true),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    REASON = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    RESOLUTION_NOTE = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    RESOLVED_AT = table.Column<DateTime>(type: "datetime2", nullable: true),
                    STATUS = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BOOKING_REPORTS", x => x.REPORT_ID);
                    table.CheckConstraint("CK_REPORTS_STATUS", "[STATUS] IN ('Open','Under Review','Resolved')");
                    table.ForeignKey(
                        name: "FK_BOOKING_REPORTS_BOOKINGS_BOOKING_ID",
                        column: x => x.BOOKING_ID,
                        principalTable: "BOOKINGS",
                        principalColumn: "BOOKING_ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BOOKING_REPORTS_USERS_REPORTED_BY_USER_ID",
                        column: x => x.REPORTED_BY_USER_ID,
                        principalTable: "USERS",
                        principalColumn: "USER_ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BOOKING_REPORTS_USERS_RESOLVED_BY_USER_ID",
                        column: x => x.RESOLVED_BY_USER_ID,
                        principalTable: "USERS",
                        principalColumn: "USER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CASH_PAYMENT_DETAILS",
                columns: table => new
                {
                    PAYMENT_ID = table.Column<int>(type: "int", nullable: false),
                    COLLECTED_AT = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IS_COLLECTED_BY_PROVIDER = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
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

            migrationBuilder.CreateTable(
                name: "EMAIL_OTPS",
                columns: table => new
                {
                    EMAIL_OTP_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    USER_ID = table.Column<int>(type: "int", nullable: false),
                    ATTEMPTS = table.Column<int>(type: "int", nullable: false),
                    CODE_HASH = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CONSUMED_AT = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    EXPIRES_AT = table.Column<DateTime>(type: "datetime2", nullable: false),
                    PURPOSE = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EMAIL_OTPS", x => x.EMAIL_OTP_ID);
                    table.ForeignKey(
                        name: "FK_EMAIL_OTPS_USERS_USER_ID",
                        column: x => x.USER_ID,
                        principalTable: "USERS",
                        principalColumn: "USER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NOTIFICATIONS",
                columns: table => new
                {
                    NOTIFICATION_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    USER_ID = table.Column<int>(type: "int", nullable: false),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    IS_READ = table.Column<bool>(type: "bit", nullable: false),
                    MESSAGE = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOTIFICATIONS", x => x.NOTIFICATION_ID);
                    table.ForeignKey(
                        name: "FK_NOTIFICATIONS_USERS_USER_ID",
                        column: x => x.USER_ID,
                        principalTable: "USERS",
                        principalColumn: "USER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PAYMENTS_BOOKING_ID",
                table: "PAYMENTS",
                column: "BOOKING_ID",
                unique: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_PAYMENTS_AMOUNT",
                table: "PAYMENTS",
                sql: "[AMOUNT] >= 0");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PAYMENTS_METHOD",
                table: "PAYMENTS",
                sql: "[PAYMENT_METHOD] IN ('CashOnCompletion','MockEWallet')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PAYMENTS_PAID_AT",
                table: "PAYMENTS",
                sql: "[PAYMENT_STATUS] = 'PendingCollection' OR [PAID_AT] IS NOT NULL");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PAYMENTS_PENDING_CASH",
                table: "PAYMENTS",
                sql: "[PAYMENT_STATUS] <> 'PendingCollection' OR [PAYMENT_METHOD] = 'CashOnCompletion'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PAYMENTS_STATUS",
                table: "PAYMENTS",
                sql: "[PAYMENT_STATUS] IN ('PendingCollection','Paid','Refunded')");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_REPORTS_BOOKING_ID",
                table: "BOOKING_REPORTS",
                column: "BOOKING_ID");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_REPORTS_REPORTED_BY_USER_ID",
                table: "BOOKING_REPORTS",
                column: "REPORTED_BY_USER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_REPORTS_RESOLVED_BY_USER_ID",
                table: "BOOKING_REPORTS",
                column: "RESOLVED_BY_USER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_EMAIL_OTPS_USER_ID_PURPOSE",
                table: "EMAIL_OTPS",
                columns: new[] { "USER_ID", "PURPOSE" });

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATIONS_USER_UNREAD",
                table: "NOTIFICATIONS",
                columns: new[] { "USER_ID", "IS_READ" });

            migrationBuilder.AddForeignKey(
                name: "FK_PAYMENTS_BOOKINGS_BOOKING_ID",
                table: "PAYMENTS",
                column: "BOOKING_ID",
                principalTable: "BOOKINGS",
                principalColumn: "BOOKING_ID",
                onDelete: ReferentialAction.Restrict);
        
            migrationBuilder.Sql(Servisyo.Web.Data.DerivedDataSql.FillOld);
            foreach (var sql in Servisyo.Web.Data.DerivedDataSql.RestoreV22)
                migrationBuilder.Sql(sql);
        }
    }
}
