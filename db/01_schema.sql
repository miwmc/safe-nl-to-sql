-- Schema for safe-nl-to-sql. Idempotent: safe to run more than once.
-- Run with sqlcmd -I so QUOTED_IDENTIFIER is ON, the same as the application connection.
SET QUOTED_IDENTIFIER ON;
GO

IF DB_ID(N'SafeNlToSql') IS NULL
    CREATE DATABASE SafeNlToSql;
GO

USE SafeNlToSql;
GO

IF OBJECT_ID(N'dbo.Certificates', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Certificates
    (
        Id                int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Certificates PRIMARY KEY,
        CertificateNumber nvarchar(50)  NOT NULL,
        EquipmentName     nvarchar(100) NOT NULL,
        EquipmentType     nvarchar(50)  NOT NULL,
        Metric            nvarchar(50)  NOT NULL,
        CalibrationDate   date          NOT NULL,
        IsCompliant       bit           NOT NULL,
        ClientName        nvarchar(100) NOT NULL -- sensitive: never readable by nl_search_reader
    );
END
GO

-- Exists only to prove that the reader login cannot read any other table.
IF OBJECT_ID(N'dbo.Clients', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.Clients
    (
        Id           int IDENTITY(1,1) NOT NULL CONSTRAINT PK_Clients PRIMARY KEY,
        Name         nvarchar(100) NOT NULL,
        ContactEmail nvarchar(200) NOT NULL
    );
END
GO
