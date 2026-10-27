using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class DatabaseV2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // database v2: stop here (nothing changed) if an object with one of the new names already exists
            migrationBuilder.Sql(Servisyo.Web.Data.DatabaseV2Sql.CheckNoClash);

            migrationBuilder.DropIndex(
                name: "IX_NOTIFICATIONS_USER_ID",
                table: "NOTIFICATIONS");

            migrationBuilder.DropIndex(
                name: "IX_MESSAGES_RECEIVER_ID",
                table: "MESSAGES");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PROVIDERS_COUNTS",
                table: "SERVICE_PROVIDERS",
                sql: "[REVIEW_COUNT] >= 0 AND ([BASE_FEE] IS NULL OR [BASE_FEE] >= 0)");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATIONS_USER_UNREAD",
                table: "NOTIFICATIONS",
                columns: new[] { "USER_ID", "IS_READ" });

            migrationBuilder.CreateIndex(
                name: "IX_MESSAGES_RECEIVER_UNREAD",
                table: "MESSAGES",
                columns: new[] { "RECEIVER_ID", "IS_READ" });

            migrationBuilder.CreateIndex(
                name: "IX_BOOKINGS_DATE_PAYMENT",
                table: "BOOKINGS",
                columns: new[] { "BOOKING_DATE", "PAYMENT_STATUS" });

            migrationBuilder.CreateIndex(
                name: "IX_BOOKINGS_PROVIDER_STATUS",
                table: "BOOKINGS",
                columns: new[] { "PROVIDER_ID", "STATUS" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_BOOKINGS_FEE",
                table: "BOOKINGS",
                sql: "[TOTAL_FEE] >= 0");
        
            // database v2: functions, views, stored procedures and triggers (docs\sql\DatabaseDesign-v2.sql)
            foreach (var sql in Servisyo.Web.Data.DatabaseV2Sql.CreateAll)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Servisyo.Web.Data.DatabaseV2Sql.DropAll);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PROVIDERS_COUNTS",
                table: "SERVICE_PROVIDERS");

            migrationBuilder.DropIndex(
                name: "IX_NOTIFICATIONS_USER_UNREAD",
                table: "NOTIFICATIONS");

            migrationBuilder.DropIndex(
                name: "IX_MESSAGES_RECEIVER_UNREAD",
                table: "MESSAGES");

            migrationBuilder.DropIndex(
                name: "IX_BOOKINGS_DATE_PAYMENT",
                table: "BOOKINGS");

            migrationBuilder.DropIndex(
                name: "IX_BOOKINGS_PROVIDER_STATUS",
                table: "BOOKINGS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_BOOKINGS_FEE",
                table: "BOOKINGS");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATIONS_USER_ID",
                table: "NOTIFICATIONS",
                column: "USER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_MESSAGES_RECEIVER_ID",
                table: "MESSAGES",
                column: "RECEIVER_ID");
        }
    }
}
