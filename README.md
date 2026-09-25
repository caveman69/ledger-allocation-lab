# Ledger Allocation Lab

A small .NET 10 lab for splitting payments exactly across the parties that own them, posting them
append-only, and reconciling two reports built from the same data. The worked example is property
tax collection, where one payment is owed to several taxing districts.

Synthetic data only. See [docs/SPEC.md](docs/SPEC.md) for the design and
[docs/WALKTHROUGH.md](docs/WALKTHROUGH.md) for how each piece works.

## Stack

.NET 10, C#, Dapper, SQL Server 2022 (Docker), xUnit.

## Run it

From this folder:

```bash
cp .env.example .env          # then set MSSQL_SA_PASSWORD
docker compose up -d
docker exec ledgerlab-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "<password>" -C -i /db/000_create_database.sql

dotnet build
dotnet test                    # Data.Tests need LEDGERLAB_SQL set; see .env.example
```

Connect from SSMS or Azure Data Studio with server `127.0.0.1,14333`, SQL Server
authentication as `sa`, and Trust server certificate checked. Use `127.0.0.1` rather than
`localhost`: the port is bound to IPv4 loopback only, and on Windows `localhost` can resolve to
IPv6 first and be refused.
