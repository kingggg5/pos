# Product Specification — Smart POS

<!-- impeccable:product-schema 1 -->

## Platform

Web application for desktop POS workstations, tablets, and touch terminals.

## Users

- Owners manage store settings, users, permissions, products, promotions, cash shifts, refunds, and reports.
- Managers operate sales and inventory and may void or refund orders.
- Cashiers operate the checkout counter but cannot grant manual discounts or reverse orders.

## Product purpose

Smart POS gives each store an independent point-of-sale workspace for checkout, inventory, cash control, customer loyalty, promotions, refunds, and daily reporting.

## Tenant isolation

- Every operational entity carries a `TenantId`.
- The authenticated tenant comes from a signed JWT claim.
- ASP.NET Core tenant middleware rejects conflicting client tenant headers.
- EF Core global query filters are fail-closed when no trusted tenant is available.
- PostgreSQL Row-Level Security is not currently configured; it is a possible additional defense for a future release.

## Current technology

- Angular 22 and TypeScript 6
- ASP.NET Core on .NET 10
- Entity Framework Core 10
- PostgreSQL 16 in Docker/production
- SQLite for local development and fast automated tests
- SignalR, NGINX, and Docker Compose

## Core capabilities

- Multi-tenant store onboarding and role-based access
- Server-authoritative checkout totals and idempotent payment submission
- Cash shift opening, expected cash calculation, closing count, and variance
- Full and item-level partial refunds with stock restoration
- Customer lookup, points earning, redemption, and refund reversal
- Percentage/fixed coupons with eligibility and usage controls
- Business-timezone-aware Z-Reports backed by immutable financial events
- Product and stock management, receipt preview, CSV export, and audit logs

## Product constraints

- Browser offline checkout is not implemented yet; the UI requires API connectivity.
- PostgreSQL RLS and production payment-gateway settlement are not implemented.
- Demo credentials and sample data are for local development only.

## Brand commitments

- Product name: Smart POS
- Voice: direct, reliable, fast, and precise
- Cashier screens prioritize high contrast, scanability, touch targets of at least 44px, and clear error recovery
