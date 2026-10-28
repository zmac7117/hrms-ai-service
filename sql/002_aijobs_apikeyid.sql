-- Jobs remember which API key created them (so clients only see their own jobs).
USE HrmsAi;
GO
IF COL_LENGTH('dbo.AiJobs', 'ApiKeyId') IS NULL
    ALTER TABLE dbo.AiJobs ADD ApiKeyId UNIQUEIDENTIFIER NULL;
GO