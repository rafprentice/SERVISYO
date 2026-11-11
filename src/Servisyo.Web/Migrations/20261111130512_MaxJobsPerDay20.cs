using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class MaxJobsPerDay20 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // database v2.6: providers above 20 become 20 first, otherwise the new CHECK cannot be added
            migrationBuilder.Sql(Servisyo.Web.Data.MaxJobsSql.CapExisting);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PROVIDERS_LIMITS",
                table: "SERVICE_PROVIDERS");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PROVIDERS_LIMITS",
                table: "SERVICE_PROVIDERS",
                sql: "[MIN_NOTICE_DAYS] >= 0 AND [MAX_JOBS_PER_DAY] BETWEEN 1 AND 20");
        
            // database v2.6: usp_RegisterProvider allows 1 to 20 jobs a day
            migrationBuilder.Sql(Servisyo.Web.Data.MaxJobsSql.RegisterProvider20);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PROVIDERS_LIMITS",
                table: "SERVICE_PROVIDERS");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PROVIDERS_LIMITS",
                table: "SERVICE_PROVIDERS",
                sql: "[MIN_NOTICE_DAYS] >= 0 AND [MAX_JOBS_PER_DAY] > 0");
        
            migrationBuilder.Sql(Servisyo.Web.Data.MaxJobsSql.RegisterProviderV23);
        }
    }
}
