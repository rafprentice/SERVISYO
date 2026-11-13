using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class BookingCompletionProof : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "COMPLETED_AT",
                table: "BOOKINGS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "COMPLETION_MESSAGE",
                table: "BOOKINGS",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "COMPLETION_PHOTO_URL",
                table: "BOOKINGS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BOOKINGS_COMPLETION",
                table: "BOOKINGS",
                sql: "([COMPLETION_MESSAGE] IS NULL AND [COMPLETION_PHOTO_URL] IS NULL AND [COMPLETED_AT] IS NULL) OR ([STATUS] = 'Completed' AND [COMPLETION_MESSAGE] IS NOT NULL AND LEN([COMPLETION_MESSAGE]) > 0 AND [COMPLETION_PHOTO_URL] IS NOT NULL AND [COMPLETED_AT] IS NOT NULL)");
        
            // database v2.11: trg_Bookings_Audit also knows the job's proof
            foreach (var sql in Servisyo.Web.Data.BookingCompletionSql.CreateAll) migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // database v2.11 undone: trg_Bookings_Audit as it was in v2.10, then EF removes the columns
            foreach (var sql in Servisyo.Web.Data.BookingCompletionSql.RestoreOld) migrationBuilder.Sql(sql);

            migrationBuilder.DropCheckConstraint(
                name: "CK_BOOKINGS_COMPLETION",
                table: "BOOKINGS");

            migrationBuilder.DropColumn(
                name: "COMPLETED_AT",
                table: "BOOKINGS");

            migrationBuilder.DropColumn(
                name: "COMPLETION_MESSAGE",
                table: "BOOKINGS");

            migrationBuilder.DropColumn(
                name: "COMPLETION_PHOTO_URL",
                table: "BOOKINGS");
        }
    }
}
