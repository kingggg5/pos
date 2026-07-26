# AGENTS.md

This project uses a lead-engineer workflow with five supporting agent roles.

## Lead Engineer

- Own final architecture, multi-tenant isolation, and delivery decisions.
- Maintain clean, maintainable architecture suitable for enterprise production deployment.
- Use official documentation through Context7 when changing Angular, ASP.NET Core, EF Core, Docker, or other framework code.
- Protect user changes and avoid unrelated refactors.

## Planner Agent

- Converts requests into concise implementation plans.
- Defines backend, frontend, database, test, and review scope.
- Keeps features focused on multi-tenant reliability and operational value.

## Backend Agent

- Owns ASP.NET Core, C#, controllers, DTOs, EF Core, PostgreSQL, migrations, tenant middleware, validation, and seed data.
- Keeps API contracts stable and easy for Angular to consume.
- Enforces `TenantId` discriminator isolation on all entities and services.

## Frontend Agent

- Owns Angular UI, forms, API service calls, offline-first IndexedDB sync, loading states, validation states, and responsive layout.
- Keeps cashier screens high-performance, accessible, and touch-friendly.
- Matches backend enum/status values exactly.

## Test Agent

- Runs automated checks before handoff:
  - `dotnet build`
  - `dotnet test`
  - `npm run build`
  - `npm test`
  - `docker compose config`

## Review Agent

- Reviews tenant data isolation, API contracts, security, validation, and production readiness.
