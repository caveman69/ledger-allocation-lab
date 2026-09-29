# Ledger Allocation Lab

A small .NET 10 lab for splitting payments exactly across the parties that own them, posting them
append-only, and reconciling two reports built from the same data. The worked example is property
tax collection, where one payment is owed to several taxing districts.

In plain terms: when a county collects a property tax payment, it passes that money on to the
school district, the city, and other taxing districts. The split has to be exact to the cent, a
recorded payment can never be quietly changed, and when two reports disagree, someone has to
explain why. This lab builds and tests each of those pieces.

Synthetic data only. See [docs/SPEC.md](docs/SPEC.md) for the design.

## What's in it

- **Allocator** (`src/LedgerAllocationLab.Core`): splits a payment across districts so the parts
  always add back to the total, to the cent. It rounds every share down, then hands the leftover
  cents to the shares that lost the most in rounding (the largest-remainder method).
- **Business-date clock** (`src/LedgerAllocationLab.Core`): decides which business day a payment
  counts toward. It turns the UTC timestamp into a date in `America/Phoenix`, which has no
  daylight saving time.
- **Posting** (`src/LedgerAllocationLab.Data`): records a payment and its split together, or
  nothing at all (one transaction). A repeat submission is recorded once (idempotent on a
  caller-supplied key). Recorded payments are never changed or deleted (append-only), and database
  triggers refuse any attempt to.
- **Reports and reconciliation** (`src/LedgerAllocationLab.Data`): two reports of what each
  district collected each day. One is correct, and one makes three common mistakes on purpose. A
  reconciliation compares them and labels every difference as rounding, timing, or unexplained.

## Status

| Milestone | State |
|---|---|
| M1 Allocator and tests | Done |
| M2 Schema, posting, idempotency, append-only | Done |
| M3 Reports and reconciliation | In progress. The report tests fail until it lands. |
| M4 Business-date clock and boundary tests | Done |

## Stack

.NET 10, C#, Dapper, SQL Server 2022 (Docker), xUnit.

## Prerequisites

- .NET 10 SDK
- Docker with Compose v2

## Run it

All commands run from the repository root.

**1. Start SQL Server.** Copy the example environment file and set a strong password in `.env`
(8+ characters with uppercase, lowercase, digit, and symbol). `.env` is git-ignored.

```bash
cp .env.example .env
docker compose up -d --wait
```

`--wait` returns once the container's health check passes.

**2. Set the connection string.** The migrator and the Data.Tests both read `LEDGERLAB_SQL`. Use
the password from your `.env`:

```bash
# bash
export LEDGERLAB_SQL="Server=127.0.0.1,14333;Database=LedgerLab;User Id=sa;Password=<your password>;TrustServerCertificate=True"
```

```powershell
# PowerShell
$env:LEDGERLAB_SQL = "Server=127.0.0.1,14333;Database=LedgerLab;User Id=sa;Password=<your password>;TrustServerCertificate=True"
```

**3. Create the database and schema.**

```bash
dotnet run --project src/LedgerAllocationLab.Migrator
```

The migrator uses [DbUp](https://dbup.readthedocs.io/). It creates the `LedgerLab` database if it
doesn't exist, then applies the scripts in `src/LedgerAllocationLab.Database/Scripts`. It's safe to
run again: DbUp records which migration scripts it has applied and only runs new ones. It exits
with a non-zero code if a script fails.

**4. Build and test.**

```bash
dotnet build
dotnet test
```

Core.Tests need nothing else. Data.Tests are skipped, with a message, unless `LEDGERLAB_SQL` is
set. They don't touch the `LedgerLab` database. Each test run creates its own
`LedgerLab_Test_...` database on the same server, builds it with the same migrator, and drops it
when the run ends. To keep it for inspection in a SQL client, set `LEDGERLAB_KEEP_TEST_DB=1`
before running the tests. Leftover test databases more than an hour old are removed at the start
of the next run.

To avoid setting `LEDGERLAB_SQL` in every shell for the tests, create `test.local.runsettings` in
the repository root. `Directory.Build.props` picks it up automatically, and it's git-ignored. It
applies to `dotnet test` only, not to the migrator:

```xml
<RunSettings>
  <RunConfiguration>
    <EnvironmentVariables>
      <LEDGERLAB_SQL>Server=127.0.0.1,14333;Database=LedgerLab;User Id=sa;Password=your-password;TrustServerCertificate=True</LEDGERLAB_SQL>
    </EnvironmentVariables>
  </RunConfiguration>
</RunSettings>
```

## Resetting the database

The payment tables are append-only, so rows can't be deleted. To get a clean `LedgerLab`
database, remove the container and its volume, then repeat steps 1 and 3:

```bash
docker compose down -v
```

## Connecting with a SQL client

Use SSMS or Azure Data Studio with server `127.0.0.1,14333`, SQL Server authentication as `sa`,
and Trust server certificate checked. Use `127.0.0.1` rather than `localhost`. The port is bound
to IPv4 loopback only, and on Windows `localhost` can resolve to IPv6 first and be refused.

## Layout

```
docs/SPEC.md                          the design
src/LedgerAllocationLab.Core          allocator and business-date clock, no database
src/LedgerAllocationLab.Data          Dapper and SQL Server: posting, reports, reconciliation
src/LedgerAllocationLab.Database      schema scripts and the DbUp migrator library
src/LedgerAllocationLab.Migrator      console app that runs the migrations
tests/LedgerAllocationLab.Core.Tests  fast unit tests
tests/LedgerAllocationLab.Data.Tests  integration tests against a throwaway database per run
```