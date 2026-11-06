using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class AdminTargets : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // database v2.2: save each ADMIN_ACTIONS row's new target before EF removes TARGET_ID
            migrationBuilder.Sql(Servisyo.Web.Data.AdminTargetsSql.StageTargets);

            migrationBuilder.DropColumn(
                name: "TARGET_ID",
                table: "ADMIN_ACTIONS");

            migrationBuilder.AddColumn<int>(
                name: "TARGET_CATEGORY_ID",
                table: "ADMIN_ACTIONS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TARGET_CUSTOMER_ID",
                table: "ADMIN_ACTIONS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TARGET_PROVIDER_ID",
                table: "ADMIN_ACTIONS",
                type: "int",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "TARGET_REVIEW_ID",
                table: "ADMIN_ACTIONS",
                type: "int",
                nullable: true);

            // database v2.2: fill the new target columns (the FOREIGN KEYs and CHECKs below need them)
            migrationBuilder.Sql(Servisyo.Web.Data.AdminTargetsSql.FillTargets);

            migrationBuilder.CreateIndex(
                name: "IX_ADMIN_ACTIONS_TARGET_CATEGORY_ID",
                table: "ADMIN_ACTIONS",
                column: "TARGET_CATEGORY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ADMIN_ACTIONS_TARGET_CUSTOMER_ID",
                table: "ADMIN_ACTIONS",
                column: "TARGET_CUSTOMER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ADMIN_ACTIONS_TARGET_PROVIDER_ID",
                table: "ADMIN_ACTIONS",
                column: "TARGET_PROVIDER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_ADMIN_ACTIONS_TARGET_REVIEW_ID",
                table: "ADMIN_ACTIONS",
                column: "TARGET_REVIEW_ID");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ADMIN_ACTIONS_TARGET",
                table: "ADMIN_ACTIONS",
                sql: "([TARGET_TYPE] = 'CUSTOMER' AND [TARGET_CUSTOMER_ID] IS NOT NULL AND [TARGET_PROVIDER_ID] IS NULL AND [TARGET_REVIEW_ID] IS NULL AND [TARGET_CATEGORY_ID] IS NULL) OR ([TARGET_TYPE] = 'SERVICE_PROVIDER' AND [TARGET_CUSTOMER_ID] IS NULL AND [TARGET_PROVIDER_ID] IS NOT NULL AND [TARGET_REVIEW_ID] IS NULL AND [TARGET_CATEGORY_ID] IS NULL) OR ([TARGET_TYPE] = 'REVIEW' AND [TARGET_CUSTOMER_ID] IS NULL AND [TARGET_PROVIDER_ID] IS NULL AND [TARGET_REVIEW_ID] IS NOT NULL AND [TARGET_CATEGORY_ID] IS NULL) OR ([TARGET_TYPE] = 'CATEGORY' AND [TARGET_CUSTOMER_ID] IS NULL AND [TARGET_PROVIDER_ID] IS NULL AND [TARGET_REVIEW_ID] IS NULL AND [TARGET_CATEGORY_ID] IS NOT NULL)");

            migrationBuilder.AddCheckConstraint(
                name: "CK_ADMIN_ACTIONS_TARGET_TYPE",
                table: "ADMIN_ACTIONS",
                sql: "[TARGET_TYPE] IN ('CUSTOMER','SERVICE_PROVIDER','REVIEW','CATEGORY')");

            migrationBuilder.AddForeignKey(
                name: "FK_ADMIN_ACTIONS_CUSTOMERS_TARGET_CUSTOMER_ID",
                table: "ADMIN_ACTIONS",
                column: "TARGET_CUSTOMER_ID",
                principalTable: "CUSTOMERS",
                principalColumn: "CUSTOMER_ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ADMIN_ACTIONS_REVIEWS_TARGET_REVIEW_ID",
                table: "ADMIN_ACTIONS",
                column: "TARGET_REVIEW_ID",
                principalTable: "REVIEWS",
                principalColumn: "REVIEW_ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ADMIN_ACTIONS_SERVICE_CATEGORIES_TARGET_CATEGORY_ID",
                table: "ADMIN_ACTIONS",
                column: "TARGET_CATEGORY_ID",
                principalTable: "SERVICE_CATEGORIES",
                principalColumn: "CATEGORY_ID",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_ADMIN_ACTIONS_SERVICE_PROVIDERS_TARGET_PROVIDER_ID",
                table: "ADMIN_ACTIONS",
                column: "TARGET_PROVIDER_ID",
                principalTable: "SERVICE_PROVIDERS",
                principalColumn: "PROVIDER_ID",
                onDelete: ReferentialAction.Restrict);
        
            // database v2.2: usp_ApproveProvider / usp_UpdateAccountStatus / usp_ModerateReview (docs\sql\DatabaseDesign-v2.sql)
            foreach (var sql in Servisyo.Web.Data.AdminTargetsSql.CreateAll)
                migrationBuilder.Sql(sql);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(Servisyo.Web.Data.AdminTargetsSql.StageOld);

            migrationBuilder.DropForeignKey(
                name: "FK_ADMIN_ACTIONS_CUSTOMERS_TARGET_CUSTOMER_ID",
                table: "ADMIN_ACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_ADMIN_ACTIONS_REVIEWS_TARGET_REVIEW_ID",
                table: "ADMIN_ACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_ADMIN_ACTIONS_SERVICE_CATEGORIES_TARGET_CATEGORY_ID",
                table: "ADMIN_ACTIONS");

            migrationBuilder.DropForeignKey(
                name: "FK_ADMIN_ACTIONS_SERVICE_PROVIDERS_TARGET_PROVIDER_ID",
                table: "ADMIN_ACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_ADMIN_ACTIONS_TARGET_CATEGORY_ID",
                table: "ADMIN_ACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_ADMIN_ACTIONS_TARGET_CUSTOMER_ID",
                table: "ADMIN_ACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_ADMIN_ACTIONS_TARGET_PROVIDER_ID",
                table: "ADMIN_ACTIONS");

            migrationBuilder.DropIndex(
                name: "IX_ADMIN_ACTIONS_TARGET_REVIEW_ID",
                table: "ADMIN_ACTIONS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ADMIN_ACTIONS_TARGET",
                table: "ADMIN_ACTIONS");

            migrationBuilder.DropCheckConstraint(
                name: "CK_ADMIN_ACTIONS_TARGET_TYPE",
                table: "ADMIN_ACTIONS");

            migrationBuilder.DropColumn(
                name: "TARGET_CATEGORY_ID",
                table: "ADMIN_ACTIONS");

            migrationBuilder.DropColumn(
                name: "TARGET_CUSTOMER_ID",
                table: "ADMIN_ACTIONS");

            migrationBuilder.DropColumn(
                name: "TARGET_PROVIDER_ID",
                table: "ADMIN_ACTIONS");

            migrationBuilder.DropColumn(
                name: "TARGET_REVIEW_ID",
                table: "ADMIN_ACTIONS");

            migrationBuilder.AddColumn<int>(
                name: "TARGET_ID",
                table: "ADMIN_ACTIONS",
                type: "int",
                nullable: false,
                defaultValue: 0);
        
            migrationBuilder.Sql(Servisyo.Web.Data.AdminTargetsSql.FillOld);
            foreach (var sql in Servisyo.Web.Data.AdminTargetsSql.RestoreV21)
                migrationBuilder.Sql(sql);
        }
    }
}
