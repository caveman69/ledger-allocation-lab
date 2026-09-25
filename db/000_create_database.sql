-- Creates the lab database. Run once after the container is healthy:
--   docker exec ledgerlab-sql /opt/mssql-tools18/bin/sqlcmd -S localhost -U sa -P "<password>" -C -i /db/000_create_database.sql
IF DB_ID(N'LedgerLab') IS NULL
BEGIN
    CREATE DATABASE LedgerLab;
END
GO
