USE HrmsAi;
GO
IF COL_LENGTH('dbo.ApiKeyUsage', 'DurationMs')   IS NULL ALTER TABLE dbo.ApiKeyUsage ADD DurationMs INT NULL;
IF COL_LENGTH('dbo.ApiKeyUsage', 'Status')       IS NULL ALTER TABLE dbo.ApiKeyUsage ADD Status NVARCHAR(20) NULL;
IF COL_LENGTH('dbo.ApiKeyUsage', 'InputSummary') IS NULL ALTER TABLE dbo.ApiKeyUsage ADD InputSummary NVARCHAR(300) NULL;
IF COL_LENGTH('dbo.ApiKeyUsage', 'ResultText')   IS NULL ALTER TABLE dbo.ApiKeyUsage ADD ResultText NVARCHAR(MAX) NULL;
IF COL_LENGTH('dbo.ApiKeyUsage', 'Error')        IS NULL ALTER TABLE dbo.ApiKeyUsage ADD Error NVARCHAR(1000) NULL;
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_AiJobs_ApiKey_Created' AND object_id = OBJECT_ID('dbo.AiJobs'))
    CREATE INDEX IX_AiJobs_ApiKey_Created ON dbo.AiJobs (ApiKeyId, CreatedAt);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApiKeyUsage_JobId' AND object_id = OBJECT_ID('dbo.ApiKeyUsage'))
    CREATE INDEX IX_ApiKeyUsage_JobId ON dbo.ApiKeyUsage (JobId) WHERE JobId IS NOT NULL;
GO
