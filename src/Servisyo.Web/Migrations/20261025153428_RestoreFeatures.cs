using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class RestoreFeatures : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "EMAIL_VERIFICATION_PENDING",
                table: "USERS",
                type: "bit",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "EMAIL_OTPS",
                columns: table => new
                {
                    EMAIL_OTP_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    USER_ID = table.Column<int>(type: "int", nullable: false),
                    PURPOSE = table.Column<string>(type: "nvarchar(30)", maxLength: 30, nullable: false),
                    CODE_HASH = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    EXPIRES_AT = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ATTEMPTS = table.Column<int>(type: "int", nullable: false),
                    CONSUMED_AT = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_EMAIL_OTPS", x => x.EMAIL_OTP_ID);
                    table.ForeignKey(
                        name: "FK_EMAIL_OTPS_USERS_USER_ID",
                        column: x => x.USER_ID,
                        principalTable: "USERS",
                        principalColumn: "USER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_EMAIL_OTPS_USER_ID_PURPOSE",
                table: "EMAIL_OTPS",
                columns: new[] { "USER_ID", "PURPOSE" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "EMAIL_OTPS");

            migrationBuilder.DropColumn(
                name: "EMAIL_VERIFICATION_PENDING",
                table: "USERS");
        }
    }
}
