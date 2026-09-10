using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Ovcuprim.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class Phase9PaymentsAndPromotions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PromotionPackages",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Code = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    NameAz = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    DescriptionAz = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    Type = table.Column<short>(type: "smallint", nullable: false),
                    DurationDays = table.Column<int>(type: "integer", nullable: false),
                    PriceAzn = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "AZN"),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PromotionPackages", x => x.Id);
                    table.CheckConstraint("CK_PromotionPackages_Duration_Positive", "\"DurationDays\" > 0");
                    table.CheckConstraint("CK_PromotionPackages_Price_NonNegative", "\"PriceAzn\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "PaymentOrders",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    SellerUserId = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionPackageId = table.Column<int>(type: "integer", nullable: false),
                    AmountAzn = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: false),
                    Currency = table.Column<string>(type: "character(3)", fixedLength: true, maxLength: 3, nullable: false, defaultValue: "AZN"),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    Provider = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                    ProviderOrderReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentOrders", x => x.Id);
                    table.CheckConstraint("CK_PaymentOrders_Amount_NonNegative", "\"AmountAzn\" >= 0");
                    table.ForeignKey(
                        name: "FK_PaymentOrders_Listings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "Listings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentOrders_PromotionPackages_PromotionPackageId",
                        column: x => x.PromotionPackageId,
                        principalTable: "PromotionPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PaymentOrders_Users_SellerUserId",
                        column: x => x.SellerUserId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "PaymentTransactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PaymentOrderId = table.Column<Guid>(type: "uuid", nullable: false),
                    EventType = table.Column<short>(type: "smallint", nullable: false),
                    ProviderReference = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    ProviderStatusRaw = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    AmountAzn = table.Column<decimal>(type: "numeric(12,2)", precision: 12, scale: 2, nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PaymentTransactions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PaymentTransactions_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Promotions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ListingId = table.Column<Guid>(type: "uuid", nullable: false),
                    PromotionPackageId = table.Column<int>(type: "integer", nullable: false),
                    PaymentOrderId = table.Column<Guid>(type: "uuid", nullable: true),
                    Status = table.Column<short>(type: "smallint", nullable: false),
                    ActivatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    ReversedReason = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Promotions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Promotions_Listings_ListingId",
                        column: x => x.ListingId,
                        principalTable: "Listings",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Promotions_PaymentOrders_PaymentOrderId",
                        column: x => x.PaymentOrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Promotions_PromotionPackages_PromotionPackageId",
                        column: x => x.PromotionPackageId,
                        principalTable: "PromotionPackages",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_ListingId_Status",
                table: "PaymentOrders",
                columns: new[] { "ListingId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_PromotionPackageId",
                table: "PaymentOrders",
                column: "PromotionPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_Provider_ProviderOrderReference",
                table: "PaymentOrders",
                columns: new[] { "Provider", "ProviderOrderReference" },
                unique: true,
                filter: "\"ProviderOrderReference\" IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_SellerUserId_CreatedAt",
                table: "PaymentOrders",
                columns: new[] { "SellerUserId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentOrders_Status_ExpiresAt",
                table: "PaymentOrders",
                columns: new[] { "Status", "ExpiresAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_CallbackIdempotency",
                table: "PaymentTransactions",
                columns: new[] { "PaymentOrderId", "EventType", "ProviderReference" },
                unique: true,
                filter: "\"ProviderReference\" IS NOT NULL AND \"EventType\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_PaymentTransactions_CreatedAt",
                table: "PaymentTransactions",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_PromotionPackages_Code",
                table: "PromotionPackages",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_PromotionPackages_IsActive_SortOrder",
                table: "PromotionPackages",
                columns: new[] { "IsActive", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_ListingId_Active",
                table: "Promotions",
                column: "ListingId",
                unique: true,
                filter: "\"Status\" = 1");

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_ListingId_CreatedAt",
                table: "Promotions",
                columns: new[] { "ListingId", "CreatedAt" },
                descending: new[] { false, true });

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_PaymentOrderId",
                table: "Promotions",
                column: "PaymentOrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_PromotionPackageId",
                table: "Promotions",
                column: "PromotionPackageId");

            migrationBuilder.CreateIndex(
                name: "IX_Promotions_Status_ExpiresAt",
                table: "Promotions",
                columns: new[] { "Status", "ExpiresAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PaymentTransactions");

            migrationBuilder.DropTable(
                name: "Promotions");

            migrationBuilder.DropTable(
                name: "PaymentOrders");

            migrationBuilder.DropTable(
                name: "PromotionPackages");
        }
    }
}
