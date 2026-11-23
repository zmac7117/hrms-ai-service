USE HrmsAi;
GO
IF OBJECT_ID('dbo.ChatDocuments') IS NULL
CREATE TABLE dbo.ChatDocuments (
    Id        UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    ApiKeyId  UNIQUEIDENTIFIER NULL,
    Title     NVARCHAR(200)    NOT NULL,
    FileName  NVARCHAR(260)    NULL,
    CreatedAt DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
IF OBJECT_ID('dbo.ChatChunks') IS NULL
CREATE TABLE dbo.ChatChunks (
    Id         BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    DocumentId UNIQUEIDENTIFIER NOT NULL,
    Seq        INT              NOT NULL,
    Content    NVARCHAR(MAX)    NOT NULL
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChatChunks_Document' AND object_id = OBJECT_ID('dbo.ChatChunks'))
    CREATE INDEX IX_ChatChunks_Document ON dbo.ChatChunks (DocumentId, Seq);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChatDocuments_Key' AND object_id = OBJECT_ID('dbo.ChatDocuments'))
    CREATE INDEX IX_ChatDocuments_Key ON dbo.ChatDocuments (ApiKeyId);
GO