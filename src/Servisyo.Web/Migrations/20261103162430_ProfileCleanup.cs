using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class ProfileCleanup : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // database v2.1: stop here (nothing changed) on a trigger-name clash, or if a phone number / name does not fit
            migrationBuilder.Sql(Servisyo.Web.Data.ProfileCleanupSql.CheckNoClash);
            migrationBuilder.Sql(Servisyo.Web.Data.ProfileCleanupSql.PrepareData);

            migrationBuilder.DropColumn(
                name: "CITY",
                table: "SERVICE_PROVIDERS");

            migrationBuilder.DropColumn(
                name: "CITY",
                table: "CUSTOMERS");

            migrationBuilder.AlterColumn<string>(
                name: "PHONE_NUMBER",
                table: "USERS",
                type: "nvarchar(11)",
                maxLength: 11,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(15)",
                oldMaxLength: 15,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MIDDLE_NAME",
                table: "USERS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(155)",
                oldMaxLength: 155,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LAST_NAME",
                table: "USERS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(155)",
                oldMaxLength: 155);

            migrationBuilder.AlterColumn<string>(
                name: "FIRST_NAME",
                table: "USERS",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(155)",
                oldMaxLength: 155);

            migrationBuilder.AlterColumn<string>(
                name: "EMAIL",
                table: "USERS",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(155)",
                oldMaxLength: 155);

            migrationBuilder.AddColumn<string>(
                name: "ACCOUNT_STATUS",
                table: "SERVICE_PROVIDERS",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddColumn<string>(
                name: "ACCOUNT_STATUS",
                table: "CUSTOMERS",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "Active");

            migrationBuilder.AddCheckConstraint(
                name: "CK_PROVIDERS_STATUS",
                table: "SERVICE_PROVIDERS",
                sql: "[ACCOUNT_STATUS] IN ('Active','Suspended','Deactivated')");

            migrationBuilder.AddCheckConstraint(
                name: "CK_CUSTOMERS_STATUS",
                table: "CUSTOMERS",
                sql: "[ACCOUNT_STATUS] IN ('Active','Suspended','Deactivated')");
        
            // database v2.1: copy USERS.ACCOUNT_STATUS into the new columns, then the v2.1 objects (docs\sql\DatabaseDesign-v2.sql)
            migrationBuilder.Sql(Servisyo.Web.Data.ProfileCleanupSql.CopyAccountStatus);
            foreach (var sql in Servisyo.Web.Data.ProfileCleanupSql.CreateAll)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Servisyo.Web.Data.ProfileCleanupSql.DropNew);

            migrationBuilder.DropCheckConstraint(
                name: "CK_PROVIDERS_STATUS",
                table: "SERVICE_PROVIDERS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_CUSTOMERS_STATUS",
                table: "CUSTOMERS");

            migrationBuilder.DropColumn(
                name: "ACCOUNT_STATUS",
                table: "SERVICE_PROVIDERS");

            migrationBuilder.DropColumn(
                name: "ACCOUNT_STATUS",
                table: "CUSTOMERS");

            migrationBuilder.AlterColumn<string>(
                name: "PHONE_NUMBER",
                table: "USERS",
                type: "nvarchar(15)",
                maxLength: 15,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(11)",
                oldMaxLength: 11,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "MIDDLE_NAME",
                table: "USERS",
                type: "nvarchar(155)",
                maxLength: 155,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100,
                oldNullable: true);

            migrationBuilder.AlterColumn<string>(
                name: "LAST_NAME",
                table: "USERS",
                type: "nvarchar(155)",
                maxLength: 155,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "FIRST_NAME",
                table: "USERS",
                type: "nvarchar(155)",
                maxLength: 155,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(100)",
                oldMaxLength: 100);

            migrationBuilder.AlterColumn<string>(
                name: "EMAIL",
                table: "USERS",
                type: "nvarchar(155)",
                maxLength: 155,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "nvarchar(256)",
                oldMaxLength: 256);

            migrationBuilder.AddColumn<string>(
                name: "CITY",
                table: "SERVICE_PROVIDERS",
                type: "nvarchar(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CITY",
                table: "CUSTOMERS",
                type: "nvarchar(255)",
                maxLength: 255,
                nullable: true);
        
            foreach (var sql in Servisyo.Web.Data.ProfileCleanupSql.RestoreV2)
                migrationBuilder.Sql(sql);
        }
    }
}
