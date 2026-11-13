using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class InactiveAccounts : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "INACTIVITY_WARNED_AT",
                table: "USERS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "LAST_ACTIVITY_AT",
                table: "USERS",
                type: "datetime2",
                nullable: false,
                defaultValueSql: "SYSUTCDATETIME()");

            migrationBuilder.CreateIndex(
                name: "IX_USERS_LAST_ACTIVITY_AT",
                table: "USERS",
                column: "LAST_ACTIVITY_AT");
        
            // database v2.9: each account starts from its last sign-in (SYSTEM_LOGS LOGIN), or its registration
            migrationBuilder.Sql(Servisyo.Web.Data.InactiveAccountsSql.InitLastActivity);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_USERS_LAST_ACTIVITY_AT",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "INACTIVITY_WARNED_AT",
                table: "USERS");

            migrationBuilder.DropColumn(
                name: "LAST_ACTIVITY_AT",
                table: "USERS");
        }
    }
}
