using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace SmartPos.Api.Data;

public static class SqliteSchemaUpgrader
{
	public static async Task UpgradeAsync(SmartPosDbContext dbContext, CancellationToken cancellationToken = default)
	{
		var connection = dbContext.Database.GetDbConnection();
		if (connection.State != System.Data.ConnectionState.Open)
			await connection.OpenAsync(cancellationToken);

		await EnsureColumnAsync(connection, "Products", "Version", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
		await EnsureColumnAsync(connection, "Tenants", "BusinessTimeZoneId", "TEXT NOT NULL DEFAULT 'Asia/Bangkok'", cancellationToken);
		await EnsureColumnAsync(connection, "OrderItems", "TenantId", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
		await EnsureColumnAsync(connection, "OrderItems", "RefundedQuantity", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "SubTotalAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "ServiceChargeAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "VatAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "ManualDiscountAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "CouponDiscountAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "LoyaltyDiscountAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "CustomerId", "INTEGER NULL", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "CouponCode", "TEXT NULL", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "LoyaltyPointsEarned", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "LoyaltyPointsRedeemed", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "CashShiftId", "INTEGER NULL", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "IdempotencyKey", "TEXT NULL", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "RequestFingerprint", "TEXT NULL", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "Version", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
		await EnsureColumnAsync(connection, "Orders", "TotalRefundedAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);

		await ExecuteAsync(connection, """
			UPDATE "OrderItems"
			SET "TenantId" = COALESCE((SELECT "TenantId" FROM "Orders" WHERE "Orders"."Id" = "OrderItems"."OrderId"), "TenantId")
			WHERE "TenantId" = 0;
			UPDATE "Orders"
			SET "SubTotalAmount" = "TotalAmount" + "DiscountAmount",
				"ManualDiscountAmount" = "DiscountAmount"
			WHERE "SubTotalAmount" = 0;
			UPDATE "OrderItems"
			SET "RefundedQuantity" = "Quantity"
			WHERE "OrderId" IN (SELECT "Id" FROM "Orders" WHERE "Status" IN (1, 2));
			UPDATE "Orders"
			SET "TotalRefundedAmount" = "TotalAmount"
			WHERE "Status" IN (1, 2) AND "TotalRefundedAmount" = 0;
			""", cancellationToken);

		await ExecuteAsync(connection, """
			CREATE TABLE IF NOT EXISTS "CashShifts" (
				"Id" INTEGER NOT NULL CONSTRAINT "PK_CashShifts" PRIMARY KEY AUTOINCREMENT,
				"TenantId" INTEGER NOT NULL, "Status" INTEGER NOT NULL, "OpenSlot" INTEGER NULL,
				"OpeningCash" TEXT NOT NULL, "CashSalesAmount" TEXT NOT NULL, "CashRefundAmount" TEXT NOT NULL,
				"ExpectedCash" TEXT NOT NULL, "ClosingCash" TEXT NULL, "Difference" TEXT NULL,
				"OpenedAt" TEXT NOT NULL, "ClosedAt" TEXT NULL, "OpenedByUserId" INTEGER NOT NULL,
				"OpenedByName" TEXT NOT NULL, "ClosedByUserId" INTEGER NULL, "ClosedByName" TEXT NULL,
				"OpeningNote" TEXT NOT NULL, "ClosingNote" TEXT NOT NULL, "OpenIdempotencyKey" TEXT NOT NULL,
				"CloseIdempotencyKey" TEXT NULL, "Version" INTEGER NOT NULL
			);
			CREATE TABLE IF NOT EXISTS "Customers" (
				"Id" INTEGER NOT NULL CONSTRAINT "PK_Customers" PRIMARY KEY AUTOINCREMENT,
				"TenantId" INTEGER NOT NULL, "Phone" TEXT NOT NULL, "PhoneNormalized" TEXT NOT NULL,
				"Name" TEXT NOT NULL, "Email" TEXT NULL, "PointsBalance" INTEGER NOT NULL,
				"LifetimePointsEarned" INTEGER NOT NULL, "LifetimePointsRedeemed" INTEGER NOT NULL,
				"CreatedAt" TEXT NOT NULL, "UpdatedAt" TEXT NOT NULL, "Version" INTEGER NOT NULL
			);
			CREATE TABLE IF NOT EXISTS "PromotionCoupons" (
				"Id" INTEGER NOT NULL CONSTRAINT "PK_PromotionCoupons" PRIMARY KEY AUTOINCREMENT,
				"TenantId" INTEGER NOT NULL, "Code" TEXT NOT NULL, "Name" TEXT NOT NULL,
				"Description" TEXT NOT NULL, "DiscountType" INTEGER NOT NULL, "Value" TEXT NOT NULL,
				"MinimumOrderAmount" TEXT NOT NULL, "MaximumDiscountAmount" TEXT NULL,
				"UsageLimit" INTEGER NULL, "UsageCount" INTEGER NOT NULL, "ValidFrom" TEXT NULL,
				"ValidUntil" TEXT NULL, "IsActive" INTEGER NOT NULL, "CreatedAt" TEXT NOT NULL,
				"UpdatedAt" TEXT NOT NULL, "Version" INTEGER NOT NULL
			);
			CREATE TABLE IF NOT EXISTS "LoyaltyTransactions" (
				"Id" INTEGER NOT NULL CONSTRAINT "PK_LoyaltyTransactions" PRIMARY KEY AUTOINCREMENT,
				"TenantId" INTEGER NOT NULL, "CustomerId" INTEGER NOT NULL, "OrderId" INTEGER NULL,
				"Type" INTEGER NOT NULL, "PointsChange" INTEGER NOT NULL, "BalanceAfter" INTEGER NOT NULL,
				"Description" TEXT NOT NULL, "CreatedAt" TEXT NOT NULL,
				CONSTRAINT "FK_LoyaltyTransactions_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id") ON DELETE CASCADE,
				CONSTRAINT "FK_LoyaltyTransactions_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id")
			);
			CREATE TABLE IF NOT EXISTS "CouponRedemptions" (
				"Id" INTEGER NOT NULL CONSTRAINT "PK_CouponRedemptions" PRIMARY KEY AUTOINCREMENT,
				"TenantId" INTEGER NOT NULL, "PromotionCouponId" INTEGER NOT NULL, "OrderId" INTEGER NOT NULL,
				"CustomerId" INTEGER NULL, "DiscountAmount" TEXT NOT NULL, "RedeemedAt" TEXT NOT NULL,
				"ReversedAt" TEXT NULL,
				CONSTRAINT "FK_CouponRedemptions_PromotionCoupons_PromotionCouponId" FOREIGN KEY ("PromotionCouponId") REFERENCES "PromotionCoupons" ("Id") ON DELETE CASCADE,
				CONSTRAINT "FK_CouponRedemptions_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE,
				CONSTRAINT "FK_CouponRedemptions_Customers_CustomerId" FOREIGN KEY ("CustomerId") REFERENCES "Customers" ("Id")
			);
			CREATE TABLE IF NOT EXISTS "OrderReversals" (
				"Id" INTEGER NOT NULL CONSTRAINT "PK_OrderReversals" PRIMARY KEY AUTOINCREMENT,
				"TenantId" INTEGER NOT NULL, "OrderId" INTEGER NOT NULL, "Type" INTEGER NOT NULL,
				"Amount" TEXT NOT NULL, "SubTotalAmount" TEXT NOT NULL DEFAULT '0',
				"ManualDiscountAmount" TEXT NOT NULL DEFAULT '0', "CouponDiscountAmount" TEXT NOT NULL DEFAULT '0',
				"LoyaltyDiscountAmount" TEXT NOT NULL DEFAULT '0', "ServiceChargeAmount" TEXT NOT NULL DEFAULT '0',
				"VatAmount" TEXT NOT NULL DEFAULT '0', "StockRestored" INTEGER NOT NULL,
				"IsFullOrderReversal" INTEGER NOT NULL DEFAULT 1, "Reason" TEXT NOT NULL,
				"IdempotencyKey" TEXT NOT NULL, "PerformedByUserId" INTEGER NOT NULL,
				"RequestFingerprint" TEXT NOT NULL DEFAULT '', "PerformedBy" TEXT NOT NULL,
				"CashShiftId" INTEGER NULL, "LoyaltyPointsEarnedReversed" INTEGER NOT NULL DEFAULT 0,
				"LoyaltyPointsRedeemedRestored" INTEGER NOT NULL DEFAULT 0,
				"CouponUsageReleased" INTEGER NOT NULL DEFAULT 0, "ProcessedAt" TEXT NOT NULL,
				CONSTRAINT "FK_OrderReversals_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE,
				CONSTRAINT "FK_OrderReversals_CashShifts_CashShiftId" FOREIGN KEY ("CashShiftId") REFERENCES "CashShifts" ("Id")
			);
			""", cancellationToken);

		await EnsureColumnAsync(connection, "CouponRedemptions", "ReversedAt", "TEXT NULL", cancellationToken);
		await EnsureColumnAsync(connection, "OrderReversals", "SubTotalAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
		await EnsureColumnAsync(connection, "OrderReversals", "ManualDiscountAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
		await EnsureColumnAsync(connection, "OrderReversals", "CouponDiscountAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
		await EnsureColumnAsync(connection, "OrderReversals", "LoyaltyDiscountAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
		await EnsureColumnAsync(connection, "OrderReversals", "ServiceChargeAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
		await EnsureColumnAsync(connection, "OrderReversals", "VatAmount", "TEXT NOT NULL DEFAULT '0'", cancellationToken);
		await EnsureColumnAsync(connection, "OrderReversals", "IsFullOrderReversal", "INTEGER NOT NULL DEFAULT 1", cancellationToken);
		await EnsureColumnAsync(connection, "OrderReversals", "RequestFingerprint", "TEXT NOT NULL DEFAULT ''", cancellationToken);
		await EnsureColumnAsync(connection, "OrderReversals", "LoyaltyPointsEarnedReversed", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
		await EnsureColumnAsync(connection, "OrderReversals", "LoyaltyPointsRedeemedRestored", "INTEGER NOT NULL DEFAULT 0", cancellationToken);
		await EnsureColumnAsync(connection, "OrderReversals", "CouponUsageReleased", "INTEGER NOT NULL DEFAULT 0", cancellationToken);

		await ExecuteAsync(connection, """
			UPDATE "OrderReversals"
			SET "SubTotalAmount" = "Amount",
				"IsFullOrderReversal" = 1,
				"LoyaltyPointsEarnedReversed" = COALESCE(
					(SELECT "LoyaltyPointsEarned" FROM "Orders" WHERE "Orders"."Id" = "OrderReversals"."OrderId"), 0),
				"LoyaltyPointsRedeemedRestored" = COALESCE(
					(SELECT "LoyaltyPointsRedeemed" FROM "Orders" WHERE "Orders"."Id" = "OrderReversals"."OrderId"), 0),
				"CouponUsageReleased" = CASE WHEN EXISTS (
					SELECT 1 FROM "CouponRedemptions"
					WHERE "CouponRedemptions"."OrderId" = "OrderReversals"."OrderId"
				) THEN 1 ELSE 0 END
			WHERE "RequestFingerprint" = '';
			UPDATE "CouponRedemptions"
			SET "ReversedAt" = (
				SELECT MIN("ProcessedAt") FROM "OrderReversals"
				WHERE "OrderReversals"."OrderId" = "CouponRedemptions"."OrderId"
			)
			WHERE "ReversedAt" IS NULL AND EXISTS (
				SELECT 1 FROM "OrderReversals"
				WHERE "OrderReversals"."OrderId" = "CouponRedemptions"."OrderId"
			);
			CREATE TABLE IF NOT EXISTS "OrderReversalItems" (
				"Id" INTEGER NOT NULL CONSTRAINT "PK_OrderReversalItems" PRIMARY KEY AUTOINCREMENT,
				"TenantId" INTEGER NOT NULL, "OrderReversalId" INTEGER NOT NULL,
				"OrderItemId" INTEGER NOT NULL, "ProductId" INTEGER NOT NULL,
				"ProductName" TEXT NOT NULL, "Quantity" INTEGER NOT NULL,
				"SubTotalAmount" TEXT NOT NULL, "ManualDiscountAmount" TEXT NOT NULL,
				"CouponDiscountAmount" TEXT NOT NULL, "LoyaltyDiscountAmount" TEXT NOT NULL,
				"ServiceChargeAmount" TEXT NOT NULL, "VatAmount" TEXT NOT NULL,
				"TotalAmount" TEXT NOT NULL,
				CONSTRAINT "FK_OrderReversalItems_OrderReversals_OrderReversalId" FOREIGN KEY ("OrderReversalId") REFERENCES "OrderReversals" ("Id") ON DELETE CASCADE,
				CONSTRAINT "FK_OrderReversalItems_OrderItems_OrderItemId" FOREIGN KEY ("OrderItemId") REFERENCES "OrderItems" ("Id") ON DELETE CASCADE,
				CONSTRAINT "FK_OrderReversalItems_Products_ProductId" FOREIGN KEY ("ProductId") REFERENCES "Products" ("Id") ON DELETE CASCADE
			);
			CREATE TABLE IF NOT EXISTS "FinancialEvents" (
				"Id" INTEGER NOT NULL CONSTRAINT "PK_FinancialEvents" PRIMARY KEY AUTOINCREMENT,
				"TenantId" INTEGER NOT NULL, "Type" INTEGER NOT NULL, "OrderId" INTEGER NOT NULL,
				"OrderReversalId" INTEGER NULL, "SourceKey" TEXT NOT NULL, "Amount" TEXT NOT NULL,
				"PaymentMethod" INTEGER NOT NULL, "CashShiftId" INTEGER NULL, "OccurredAt" TEXT NOT NULL,
				"Description" TEXT NOT NULL,
				CONSTRAINT "FK_FinancialEvents_Orders_OrderId" FOREIGN KEY ("OrderId") REFERENCES "Orders" ("Id") ON DELETE CASCADE,
				CONSTRAINT "FK_FinancialEvents_OrderReversals_OrderReversalId" FOREIGN KEY ("OrderReversalId") REFERENCES "OrderReversals" ("Id"),
				CONSTRAINT "FK_FinancialEvents_CashShifts_CashShiftId" FOREIGN KEY ("CashShiftId") REFERENCES "CashShifts" ("Id")
			);
			CREATE UNIQUE INDEX IF NOT EXISTS "IX_Products_TenantId_Barcode" ON "Products" ("TenantId", "Barcode");
			CREATE UNIQUE INDEX IF NOT EXISTS "IX_Orders_TenantId_IdempotencyKey" ON "Orders" ("TenantId", "IdempotencyKey");
			CREATE UNIQUE INDEX IF NOT EXISTS "IX_CashShifts_TenantId_OpenSlot" ON "CashShifts" ("TenantId", "OpenSlot");
			CREATE UNIQUE INDEX IF NOT EXISTS "IX_CashShifts_TenantId_OpenIdempotencyKey" ON "CashShifts" ("TenantId", "OpenIdempotencyKey");
			CREATE UNIQUE INDEX IF NOT EXISTS "IX_CashShifts_TenantId_CloseIdempotencyKey" ON "CashShifts" ("TenantId", "CloseIdempotencyKey");
			CREATE UNIQUE INDEX IF NOT EXISTS "IX_Customers_TenantId_PhoneNormalized" ON "Customers" ("TenantId", "PhoneNormalized");
			CREATE UNIQUE INDEX IF NOT EXISTS "IX_PromotionCoupons_TenantId_Code" ON "PromotionCoupons" ("TenantId", "Code");
			CREATE UNIQUE INDEX IF NOT EXISTS "IX_CouponRedemptions_TenantId_PromotionCouponId_OrderId" ON "CouponRedemptions" ("TenantId", "PromotionCouponId", "OrderId");
			DROP INDEX IF EXISTS "IX_OrderReversals_TenantId_OrderId";
			CREATE INDEX IF NOT EXISTS "IX_OrderReversals_TenantId_OrderId" ON "OrderReversals" ("TenantId", "OrderId");
			CREATE UNIQUE INDEX IF NOT EXISTS "IX_OrderReversals_TenantId_IdempotencyKey" ON "OrderReversals" ("TenantId", "IdempotencyKey");
			CREATE INDEX IF NOT EXISTS "IX_OrderItems_TenantId_OrderId" ON "OrderItems" ("TenantId", "OrderId");
			CREATE INDEX IF NOT EXISTS "IX_OrderReversalItems_TenantId_OrderReversalId_OrderItemId" ON "OrderReversalItems" ("TenantId", "OrderReversalId", "OrderItemId");
			CREATE UNIQUE INDEX IF NOT EXISTS "IX_FinancialEvents_TenantId_SourceKey" ON "FinancialEvents" ("TenantId", "SourceKey");
			CREATE INDEX IF NOT EXISTS "IX_FinancialEvents_TenantId_OccurredAt" ON "FinancialEvents" ("TenantId", "OccurredAt");
			INSERT INTO "FinancialEvents"
				("TenantId", "Type", "OrderId", "OrderReversalId", "SourceKey", "Amount",
				 "PaymentMethod", "CashShiftId", "OccurredAt", "Description")
			SELECT o."TenantId", 0, o."Id", NULL, 'LEGACY-SALE:' || o."Id", o."TotalAmount",
				o."PaymentMethod", o."CashShiftId", o."CreatedAt", 'Legacy sale ' || o."OrderNo"
			FROM "Orders" o
			WHERE NOT EXISTS (
				SELECT 1 FROM "FinancialEvents" e
				WHERE e."TenantId" = o."TenantId" AND e."OrderId" = o."Id" AND e."Type" = 0
			);
			INSERT INTO "FinancialEvents"
				("TenantId", "Type", "OrderId", "OrderReversalId", "SourceKey", "Amount",
				 "PaymentMethod", "CashShiftId", "OccurredAt", "Description")
			SELECT r."TenantId", CASE WHEN r."Type" = 0 THEN 2 ELSE 1 END, r."OrderId", r."Id",
				'LEGACY-REVERSAL:' || r."Id", -r."Amount", o."PaymentMethod",
				r."CashShiftId", r."ProcessedAt", 'Legacy reversal ' || o."OrderNo"
			FROM "OrderReversals" r
			JOIN "Orders" o ON o."Id" = r."OrderId"
			WHERE NOT EXISTS (
				SELECT 1 FROM "FinancialEvents" e
				WHERE e."TenantId" = r."TenantId" AND e."OrderReversalId" = r."Id"
			);
			""", cancellationToken);
	}

	private static async Task EnsureColumnAsync(
		DbConnection connection,
		string table,
		string column,
		string definition,
		CancellationToken cancellationToken)
	{
		await using var query = connection.CreateCommand();
		query.CommandText = $"PRAGMA table_info(\"{table}\");";
		await using var reader = await query.ExecuteReaderAsync(cancellationToken);
		while (await reader.ReadAsync(cancellationToken))
		{
			if (string.Equals(reader.GetString(1), column, StringComparison.OrdinalIgnoreCase))
				return;
		}

		await ExecuteAsync(connection, $"ALTER TABLE \"{table}\" ADD COLUMN \"{column}\" {definition};", cancellationToken);
	}

	private static async Task ExecuteAsync(DbConnection connection, string sql, CancellationToken cancellationToken)
	{
		await using var command = connection.CreateCommand();
		command.CommandText = sql;
		await command.ExecuteNonQueryAsync(cancellationToken);
	}
}
