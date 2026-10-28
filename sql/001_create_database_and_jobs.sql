-- Run in SSMS. Creates the database and the job queue table.
IF DB_ID('HrmsAi') IS NULL CREATE DATABASE HrmsAi;
GO
USE HrmsAi;
GO
IF OBJECT_ID('dbo.AiJobs') IS NULL
BEGIN
    CREATE TABLE dbo.AiJobs (
        Id            UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
        JobType       NVARCHAR(30)  NOT NULL,
        Status        NVARCHAR(20)  NOT NULL,   -- Queued, Running, Done, Failed
        InputFileName NVARCHAR(260) NULL,
        InputText     NVARCHAR(MAX) NULL,
        ResultJson    NVARCHAR(MAX) NULL,
        Error         NVARCHAR(2000) NULL,
        CreatedAt     DATETIME2 NOT NULL CONSTRAINT DF_AiJobs_Created DEFAULT SYSUTCDATETIME(),
        StartedAt     DATETIME2 NULL,
        CompletedAt   DATETIME2 NULL,
        DurationMs    INT NULL
    );
    CREATE INDEX IX_AiJobs_Status_Created ON dbo.AiJobs (Status, CreatedAt);
END
GO