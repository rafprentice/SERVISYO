using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class DayOffRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "REQUEST_ID",
                table: "PROVIDER_UNAVAILABLE_DATES",
                type: "int",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "PROVIDER_DAY_OFF_REQUESTS",
                columns: table => new
                {
                    REQUEST_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PROVIDER_ID = table.Column<int>(type: "int", nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    STATUS = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    REQUESTED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    DECIDED_BY_ACTION_ID = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROVIDER_DAY_OFF_REQUESTS", x => x.REQUEST_ID);
                    table.CheckConstraint("CK_PROVIDER_DAY_OFF_REQUESTS_DECISION", "([STATUS] = 'Pending' AND [DECIDED_BY_ACTION_ID] IS NULL) OR ([STATUS] <> 'Pending' AND [DECIDED_BY_ACTION_ID] IS NOT NULL)");
                    table.CheckConstraint("CK_PROVIDER_DAY_OFF_REQUESTS_STATUS", "[STATUS] IN ('Pending','Approved','Rejected')");
                    table.ForeignKey(
                        name: "FK_PROVIDER_DAY_OFF_REQUESTS_ADMIN_ACTIONS_DECIDED_BY_ACTION_ID",
                        column: x => x.DECIDED_BY_ACTION_ID,
                        principalTable: "ADMIN_ACTIONS",
                        principalColumn: "ACTION_ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PROVIDER_DAY_OFF_REQUESTS_SERVICE_PROVIDERS_PROVIDER_ID",
                        column: x => x.PROVIDER_ID,
                        principalTable: "SERVICE_PROVIDERS",
                        principalColumn: "PROVIDER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PROVIDER_UNAVAILABLE_DATES_REQUEST_ID",
                table: "PROVIDER_UNAVAILABLE_DATES",
                column: "REQUEST_ID");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PROVIDER_UNAVAILABLE_DATES_SOURCE",
                table: "PROVIDER_UNAVAILABLE_DATES",
                sql: "[REQUEST_ID] IS NULL OR [APPROVED_BY_ACTION_ID] IS NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PROVIDER_DAY_OFF_REQUESTS_DECIDED_BY_ACTION_ID",
                table: "PROVIDER_DAY_OFF_REQUESTS",
                column: "DECIDED_BY_ACTION_ID",
                unique: true,
                filter: "[DECIDED_BY_ACTION_ID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PROVIDER_DAY_OFF_REQUESTS_PROVIDER_ID_STATUS",
                table: "PROVIDER_DAY_OFF_REQUESTS",
                columns: new[] { "PROVIDER_ID", "STATUS" });

            migrationBuilder.AddForeignKey(
                name: "FK_PROVIDER_UNAVAILABLE_DATES_PROVIDER_DAY_OFF_REQUESTS_REQUEST_ID",
                table: "PROVIDER_UNAVAILABLE_DATES",
                column: "REQUEST_ID",
                principalTable: "PROVIDER_DAY_OFF_REQUESTS",
                principalColumn: "REQUEST_ID",
                onDelete: ReferentialAction.Restrict);
        
            // database v2.7: a Pending day-off request does not stop bookings; the bell shows requests and answers
            migrationBuilder.Sql(Servisyo.Web.Data.DayOffRequestsSql.CreateAll[0]);   // fn_IsProviderAvailable
            migrationBuilder.Sql(Servisyo.Web.Data.DayOffRequestsSql.CreateAll[1]);   // usp_CreateBooking
            migrationBuilder.Sql(Servisyo.Web.Data.DayOffRequestsSql.CreateAll[2]);   // usp_GetNotifications
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // database v2.7 undone: the objects as they were before, then EF removes the table and the column
            migrationBuilder.Sql(Servisyo.Web.Data.DayOffRequestsSql.RestoreOld[0]);
            migrationBuilder.Sql(Servisyo.Web.Data.DayOffRequestsSql.RestoreOld[1]);
            migrationBuilder.Sql(Servisyo.Web.Data.DayOffRequestsSql.RestoreOld[2]);

            migrationBuilder.DropForeignKey(
                name: "FK_PROVIDER_UNAVAILABLE_DATES_PROVIDER_DAY_OFF_REQUESTS_REQUEST_ID",
                table: "PROVIDER_UNAVAILABLE_DATES");

            migrationBuilder.DropTable(
                name: "PROVIDER_DAY_OFF_REQUESTS");

            migrationBuilder.DropIndex(
                name: "IX_PROVIDER_UNAVAILABLE_DATES_REQUEST_ID",
                table: "PROVIDER_UNAVAILABLE_DATES");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PROVIDER_UNAVAILABLE_DATES_SOURCE",
                table: "PROVIDER_UNAVAILABLE_DATES");

            migrationBuilder.DropColumn(
                name: "REQUEST_ID",
                table: "PROVIDER_UNAVAILABLE_DATES");
        }
    }
}
