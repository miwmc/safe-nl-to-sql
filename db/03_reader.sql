-- Read-only login for the API. Idempotent: reruns converge to the same permissions.
-- Requires the sqlcmd variable READER_PASSWORD (sqlcmd -v READER_PASSWORD=...).
-- The password must not contain a single quote.
SET QUOTED_IDENTIFIER ON;
GO

USE SafeNlToSql;
GO

IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'nl_search_reader')
    CREATE LOGIN nl_search_reader
        WITH PASSWORD = N'$(READER_PASSWORD)', DEFAULT_DATABASE = SafeNlToSql;
ELSE
    ALTER LOGIN nl_search_reader WITH PASSWORD = N'$(READER_PASSWORD)';
GO

IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'nl_search_reader')
    CREATE USER nl_search_reader FOR LOGIN nl_search_reader;
GO

-- Start from nothing on the table (also clears any wider grant from an earlier run),
-- then grant column-level SELECT on the six allowed columns. ClientName is left out on purpose.
REVOKE SELECT ON dbo.Certificates FROM nl_search_reader;
GO

GRANT SELECT ON dbo.Certificates
    (CertificateNumber, EquipmentName, EquipmentType, Metric, CalibrationDate, IsCompliant)
    TO nl_search_reader;
GO
