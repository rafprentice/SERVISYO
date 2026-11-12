using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class NameChangeRequests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PROVIDER_NAME_CHANGE_REQUESTS",
                columns: table => new
                {
                    REQUEST_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PROVIDER_ID = table.Column<int>(type: "int", nullable: false),
                    FIRST_NAME = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    MIDDLE_NAME = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    LAST_NAME = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false, defaultValue: "Pending"),
                    REQUESTED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    DECIDED_BY_ACTION_ID = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROVIDER_NAME_CHANGE_REQUESTS", x => x.REQUEST_ID);
                    table.CheckConstraint("CK_PROVIDER_NAME_CHANGE_REQUESTS_DECISION", "([STATUS] = 'Pending' AND [DECIDED_BY_ACTION_ID] IS NULL) OR ([STATUS] <> 'Pending' AND [DECIDED_BY_ACTION_ID] IS NOT NULL)");
                    table.CheckConstraint("CK_PROVIDER_NAME_CHANGE_REQUESTS_STATUS", "[STATUS] IN ('Pending','Approved','Rejected')");
                    table.ForeignKey(
                        name: "FK_PROVIDER_NAME_CHANGE_REQUESTS_ADMIN_ACTIONS_DECIDED_BY_ACTION_ID",
                        column: x => x.DECIDED_BY_ACTION_ID,
                        principalTable: "ADMIN_ACTIONS",
                        principalColumn: "ACTION_ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PROVIDER_NAME_CHANGE_REQUESTS_SERVICE_PROVIDERS_PROVIDER_ID",
                        column: x => x.PROVIDER_ID,
                        principalTable: "SERVICE_PROVIDERS",
                        principalColumn: "PROVIDER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PROVIDER_NAME_CHANGE_REQUESTS_DECIDED_BY_ACTION_ID",
                table: "PROVIDER_NAME_CHANGE_REQUESTS",
                column: "DECIDED_BY_ACTION_ID",
                unique: true,
                filter: "[DECIDED_BY_ACTION_ID] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PROVIDER_NAME_CHANGE_REQUESTS_PROVIDER_ID_STATUS",
                table: "PROVIDER_NAME_CHANGE_REQUESTS",
                columns: new[] { "PROVIDER_ID", "STATUS" });

            migrationBuilder.CreateIndex(
                name: "UX_PROVIDER_NAME_CHANGE_REQUESTS_ONE_PENDING",
                table: "PROVIDER_NAME_CHANGE_REQUESTS",
                column: "PROVIDER_ID",
                unique: true,
                filter: "[STATUS] = 'Pending'");
        
            // database v2.8: the bell shows name change requests (administrators) and their answers (providers)
            migrationBuilder.Sql(Servisyo.Web.Data.NameChangeRequestsSql.CreateAll[0]);   // usp_GetNotifications
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // database v2.8 undone: usp_GetNotifications as it was in v2.7, then EF removes the table
            migrationBuilder.Sql(Servisyo.Web.Data.NameChangeRequestsSql.RestoreOld[0]);

            migrationBuilder.DropTable(
                name: "PROVIDER_NAME_CHANGE_REQUESTS");
        }
    }
}
