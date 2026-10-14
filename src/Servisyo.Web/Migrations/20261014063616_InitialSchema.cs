using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Servisyo.Web.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "SERVICE_CATEGORIES",
                columns: table => new
                {
                    CATEGORY_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CATEGORY_NAME = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DESCRIPTION = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    IS_ACTIVE = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SERVICE_CATEGORIES", x => x.CATEGORY_ID);
                });

            migrationBuilder.CreateTable(
                name: "USERS",
                columns: table => new
                {
                    USER_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FIRST_NAME = table.Column<string>(type: "nvarchar(155)", maxLength: 155, nullable: false),
                    MIDDLE_NAME = table.Column<string>(type: "nvarchar(155)", maxLength: 155, nullable: true),
                    LAST_NAME = table.Column<string>(type: "nvarchar(155)", maxLength: 155, nullable: false),
                    EMAIL = table.Column<string>(type: "nvarchar(155)", maxLength: 155, nullable: false),
                    PASSWORD_HASH = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    PHONE_NUMBER = table.Column<string>(type: "nvarchar(15)", maxLength: 15, nullable: true),
                    ROLE = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    ACCOUNT_STATUS = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_USERS", x => x.USER_ID);
                    table.CheckConstraint("CK_USERS_ROLE", "[ROLE] IN ('Customer','Service Provider','Administrator')");
                    table.CheckConstraint("CK_USERS_STATUS", "[ACCOUNT_STATUS] IN ('Active','Suspended','Deactivated')");
                });

            migrationBuilder.CreateTable(
                name: "ADMIN_ACTIONS",
                columns: table => new
                {
                    ACTION_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ADMIN_USER_ID = table.Column<int>(type: "int", nullable: false),
                    ACTION_TYPE = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TARGET_TYPE = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    TARGET_ID = table.Column<int>(type: "int", nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ADMIN_ACTIONS", x => x.ACTION_ID);
                    table.ForeignKey(
                        name: "FK_ADMIN_ACTIONS_USERS_ADMIN_USER_ID",
                        column: x => x.ADMIN_USER_ID,
                        principalTable: "USERS",
                        principalColumn: "USER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CUSTOMERS",
                columns: table => new
                {
                    CUSTOMER_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    USER_ID = table.Column<int>(type: "int", nullable: false),
                    ADDRESS = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CITY = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PROFILE_PHOTO = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CUSTOMERS", x => x.CUSTOMER_ID);
                    table.ForeignKey(
                        name: "FK_CUSTOMERS_USERS_USER_ID",
                        column: x => x.USER_ID,
                        principalTable: "USERS",
                        principalColumn: "USER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "MESSAGES",
                columns: table => new
                {
                    MESSAGE_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SENDER_ID = table.Column<int>(type: "int", nullable: false),
                    RECEIVER_ID = table.Column<int>(type: "int", nullable: false),
                    MESSAGE_TEXT = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    IS_READ = table.Column<bool>(type: "bit", nullable: false),
                    SENT_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MESSAGES", x => x.MESSAGE_ID);
                    table.ForeignKey(
                        name: "FK_MESSAGES_USERS_RECEIVER_ID",
                        column: x => x.RECEIVER_ID,
                        principalTable: "USERS",
                        principalColumn: "USER_ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_MESSAGES_USERS_SENDER_ID",
                        column: x => x.SENDER_ID,
                        principalTable: "USERS",
                        principalColumn: "USER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "NOTIFICATIONS",
                columns: table => new
                {
                    NOTIFICATION_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    USER_ID = table.Column<int>(type: "int", nullable: false),
                    MESSAGE = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    IS_READ = table.Column<bool>(type: "bit", nullable: false),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_NOTIFICATIONS", x => x.NOTIFICATION_ID);
                    table.ForeignKey(
                        name: "FK_NOTIFICATIONS_USERS_USER_ID",
                        column: x => x.USER_ID,
                        principalTable: "USERS",
                        principalColumn: "USER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SERVICE_PROVIDERS",
                columns: table => new
                {
                    PROVIDER_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    USER_ID = table.Column<int>(type: "int", nullable: false),
                    BUSINESS_NAME = table.Column<string>(type: "nvarchar(155)", maxLength: 155, nullable: false),
                    DESCRIPTION = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PROFILE_PHOTO = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    ADDRESS = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CITY = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: true),
                    BASE_FEE = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: true),
                    AVG_RATING = table.Column<decimal>(type: "decimal(2,1)", precision: 2, scale: 1, nullable: true),
                    REVIEW_COUNT = table.Column<int>(type: "int", nullable: false),
                    MIN_NOTICE_DAYS = table.Column<int>(type: "int", nullable: false),
                    MAX_JOBS_PER_DAY = table.Column<int>(type: "int", nullable: false),
                    APPROVAL_STATUS = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    DATE_APPLIED = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    DATE_APPROVED = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SERVICE_PROVIDERS", x => x.PROVIDER_ID);
                    table.CheckConstraint("CK_PROVIDERS_APPROVAL", "[APPROVAL_STATUS] IN ('Pending','Approved','Rejected')");
                    table.CheckConstraint("CK_PROVIDERS_LIMITS", "[MIN_NOTICE_DAYS] >= 0 AND [MAX_JOBS_PER_DAY] > 0");
                    table.CheckConstraint("CK_PROVIDERS_RATING", "[AVG_RATING] IS NULL OR [AVG_RATING] BETWEEN 0 AND 5");
                    table.ForeignKey(
                        name: "FK_SERVICE_PROVIDERS_USERS_USER_ID",
                        column: x => x.USER_ID,
                        principalTable: "USERS",
                        principalColumn: "USER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BOOKINGS",
                columns: table => new
                {
                    BOOKING_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CUSTOMER_ID = table.Column<int>(type: "int", nullable: false),
                    PROVIDER_ID = table.Column<int>(type: "int", nullable: false),
                    BOOKING_DATE = table.Column<DateOnly>(type: "date", nullable: false),
                    BOOKING_TIME = table.Column<TimeOnly>(type: "time", nullable: false),
                    TOTAL_DURATION_MINUTES = table.Column<int>(type: "int", nullable: false),
                    END_TIME = table.Column<TimeOnly>(type: "time", nullable: false, computedColumnSql: "DATEADD(MINUTE, [TOTAL_DURATION_MINUTES], [BOOKING_TIME])"),
                    SERVICE_LOCATION = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    SERVICE_DESCRIPTION = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    TOTAL_FEE = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    PAYMENT_STATUS = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    UPDATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    ROW_VERSION = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BOOKINGS", x => x.BOOKING_ID);
                    table.CheckConstraint("CK_BOOKINGS_DURATION", "[TOTAL_DURATION_MINUTES] > 0");
                    table.CheckConstraint("CK_BOOKINGS_PAYMENT", "[PAYMENT_STATUS] IN ('Unpaid','Paid','Refunded')");
                    table.CheckConstraint("CK_BOOKINGS_STATUS", "[STATUS] IN ('Pending','Accepted','Rejected','In Progress','Completed','Cancelled')");
                    table.ForeignKey(
                        name: "FK_BOOKINGS_CUSTOMERS_CUSTOMER_ID",
                        column: x => x.CUSTOMER_ID,
                        principalTable: "CUSTOMERS",
                        principalColumn: "CUSTOMER_ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BOOKINGS_SERVICE_PROVIDERS_PROVIDER_ID",
                        column: x => x.PROVIDER_ID,
                        principalTable: "SERVICE_PROVIDERS",
                        principalColumn: "PROVIDER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PROVIDER_AVAILABILITY",
                columns: table => new
                {
                    AVAILABILITY_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PROVIDER_ID = table.Column<int>(type: "int", nullable: false),
                    DAY_OF_WEEK = table.Column<byte>(type: "tinyint", nullable: false),
                    IS_AVAILABLE = table.Column<bool>(type: "bit", nullable: false),
                    START_TIME = table.Column<TimeOnly>(type: "time", nullable: false),
                    END_TIME = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROVIDER_AVAILABILITY", x => x.AVAILABILITY_ID);
                    table.CheckConstraint("CK_AVAIL_DAY", "[DAY_OF_WEEK] BETWEEN 0 AND 6");
                    table.CheckConstraint("CK_AVAIL_TIMES", "[END_TIME] > [START_TIME]");
                    table.ForeignKey(
                        name: "FK_PROVIDER_AVAILABILITY_SERVICE_PROVIDERS_PROVIDER_ID",
                        column: x => x.PROVIDER_ID,
                        principalTable: "SERVICE_PROVIDERS",
                        principalColumn: "PROVIDER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PROVIDER_CATEGORIES",
                columns: table => new
                {
                    PROVIDER_CATEGORY_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PROVIDER_ID = table.Column<int>(type: "int", nullable: false),
                    CATEGORY_ID = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROVIDER_CATEGORIES", x => x.PROVIDER_CATEGORY_ID);
                    table.ForeignKey(
                        name: "FK_PROVIDER_CATEGORIES_SERVICE_CATEGORIES_CATEGORY_ID",
                        column: x => x.CATEGORY_ID,
                        principalTable: "SERVICE_CATEGORIES",
                        principalColumn: "CATEGORY_ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PROVIDER_CATEGORIES_SERVICE_PROVIDERS_PROVIDER_ID",
                        column: x => x.PROVIDER_ID,
                        principalTable: "SERVICE_PROVIDERS",
                        principalColumn: "PROVIDER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PROVIDER_UNAVAILABLE_DATES",
                columns: table => new
                {
                    UNAVAILABLE_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PROVIDER_ID = table.Column<int>(type: "int", nullable: false),
                    UNAVAILABLE_DATE = table.Column<DateOnly>(type: "date", nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROVIDER_UNAVAILABLE_DATES", x => x.UNAVAILABLE_ID);
                    table.ForeignKey(
                        name: "FK_PROVIDER_UNAVAILABLE_DATES_SERVICE_PROVIDERS_PROVIDER_ID",
                        column: x => x.PROVIDER_ID,
                        principalTable: "SERVICE_PROVIDERS",
                        principalColumn: "PROVIDER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SERVICES",
                columns: table => new
                {
                    SERVICE_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    PROVIDER_ID = table.Column<int>(type: "int", nullable: false),
                    CATEGORY_ID = table.Column<int>(type: "int", nullable: false),
                    SERVICE_NAME = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    DESCRIPTION = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    PRICE = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DURATION_MINUTES = table.Column<int>(type: "int", nullable: false),
                    IS_ACTIVE = table.Column<bool>(type: "bit", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SERVICES", x => x.SERVICE_ID);
                    table.CheckConstraint("CK_SERVICES_DURATION", "[DURATION_MINUTES] > 0");
                    table.CheckConstraint("CK_SERVICES_PRICE", "[PRICE] >= 0");
                    table.ForeignKey(
                        name: "FK_SERVICES_SERVICE_CATEGORIES_CATEGORY_ID",
                        column: x => x.CATEGORY_ID,
                        principalTable: "SERVICE_CATEGORIES",
                        principalColumn: "CATEGORY_ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SERVICES_SERVICE_PROVIDERS_PROVIDER_ID",
                        column: x => x.PROVIDER_ID,
                        principalTable: "SERVICE_PROVIDERS",
                        principalColumn: "PROVIDER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "BOOKING_REPORTS",
                columns: table => new
                {
                    REPORT_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BOOKING_ID = table.Column<int>(type: "int", nullable: false),
                    REPORTED_BY_USER_ID = table.Column<int>(type: "int", nullable: false),
                    REASON = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: false),
                    STATUS = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    RESOLVED_BY_USER_ID = table.Column<int>(type: "int", nullable: true),
                    RESOLUTION_NOTE = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    RESOLVED_AT = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BOOKING_REPORTS", x => x.REPORT_ID);
                    table.CheckConstraint("CK_REPORTS_STATUS", "[STATUS] IN ('Open','Under Review','Resolved')");
                    table.ForeignKey(
                        name: "FK_BOOKING_REPORTS_BOOKINGS_BOOKING_ID",
                        column: x => x.BOOKING_ID,
                        principalTable: "BOOKINGS",
                        principalColumn: "BOOKING_ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BOOKING_REPORTS_USERS_REPORTED_BY_USER_ID",
                        column: x => x.REPORTED_BY_USER_ID,
                        principalTable: "USERS",
                        principalColumn: "USER_ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BOOKING_REPORTS_USERS_RESOLVED_BY_USER_ID",
                        column: x => x.RESOLVED_BY_USER_ID,
                        principalTable: "USERS",
                        principalColumn: "USER_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "REVIEWS",
                columns: table => new
                {
                    REVIEW_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BOOKING_ID = table.Column<int>(type: "int", nullable: false),
                    RATING = table.Column<byte>(type: "tinyint", nullable: false),
                    COMMENT = table.Column<string>(type: "nvarchar(255)", maxLength: 255, nullable: true),
                    CREATED_AT = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "SYSUTCDATETIME()"),
                    STATUS = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_REVIEWS", x => x.REVIEW_ID);
                    table.CheckConstraint("CK_REVIEWS_RATING", "[RATING] BETWEEN 1 AND 5");
                    table.CheckConstraint("CK_REVIEWS_STATUS", "[STATUS] IN ('Visible','Flagged','Removed')");
                    table.ForeignKey(
                        name: "FK_REVIEWS_BOOKINGS_BOOKING_ID",
                        column: x => x.BOOKING_ID,
                        principalTable: "BOOKINGS",
                        principalColumn: "BOOKING_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PROVIDER_BREAKS",
                columns: table => new
                {
                    BREAK_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AVAILABILITY_ID = table.Column<int>(type: "int", nullable: false),
                    BREAK_START = table.Column<TimeOnly>(type: "time", nullable: false),
                    BREAK_END = table.Column<TimeOnly>(type: "time", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PROVIDER_BREAKS", x => x.BREAK_ID);
                    table.CheckConstraint("CK_BREAK_TIMES", "[BREAK_END] > [BREAK_START]");
                    table.ForeignKey(
                        name: "FK_PROVIDER_BREAKS_PROVIDER_AVAILABILITY_AVAILABILITY_ID",
                        column: x => x.AVAILABILITY_ID,
                        principalTable: "PROVIDER_AVAILABILITY",
                        principalColumn: "AVAILABILITY_ID",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BOOKING_SERVICES",
                columns: table => new
                {
                    BOOKING_SERVICE_ID = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BOOKING_ID = table.Column<int>(type: "int", nullable: false),
                    SERVICE_ID = table.Column<int>(type: "int", nullable: false),
                    PRICE_SNAPSHOT = table.Column<decimal>(type: "decimal(10,2)", precision: 10, scale: 2, nullable: false),
                    DURATION_MINUTES_SNAPSHOT = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BOOKING_SERVICES", x => x.BOOKING_SERVICE_ID);
                    table.ForeignKey(
                        name: "FK_BOOKING_SERVICES_BOOKINGS_BOOKING_ID",
                        column: x => x.BOOKING_ID,
                        principalTable: "BOOKINGS",
                        principalColumn: "BOOKING_ID",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BOOKING_SERVICES_SERVICES_SERVICE_ID",
                        column: x => x.SERVICE_ID,
                        principalTable: "SERVICES",
                        principalColumn: "SERVICE_ID",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ADMIN_ACTIONS_ADMIN_USER_ID",
                table: "ADMIN_ACTIONS",
                column: "ADMIN_USER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_REPORTS_BOOKING_ID",
                table: "BOOKING_REPORTS",
                column: "BOOKING_ID");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_REPORTS_REPORTED_BY_USER_ID",
                table: "BOOKING_REPORTS",
                column: "REPORTED_BY_USER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_REPORTS_RESOLVED_BY_USER_ID",
                table: "BOOKING_REPORTS",
                column: "RESOLVED_BY_USER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_SERVICES_BOOKING_ID_SERVICE_ID",
                table: "BOOKING_SERVICES",
                columns: new[] { "BOOKING_ID", "SERVICE_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BOOKING_SERVICES_SERVICE_ID",
                table: "BOOKING_SERVICES",
                column: "SERVICE_ID");

            migrationBuilder.CreateIndex(
                name: "IX_BOOKINGS_CUSTOMER_ID",
                table: "BOOKINGS",
                column: "CUSTOMER_ID");

            migrationBuilder.CreateIndex(
                name: "UX_BOOKINGS_ACTIVE_SLOT",
                table: "BOOKINGS",
                columns: new[] { "PROVIDER_ID", "BOOKING_DATE", "BOOKING_TIME" },
                unique: true,
                filter: "[STATUS] IN ('Pending','Accepted','In Progress')");

            migrationBuilder.CreateIndex(
                name: "IX_CUSTOMERS_USER_ID",
                table: "CUSTOMERS",
                column: "USER_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_MESSAGES_RECEIVER_ID",
                table: "MESSAGES",
                column: "RECEIVER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_MESSAGES_SENDER_ID",
                table: "MESSAGES",
                column: "SENDER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_NOTIFICATIONS_USER_ID",
                table: "NOTIFICATIONS",
                column: "USER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_PROVIDER_AVAILABILITY_PROVIDER_ID_DAY_OF_WEEK",
                table: "PROVIDER_AVAILABILITY",
                columns: new[] { "PROVIDER_ID", "DAY_OF_WEEK" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PROVIDER_BREAKS_AVAILABILITY_ID",
                table: "PROVIDER_BREAKS",
                column: "AVAILABILITY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_PROVIDER_CATEGORIES_CATEGORY_ID",
                table: "PROVIDER_CATEGORIES",
                column: "CATEGORY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_PROVIDER_CATEGORIES_PROVIDER_ID_CATEGORY_ID",
                table: "PROVIDER_CATEGORIES",
                columns: new[] { "PROVIDER_ID", "CATEGORY_ID" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PROVIDER_UNAVAILABLE_DATES_PROVIDER_ID_UNAVAILABLE_DATE",
                table: "PROVIDER_UNAVAILABLE_DATES",
                columns: new[] { "PROVIDER_ID", "UNAVAILABLE_DATE" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_REVIEWS_BOOKING_ID",
                table: "REVIEWS",
                column: "BOOKING_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SERVICE_CATEGORIES_CATEGORY_NAME",
                table: "SERVICE_CATEGORIES",
                column: "CATEGORY_NAME",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SERVICE_PROVIDERS_USER_ID",
                table: "SERVICE_PROVIDERS",
                column: "USER_ID",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SERVICES_CATEGORY_ID",
                table: "SERVICES",
                column: "CATEGORY_ID");

            migrationBuilder.CreateIndex(
                name: "IX_SERVICES_PROVIDER_ID",
                table: "SERVICES",
                column: "PROVIDER_ID");

            migrationBuilder.CreateIndex(
                name: "IX_USERS_EMAIL",
                table: "USERS",
                column: "EMAIL",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ADMIN_ACTIONS");

            migrationBuilder.DropTable(
                name: "BOOKING_REPORTS");

            migrationBuilder.DropTable(
                name: "BOOKING_SERVICES");

            migrationBuilder.DropTable(
                name: "MESSAGES");

            migrationBuilder.DropTable(
                name: "NOTIFICATIONS");

            migrationBuilder.DropTable(
                name: "PROVIDER_BREAKS");

            migrationBuilder.DropTable(
                name: "PROVIDER_CATEGORIES");

            migrationBuilder.DropTable(
                name: "PROVIDER_UNAVAILABLE_DATES");

            migrationBuilder.DropTable(
                name: "REVIEWS");

            migrationBuilder.DropTable(
                name: "SERVICES");

            migrationBuilder.DropTable(
                name: "PROVIDER_AVAILABILITY");

            migrationBuilder.DropTable(
                name: "BOOKINGS");

            migrationBuilder.DropTable(
                name: "SERVICE_CATEGORIES");

            migrationBuilder.DropTable(
                name: "CUSTOMERS");

            migrationBuilder.DropTable(
                name: "SERVICE_PROVIDERS");

            migrationBuilder.DropTable(
                name: "USERS");
        }
    }
}
