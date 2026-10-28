-- API keys, token limits and usage tracking.
USE HrmsAi;
GO
IF OBJECT_ID('dbo.ApiKeys') IS NULL
CREATE TABLE dbo.ApiKeys (
    Id             UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ApiKeys PRIMARY KEY,
    ClientName     NVARCHAR(200) NOT NULL,
    KeyPrefix      NVARCHAR(16)  NOT NULL,
    KeyHash        BINARY(32)    NOT NULL,                              -- SHA-256 of the key; the key itself is never stored
    IsActive       BIT           NOT NULL CONSTRAINT DF_ApiKeys_Active  DEFAULT 1,
    AllowedModules NVARCHAR(200) NOT NULL CONSTRAINT DF_ApiKeys_Mods    DEFAULT '*',
    TokenLimit     BIGINT        NULL,                                  -- NULL = unlimited
    LimitPeriod    NVARCHAR(20)  NOT NULL CONSTRAINT DF_ApiKeys_Period  DEFAULT 'Monthly',   -- Daily | Monthly | Total
    ExpiresAt      DATETIME2     NULL,
    Notes          NVARCHAR(500) NULL,
    CreatedAt      DATETIME2     NOT NULL CONSTRAINT DF_ApiKeys_Created DEFAULT SYSUTCDATETIME(),
    LastUsedAt     DATETIME2     NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_ApiKeys_KeyHash' AND object_id = OBJECT_ID('dbo.ApiKeys'))
    CREATE UNIQUE INDEX UX_ApiKeys_KeyHash ON dbo.ApiKeys (KeyHash);
GO
IF OBJECT_ID('dbo.ApiKeyUsage') IS NULL
CREATE TABLE dbo.ApiKeyUsage (
    Id               BIGINT IDENTITY(1,1) NOT NULL CONSTRAINT PK_ApiKeyUsage PRIMARY KEY,
    ApiKeyId         UNIQUEIDENTIFIER NOT NULL CONSTRAINT FK_ApiKeyUsage_Key REFERENCES dbo.ApiKeys (Id),
    At               DATETIME2 NOT NULL CONSTRAINT DF_ApiKeyUsage_At DEFAULT SYSUTCDATETIME(),
    Module           NVARCHAR(50)  NULL,
    Operation        NVARCHAR(100) NULL,
    PromptTokens     INT NOT NULL,
    CompletionTokens INT NOT NULL,
    TotalTokens      INT NOT NULL,
    JobId            UNIQUEIDENTIFIER NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ApiKeyUsage_Key_At' AND object_id = OBJECT_ID('dbo.ApiKeyUsage'))
    CREATE INDEX IX_ApiKeyUsage_Key_At ON dbo.ApiKeyUsage (ApiKeyId, At) INCLUDE (TotalTokens);
GO