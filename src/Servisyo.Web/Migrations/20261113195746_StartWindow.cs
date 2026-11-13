using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class StartWindow : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "LATE_REASON",
                table: "BOOKINGS",
                type: "nvarchar(500)",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "STARTED_AT",
                table: "BOOKINGS",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IS_LATE",
                table: "BOOKINGS",
                type: "bit",
                nullable: false,
                computedColumnSql: "ISNULL(CAST(CASE WHEN DATEDIFF(MINUTE, CAST([BOOKING_DATE] AS DATETIME) + CAST([BOOKING_TIME] AS DATETIME), DATEADD(HOUR, 8, [STARTED_AT])) > 15 THEN 1 ELSE 0 END AS BIT), CAST(0 AS BIT))",
                stored: false);

            migrationBuilder.AddColumn<int>(
                name: "MINUTES_LATE",
                table: "BOOKINGS",
                type: "int",
                nullable: true,
                computedColumnSql: "CASE WHEN DATEDIFF(MINUTE, CAST([BOOKING_DATE] AS DATETIME) + CAST([BOOKING_TIME] AS DATETIME), DATEADD(HOUR, 8, [STARTED_AT])) > 15 THEN DATEDIFF(MINUTE, CAST([BOOKING_DATE] AS DATETIME) + CAST([BOOKING_TIME] AS DATETIME), DATEADD(HOUR, 8, [STARTED_AT])) END",
                stored: false);

            migrationBuilder.AddCheckConstraint(
                name: "CK_BOOKINGS_START",
                table: "BOOKINGS",
                sql: "([STARTED_AT] IS NULL AND [LATE_REASON] IS NULL) OR ([STARTED_AT] IS NOT NULL AND [STATUS] IN ('In Progress','Completed') AND DATEDIFF(MINUTE, CAST([BOOKING_DATE] AS DATETIME) + CAST([BOOKING_TIME] AS DATETIME), DATEADD(HOUR, 8, [STARTED_AT])) BETWEEN 0 AND 120 AND ((DATEDIFF(MINUTE, CAST([BOOKING_DATE] AS DATETIME) + CAST([BOOKING_TIME] AS DATETIME), DATEADD(HOUR, 8, [STARTED_AT])) > 15 AND [LATE_REASON] IS NOT NULL AND LEN([LATE_REASON]) > 0) OR (DATEDIFF(MINUTE, CAST([BOOKING_DATE] AS DATETIME) + CAST([BOOKING_TIME] AS DATETIME), DATEADD(HOUR, 8, [STARTED_AT])) <= 15 AND [LATE_REASON] IS NULL)))");
        
            // database v2.12: the start window, the late reason and the provider no-show in the triggers and procedures
            foreach (var sql in Servisyo.Web.Data.StartWindowSql.CreateAll) migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // database v2.12 undone: the five objects as they were before, then EF removes the columns
            foreach (var sql in Servisyo.Web.Data.StartWindowSql.RestoreOld) migrationBuilder.Sql(sql);

            migrationBuilder.DropCheckConstraint(
                name: "CK_BOOKINGS_START",
                table: "BOOKINGS");

            migrationBuilder.DropColumn(
                name: "IS_LATE",
                table: "BOOKINGS");

            migrationBuilder.DropColumn(
                name: "MINUTES_LATE",
                table: "BOOKINGS");

            migrationBuilder.DropColumn(
                name: "LATE_REASON",
                table: "BOOKINGS");

            migrationBuilder.DropColumn(
                name: "STARTED_AT",
                table: "BOOKINGS");
        }
    }
}
