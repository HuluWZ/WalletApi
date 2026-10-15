# WalletApi

ASP.NET Core 9 Web API for a digital wallet: JWT auth, accounts, deposits, idempotent transfers, scheduled payments, a transparent credit score, lending, and regulator compliance monitoring with a dashboard.

## Architecture
Clean Architecture, inward-only dependencies (see [ADR 1](docs/adr/0001-clean-architecture.md)):

    src/WalletApi.Domain          entities, rules
    src/WalletApi.Application     use cases + ports
    src/WalletApi.Infrastructure  EF Core/Postgres, JWT, background worker
    src/WalletApi.Api             controllers, composition root
    tests/WalletApi.Application.Tests

Repo standards: central package management, `global.json`, `.editorconfig` + warnings-as-errors, ADRs, CODEOWNERS, PR template, Dependabot, CI (format, build, test, Postgres smoke test, Docker build).

## Diagrams

### System overview
```mermaid
flowchart LR
    C[Customer app / Swagger UI] -->|JWT| API
    R[Regulator dashboard] -->|JWT, Regulator role| API
    subgraph Service
        API[Api<br/>controllers] --> APP[Application<br/>use cases]
        APP --> DOM[Domain<br/>entities and rules]
        INF[Infrastructure<br/>EF Core, JWT, workers] -.implements ports.-> APP
        API --> INF
        W[Background worker<br/>every N seconds] --> APP
    end
    INF --> DB[(PostgreSQL)]
```

### Layer dependencies (inward only)
```mermaid
flowchart LR
    Api --> Infrastructure --> Application --> Domain
```

### Transfer: idempotent, atomic, flagged in the same commit
```mermaid
sequenceDiagram
    participant U as Client
    participant A as AccountsController
    participant T as TransferService
    participant K as ComplianceService
    participant D as PostgreSQL
    U->>A: POST /transfers (Idempotency-Key)
    A->>T: TransferAsync
    T->>D: key already used?
    alt replay
        D-->>T: existing transfer
        T-->>U: 200 same result, no second debit
    else new
        T->>K: EvaluateAsync (rules vs 24h history)
        K-->>T: flags added to the unit of work
        T->>D: SaveChanges: balances + transfer + flags (xmin concurrency check)
        D-->>U: 201 Created
    end
```

### Data model
```mermaid
erDiagram
    USER ||--o{ ACCOUNT : owns
    ACCOUNT ||--o{ TRANSFER : "sends / receives"
    TRANSFER ||--o{ COMPLIANCE_FLAG : "may raise"
    USER ||--o{ SCHEDULED_TRANSFER : creates
    USER ||--o{ LOAN : borrows
    LOAN ||--|{ LOAN_INSTALLMENT : "repaid by"
    USER ||--o{ CREDIT_SCORE_SNAPSHOT : "daily score"
    USER { guid Id string Email string Role }
    ACCOUNT { guid Id long Balance }
    TRANSFER { guid Id string IdempotencyKey long Amount string Type }
    COMPLIANCE_FLAG { guid Id string Rule string Status }
    LOAN { guid Id long Principal decimal AnnualRatePercent string Status }
```

### Credit score to loan offer
```mermaid
flowchart LR
    TX[Transactions<br/>+ repayments] --> F[6 weighted factors<br/>repayment 30, income 20,<br/>savings 15, cushion 15,<br/>age 10, activity 10]
    F --> S[Score 300-850<br/>+ reason per factor]
    S --> B{Score >= 580<br/>and account >= 30 days?}
    B -- no --> N[No offer, reason shown]
    B -- yes --> O[Limit and APR by band<br/>580 / 670 / 740 / 800]
    O --> L[Apply: amortized installments]
    L --> AP[Worker autopay<br/>30 days overdue = default]
    AP -->|repayment history| TX
```

### Compliance monitoring
```mermaid
flowchart TD
    T[Transaction] --> R1{Amount over single threshold?}
    T --> R2{24h outgoing total over daily threshold?}
    T --> R3{3+ transfers just under threshold in 24h?}
    T --> R4{80%+ of money received sent on within 1h?}
    R1 & R2 & R3 & R4 -->|yes| F[(Open flag, saved with the transaction)]
    F --> REG[Regulator: Dismiss or Escalate + note]
    F --> G[Dashboard: volume and flags per day,<br/>transfer network graph]
```

## Run
    Swagger UI: http://localhost:8080/swagger       Dashboard: http://localhost:8080/dashboard.html
    Seeded regulator (compose only): regulator@wallet.local / Regulator#12345
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
| GET | /api/credit/score, /api/credit/history | 300-850 score with per-factor reasons; daily trend |
| GET / POST | /api/loans/offer, /api/loans | offer from score; apply (30-day-old account, one active loan) |
| POST | /api/loans/{id}/repay | pay next installment now (autopay also collects due ones) |
| GET | /api/compliance/flags, /stats, /network | **Regulator role only**; flags, per-day chart data, transfer graph |
| POST | /api/compliance/flags/{id}/review | Regulator: Dismissed or Escalated + note |

## Compliance rules (thresholds in `Compliance` config, minor units)
Large transaction, 24h outgoing total, structuring (repeated transfers just under the threshold), rapid pass-through (money in then out within 1h). Flags commit atomically with the transaction; loan movements are exempt.

## Credit score
Weighted scorecard 300-850: repayment history 30%, income regularity 20%, savings rate 15%, balance cushion 15%, account age 10%, activity 10%. Thin files get neutral values; every factor returns a plain-language reason ([ADR 4](docs/adr/0004-credit-scorecard.md)).

## Roadmap checklist
- [x] Auth (JWT), accounts, deposits
- [x] Idempotent atomic transfers with optimistic concurrency
- [x] Scheduled payments (daily/weekly/monthly) with background worker
- [x] CI: format, build, test, Postgres smoke test, Docker build
- [x] Swagger/OpenAPI docs with JWT auth
- [x] Credit score (weighted scorecard) with history chart
- [x] Lending: offer, apply, amortized schedule, autopay, default after 30 days overdue
- [x] Regulator role: configurable flag rules, review workflow, stats and network graph, dashboard
- [ ] **Account verification via Fayda (Ethiopia's national digital ID)** - next step: verification status on `User`, a `IIdentityVerifier` port in Application, a Fayda adapter in Infrastructure, and gating transfers above a limit on verified accounts. Requires Fayda/NIDP integration access.
- [ ] EF Core migrations (replace `EnsureCreated`)
- [ ] Refresh tokens, rate limiting, audit log
- [ ] Multi-instance-safe scheduler (`FOR UPDATE SKIP LOCKED`, see ADR 3)
- [ ] Integration tests with Testcontainers; OpenTelemetry metrics
