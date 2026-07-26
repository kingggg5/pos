using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace SmartPos.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class BusinessTimeEventsPartialRefund : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_OrderReversals_TenantId_OrderId",
                table: "OrderReversals");

            migrationBuilder.AddColumn<string>(
                name: "BusinessTimeZoneId",
                table: "Tenants",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "Asia/Bangkok");

            migrationBuilder.AddColumn<decimal>(
                name: "TotalRefundedAmount",
                table: "Orders",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                table: "OrderReversals",
                type: "character varying(100)",
                maxLength: 100,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<decimal>(
                name: "CouponDiscountAmount",
                table: "OrderReversals",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<bool>(
                name: "CouponUsageReleased",
                table: "OrderReversals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "IsFullOrderReversal",
                table: "OrderReversals",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<decimal>(
                name: "LoyaltyDiscountAmount",
                table: "OrderReversals",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "LoyaltyPointsEarnedReversed",
                table: "OrderReversals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "LoyaltyPointsRedeemedRestored",
                table: "OrderReversals",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<decimal>(
                name: "ManualDiscountAmount",
                table: "OrderReversals",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<string>(
                name: "RequestFingerprint",
                table: "OrderReversals",
                type: "character varying(64)",
                maxLength: 64,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<decimal>(
                name: "ServiceChargeAmount",
                table: "OrderReversals",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "SubTotalAmount",
                table: "OrderReversals",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<decimal>(
                name: "VatAmount",
                table: "OrderReversals",
                type: "numeric(18,2)",
                precision: 18,
                scale: 2,
                nullable: false,
                defaultValue: 0m);

            migrationBuilder.AddColumn<int>(
                name: "RefundedQuantity",
                table: "OrderItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<DateTime>(
                name: "ReversedAt",
                table: "CouponRedemptions",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "FinancialEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    Type = table.Column<int>(type: "integer", nullable: false),
                    OrderId = table.Column<int>(type: "integer", nullable: false),
                    OrderReversalId = table.Column<int>(type: "integer", nullable: true),
                    SourceKey = table.Column<string>(type: "character varying(140)", maxLength: 140, nullable: false),
                    Amount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    PaymentMethod = table.Column<int>(type: "integer", nullable: false),
                    CashShiftId = table.Column<int>(type: "integer", nullable: true),
                    OccurredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinancialEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinancialEvents_CashShifts_CashShiftId",
                        column: x => x.CashShiftId,
                        principalTable: "CashShifts",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinancialEvents_OrderReversals_OrderReversalId",
                        column: x => x.OrderReversalId,
                        principalTable: "OrderReversals",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FinancialEvents_Orders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "Orders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OrderReversalItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TenantId = table.Column<int>(type: "integer", nullable: false),
                    OrderReversalId = table.Column<int>(type: "integer", nullable: false),
                    OrderItemId = table.Column<int>(type: "integer", nullable: false),
                    ProductId = table.Column<int>(type: "integer", nullable: false),
                    ProductName = table.Column<string>(type: "text", nullable: false),
                    Quantity = table.Column<int>(type: "integer", nullable: false),
                    SubTotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ManualDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    CouponDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    LoyaltyDiscountAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    ServiceChargeAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    VatAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false),
                    TotalAmount = table.Column<decimal>(type: "numeric(18,2)", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OrderReversalItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OrderReversalItems_OrderItems_OrderItemId",
                        column: x => x.OrderItemId,
                        principalTable: "OrderItems",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderReversalItems_OrderReversals_OrderReversalId",
                        column: x => x.OrderReversalId,
                        principalTable: "OrderReversals",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_OrderReversalItems_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_OrderReversals_TenantId_OrderId",
                table: "OrderReversals",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_OrderItems_TenantId_OrderId",
                table: "OrderItems",
                columns: new[] { "TenantId", "OrderId" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvents_CashShiftId",
                table: "FinancialEvents",
                column: "CashShiftId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvents_OrderId",
                table: "FinancialEvents",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvents_OrderReversalId",
                table: "FinancialEvents",
                column: "OrderReversalId");

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvents_TenantId_OccurredAt",
                table: "FinancialEvents",
                columns: new[] { "TenantId", "OccurredAt" });

            migrationBuilder.CreateIndex(
                name: "IX_FinancialEvents_TenantId_SourceKey",
                table: "FinancialEvents",
                columns: new[] { "TenantId", "SourceKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OrderReversalItems_OrderItemId",
                table: "OrderReversalItems",
                column: "OrderItemId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderReversalItems_OrderReversalId",
                table: "OrderReversalItems",
                column: "OrderReversalId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderReversalItems_ProductId",
                table: "OrderReversalItems",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OrderReversalItems_TenantId_OrderReversalId_OrderItemId",
                table: "OrderReversalItems",
                columns: new[] { "TenantId", "OrderReversalId", "OrderItemId" });

            migrationBuilder.Sql(
                """
                UPDATE "OrderItems" AS i
                SET "RefundedQuantity" = i."Quantity"
                FROM "Orders" AS o
                WHERE i."OrderId" = o."Id" AND o."Status" IN (1, 2);

                UPDATE "Orders"
                SET "TotalRefundedAmount" = "TotalAmount"
                WHERE "Status" IN (1, 2);

                UPDATE "OrderReversals" AS r
                SET "SubTotalAmount" = r."Amount",
                    "IsFullOrderReversal" = TRUE,
                    "LoyaltyPointsEarnedReversed" = o."LoyaltyPointsEarned",
                    "LoyaltyPointsRedeemedRestored" = o."LoyaltyPointsRedeemed",
                    "CouponUsageReleased" = o."CouponCode" IS NOT NULL
                FROM "Orders" AS o
                WHERE r."OrderId" = o."Id";

                UPDATE "CouponRedemptions" AS c
                SET "ReversedAt" = r."ProcessedAt"
                FROM "OrderReversals" AS r
                WHERE c."OrderId" = r."OrderId";

                INSERT INTO "FinancialEvents"
                    ("TenantId", "Type", "OrderId", "OrderReversalId", "SourceKey", "Amount",
                     "PaymentMethod", "CashShiftId", "OccurredAt", "Description")
                SELECT o."TenantId", 0, o."Id", NULL, 'LEGACY-SALE:' || o."Id", o."TotalAmount",
                    o."PaymentMethod", o."CashShiftId", o."CreatedAt", 'Legacy sale ' || o."OrderNo"
                FROM "Orders" AS o;

                INSERT INTO "FinancialEvents"
                    ("TenantId", "Type", "OrderId", "OrderReversalId", "SourceKey", "Amount",
                     "PaymentMethod", "CashShiftId", "OccurredAt", "Description")
                SELECT r."TenantId", CASE WHEN r."Type" = 0 THEN 2 ELSE 1 END, r."OrderId", r."Id",
                    'LEGACY-REVERSAL:' || r."Id", -r."Amount", o."PaymentMethod",
                    r."CashShiftId", r."ProcessedAt", 'Legacy reversal ' || o."OrderNo"
                FROM "OrderReversals" AS r
                INNER JOIN "Orders" AS o ON o."Id" = r."OrderId";
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinancialEvents");

            migrationBuilder.DropTable(
                name: "OrderReversalItems");

            migrationBuilder.DropIndex(
                name: "IX_OrderReversals_TenantId_OrderId",
                table: "OrderReversals");

            migrationBuilder.DropIndex(
                name: "IX_OrderItems_TenantId_OrderId",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "BusinessTimeZoneId",
                table: "Tenants");

            migrationBuilder.DropColumn(
                name: "TotalRefundedAmount",
                table: "Orders");

            migrationBuilder.DropColumn(
                name: "CouponDiscountAmount",
                table: "OrderReversals");

            migrationBuilder.DropColumn(
                name: "CouponUsageReleased",
                table: "OrderReversals");

            migrationBuilder.DropColumn(
                name: "IsFullOrderReversal",
                table: "OrderReversals");

            migrationBuilder.DropColumn(
                name: "LoyaltyDiscountAmount",
                table: "OrderReversals");

            migrationBuilder.DropColumn(
                name: "LoyaltyPointsEarnedReversed",
                table: "OrderReversals");

            migrationBuilder.DropColumn(
                name: "LoyaltyPointsRedeemedRestored",
                table: "OrderReversals");

            migrationBuilder.DropColumn(
                name: "ManualDiscountAmount",
                table: "OrderReversals");

            migrationBuilder.DropColumn(
                name: "RequestFingerprint",
                table: "OrderReversals");

            migrationBuilder.DropColumn(
                name: "ServiceChargeAmount",
                table: "OrderReversals");

            migrationBuilder.DropColumn(
                name: "SubTotalAmount",
                table: "OrderReversals");

            migrationBuilder.DropColumn(
                name: "VatAmount",
                table: "OrderReversals");

            migrationBuilder.DropColumn(
                name: "RefundedQuantity",
                table: "OrderItems");

            migrationBuilder.DropColumn(
                name: "ReversedAt",
                table: "CouponRedemptions");

            migrationBuilder.AlterColumn<string>(
                name: "IdempotencyKey",
                table: "OrderReversals",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(100)",
                oldMaxLength: 100);

            migrationBuilder.CreateIndex(
                name: "IX_OrderReversals_TenantId_OrderId",
                table: "OrderReversals",
                columns: new[] { "TenantId", "OrderId" },
                unique: true);
        }
    }
}
