using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class AuditStandard : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // database v2.10: stop if another object has one of the audit names; the v2.9 REPORT lines go
            migrationBuilder.Sql(Servisyo.Web.Data.AuditStandardSql.CheckBefore);

            migrationBuilder.DropIndex(
                name: "IX_SYSTEM_LOGS_ACTION_TYPE_USER_ROLE",
                table: "SYSTEM_LOGS");

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_LOGS_ACTION_TYPE_CREATED_AT",
                table: "SYSTEM_LOGS",
                columns: new[] { "ACTION_TYPE", "CREATED_AT" });

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_LOGS_USER_ID_CREATED_AT",
                table: "SYSTEM_LOGS",
                columns: new[] { "USER_ID", "CREATED_AT" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_SYSTEM_LOGS_ACTION_TYPE",
                table: "SYSTEM_LOGS",
                sql: "[ACTION_TYPE] IN ('INSERT','UPDATE','DELETE','LOGIN')");
        
            // database v2.10: fn_AuditValue, fn_AuditActor and the audit triggers in the standard format
            foreach (var sql in Servisyo.Web.Data.AuditStandardSql.CreateAll) migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // database v2.10 undone: the v2.9 audit triggers back, the new triggers and the functions removed
            foreach (var sql in Servisyo.Web.Data.AuditStandardSql.RestoreOld) migrationBuilder.Sql(sql);

            migrationBuilder.DropIndex(
                name: "IX_SYSTEM_LOGS_ACTION_TYPE_CREATED_AT",
                table: "SYSTEM_LOGS");

            migrationBuilder.DropIndex(
                name: "IX_SYSTEM_LOGS_USER_ID_CREATED_AT",
                table: "SYSTEM_LOGS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_SYSTEM_LOGS_ACTION_TYPE",
                table: "SYSTEM_LOGS");

            migrationBuilder.CreateIndex(
                name: "IX_SYSTEM_LOGS_ACTION_TYPE_USER_ROLE",
                table: "SYSTEM_LOGS",
                columns: new[] { "ACTION_TYPE", "USER_ROLE" });
        }
    }
}
