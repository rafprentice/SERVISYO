using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class ExtraDayOff : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "APPROVED_BY_ACTION_ID",
                table: "PROVIDER_UNAVAILABLE_DATES",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_PROVIDER_UNAVAILABLE_DATES_APPROVED_BY_ACTION_ID",
                table: "PROVIDER_UNAVAILABLE_DATES",
                column: "APPROVED_BY_ACTION_ID");

            migrationBuilder.AddForeignKey(
                name: "FK_PROVIDER_UNAVAILABLE_DATES_ADMIN_ACTIONS_APPROVED_BY_ACTION_ID",
                table: "PROVIDER_UNAVAILABLE_DATES",
                column: "APPROVED_BY_ACTION_ID",
                principalTable: "ADMIN_ACTIONS",
                principalColumn: "ACTION_ID",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_PROVIDER_UNAVAILABLE_DATES_ADMIN_ACTIONS_APPROVED_BY_ACTION_ID",
                table: "PROVIDER_UNAVAILABLE_DATES");

            migrationBuilder.DropIndex(
                name: "IX_PROVIDER_UNAVAILABLE_DATES_APPROVED_BY_ACTION_ID",
                table: "PROVIDER_UNAVAILABLE_DATES");

            migrationBuilder.DropColumn(
                name: "APPROVED_BY_ACTION_ID",
                table: "PROVIDER_UNAVAILABLE_DATES");
        }
    }
}
