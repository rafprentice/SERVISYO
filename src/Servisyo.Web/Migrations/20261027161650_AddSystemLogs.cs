using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddSystemLogs : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SYSTEM_LOGS",
                columns: table => new
                {
                    SYSTEM_LOG_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    USER_ID = table.Column<int>(type: "int", nullable: true),
                    USER_ROLE = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ACTION_TYPE = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    ENTITY_NAME = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ENTITY_ID = table.Column<int>(type: "int", nullable: true),
                    DESCRIPTION = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SYSTEM_LOGS", x => x.SYSTEM_LOG_ID);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_LOGS_ACTION_TYPE_USER_ROLE",
                table: "SYSTEM_LOGS",
                columns: new[] { "ACTION_TYPE", "USER_ROLE" });

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_LOGS_CREATED_AT",
                table: "SYSTEM_LOGS",
                column: "CREATED_AT");
        
            // audit triggers (after the SYSTEM_LOGS table exists)
            migrationBuilder.Sql(Servisyo.Web.Data.AuditTriggersSql.CreateBookings);
            migrationBuilder.Sql(Servisyo.Web.Data.AuditTriggersSql.CreateUsers);
            migrationBuilder.Sql(Servisyo.Web.Data.AuditTriggersSql.CreateServices);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Servisyo.Web.Data.AuditTriggersSql.DropAll);
            migrationBuilder.DropTable(
                name: "SYSTEM_LOGS");
        }
    }
}
