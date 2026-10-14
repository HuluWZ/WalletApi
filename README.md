# WalletApi

ASP.NET Core 9 Web API for a digital wallet: JWT auth, accounts, deposits, idempotent transfers, and scheduled (recurring) payments.

## Architecture
Clean Architecture, inward-only dependencies (see [ADR 1](docs/adr/0001-clean-architecture.md)):

    src/WalletApi.Domain          entities, rules
    src/WalletApi.Application     use cases + ports
    src/WalletApi.Infrastructure  EF Core/Postgres, JWT, background worker
    src/WalletApi.Api             controllers, composition root
    tests/WalletApi.Application.Tests

Repo standards: central package management, `global.json`, `.editorconfig` + warnings-as-errors, ADRs, CODEOWNERS, PR template, Dependabot, CI (format, build, test, Postgres smoke test, Docker build).

## Run
    docker compose up --build        # API on :8080, worker ticks every 2s in compose
    dotnet test

## Endpoints
| Method | Path | Notes |
|---|---|---|
| POST | /api/auth/register, /api/auth/login | returns `{token}` |
| GET | /api/accounts | your accounts |
| POST | /api/accounts/{id}/deposit | `{amount}` |
| POST | /api/accounts/transfers | header `Idempotency-Key`; `{fromAccountId,toAccountId,amount}` |
| POST | /api/scheduled-transfers | `{fromAccountId,toAccountId,amount,interval: Daily/Weekly/Monthly,firstRunAt}` |
| GET / DELETE | /api/scheduled-transfers[/{id}] | list / cancel |

## Roadmap checklist
- [x] Auth (JWT), accounts, deposits
- [x] Idempotent atomic transfers with optimistic concurrency
- [x] Scheduled payments (daily/weekly/monthly) with background worker
- [x] CI: format, build, test, Postgres smoke test, Docker build
- [ ] **Account verification via Fayda (Ethiopia's national digital ID)** - next step: verification status on `User`, a `IIdentityVerifier` port in Application, a Fayda adapter in Infrastructure, and gating transfers above a limit on verified accounts. Requires Fayda/NIDP integration access.
- [ ] EF Core migrations (replace `EnsureCreated`)
- [ ] Refresh tokens, rate limiting, audit log
- [ ] Multi-instance-safe scheduler (`FOR UPDATE SKIP LOCKED`, see ADR 3)
- [ ] Integration tests with Testcontainers; OpenTelemetry metrics
