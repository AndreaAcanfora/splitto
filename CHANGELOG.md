# Changelog

## 0.1.0 (unreleased)

First version.

- Groups without sign-up: a secret invite link (128 random bits) is the only key; groups can't be enumerated.
- Expenses split equally, by shares or by exact amounts, to the cent (largest-remainder rounding).
- Expenses in other currencies with an exchange rate to the group's currency.
- Balances computed with SQL aggregates; the fewest payments to settle, solved exactly (dynamic programming over
  subsets) up to 20 people with an open balance, greedy beyond.
- Payments to record settling up; CSV export with one column per member.
- PostgreSQL with EF Core migrations and check constraints; rate limit on group creation.
- Blazor WebAssembly client served by the API: expenses, balances, people; light and dark theme, mobile layout.
- Docker image and docker-compose with PostgreSQL; CI with integration tests on a real PostgreSQL and a Docker
  smoke test.
