# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```bash
# Build the solution
dotnet build OnwardsSwift.sln

# Run the API (from solution root or OnwardsSwift.API/)
dotnet run --project OnwardsSwift.API

# Publish
dotnet publish OnwardsSwift.API -c Release
```

There are no automated tests in this solution. All testing is manual.

The app is hosted under the path base `/Onwards` (configured in `appsettings.json` as `PathBase`). When running locally, it defaults to http://localhost:5000/Onwards.

## Database

- **SQL Server** at `45.150.188.26,4420`, database `OnwardsSwiftDB`
- **ORM**: Dapper (raw SQL only — no Entity Framework)
- Connection factory: `OnwardsSwift.Infrastructure/Data/DapperContext.cs` — opens a new `SqlConnection` per operation
- Schema is managed via SQL scripts (not code-first migrations):
  - `OnwardsSwift.Infrastructure/Data/InitialMigration.sql` — full initial schema
  - `AddFeatureTables.sql`, `AddMissingTables.sql`, `AddAlterTables.sql` — incremental updates at solution root

When adding new tables or columns, add a corresponding SQL script at the solution root following the existing naming pattern.

## Architecture

Three-project clean architecture:

- **OnwardsSwift.API** — ASP.NET Core MVC (controllers, views, wwwroot). Entry point; handles HTTP, auth cookie, file uploads.
- **OnwardsSwift.Core** — DTOs, interfaces, and enums. No dependencies on other projects.
- **OnwardsSwift.Infrastructure** — Dapper-based repositories and services implementing Core interfaces. References Core only.

All services are registered in `OnwardsSwift.API/Program.cs`. DapperContext is a Singleton; all services are Scoped.

## Authentication & Authorization

- Cookie auth: scheme `OnwardsSwift.Auth`, 8-hour sliding expiration, login path `/Account/Login`
- Passwords hashed with SHA-256 via `AppController.Hash()`
- All controllers inherit `AppController` (in Controllers/), which provides `[Authorize]`, `Success(msg)`/`Error(msg)` TempData helpers, and current-user accessors
- Roles (defined in `OnwardsSwift.Core/Enums/UserRole.cs`): Admin (1), RelationshipManager (2), CreditOfficer (3), Client (4), Auditor (5)

## Key Enums

These drive status and type throughout the codebase:

- `FacilityStatus`: Draft → Pending → UnderReview → Approved → Disbursed → Rejected / Expired / Settled
- `FacilityType`: BidBond, PerformanceBond, AdvancePayment
- `ClientType`: Individual, SME, Corporate, Government
- `DocumentType`: used for file upload categorization

## File Uploads

- KYC documents saved to `wwwroot/uploads/kyc/`
- Bond/facility documents saved to `wwwroot/uploads/bonds/`
- Azure Blob Storage is configured in `appsettings.json` but not yet wired up

## Service Integrations (configured but not fully implemented)

- **SendGrid** — email notifications (`appsettings.json → SendGrid`)
- **Africa's Talking** — SMS (`appsettings.json → AfricasTalking`, sender ID `onwards_swift`)

## Views & Frontend

- Razor views with Bootstrap 5.3 and Bootstrap Icons
- Shared layout: `Views/Shared/_Layout.cshtml`
- Dynamic sidebar: `Views/Shared/Components/Sidebar/` (populated via `IMenuService`)
- Navy theme (`#002D72`) defined in site CSS
