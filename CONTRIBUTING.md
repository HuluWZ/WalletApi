# Contributing

1. Branch from `main`; open a PR (CODEOWNERS review required).
2. Before pushing: `dotnet format && dotnet build && dotnet test`.
3. Keep dependency direction `Api -> Infrastructure -> Application -> Domain`. New packages go in `Directory.Packages.props` (central versions).
4. Money paths need tests: idempotency, ownership, insufficient funds, concurrency.
5. Record significant decisions as an ADR in `docs/adr/`.
