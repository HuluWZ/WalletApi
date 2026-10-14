# 1. Clean Architecture layering

**Status:** accepted

## Context
The wallet will grow (verification, scheduling, more payment rails). Layers must stay independently testable and replaceable.

## Decision
Four projects with inward-only dependencies:

    Api -> Infrastructure -> Application -> Domain

- **Domain:** entities and rules, no dependencies.
- **Application:** use-case services plus ports (`IWalletDb`, `ITokenService`). No ASP.NET, no Npgsql.
- **Infrastructure:** EF Core/Postgres, JWT, background workers. Implements the ports.
- **Api:** thin controllers and composition root only.

## Consequences
Business logic is tested against SQLite without HTTP. Swapping the database or token provider touches Infrastructure only.
