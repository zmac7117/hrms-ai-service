USE HrmsAi;
GO
IF OBJECT_ID('dbo.ChatConversations') IS NULL
CREATE TABLE dbo.ChatConversations (
    Id             UNIQUEIDENTIFIER NOT NULL PRIMARY KEY,
    ApiKeyId       UNIQUEIDENTIFIER NULL,
    ExternalUserId NVARCHAR(100)    NOT NULL,
    UserName       NVARCHAR(100)    NOT NULL,
    Language       NVARCHAR(10)     NOT NULL,
    CreatedAt      DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME(),
    LastMessageAt  DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
IF OBJECT_ID('dbo.ChatMessages') IS NULL
CREATE TABLE dbo.ChatMessages (
    Seq            BIGINT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Id             UNIQUEIDENTIFIER NOT NULL,
    ConversationId UNIQUEIDENTIFIER NOT NULL,
    Role           NVARCHAR(20)     NOT NULL,
    Content        NVARCHAR(MAX)    NOT NULL,
    Tokens         INT              NULL,
    CreatedAt      DATETIME2        NOT NULL DEFAULT SYSUTCDATETIME()
);
GO
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChatMessages_Conversation' AND object_id = OBJECT_ID('dbo.ChatMessages'))
    CREATE INDEX IX_ChatMessages_Conversation ON dbo.ChatMessages (ConversationId, Seq);
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_ChatConversations_User' AND object_id = OBJECT_ID('dbo.ChatConversations'))
    CREATE INDEX IX_ChatConversations_User ON dbo.ChatConversations (ApiKeyId, ExternalUserId, LastMessageAt);
GO