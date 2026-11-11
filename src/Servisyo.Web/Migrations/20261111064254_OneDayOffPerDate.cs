using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class OneDayOffPerDate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_PROVIDER_UNAVAILABLE_DATES_UNAVAILABLE_DATE",
                table: "PROVIDER_UNAVAILABLE_DATES",
                column: "UNAVAILABLE_DATE",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_PROVIDER_UNAVAILABLE_DATES_UNAVAILABLE_DATE",
                table: "PROVIDER_UNAVAILABLE_DATES");
        }
    }
}
