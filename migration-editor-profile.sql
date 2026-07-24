-- ============================================================
-- CloudNews Lite — Editor Profile Feature Migration
-- Run in Azure Data Studio / VS Code MSSQL extension
-- ============================================================

IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='EditorProfiles' AND xtype='U')
BEGIN
    CREATE TABLE [dbo].[EditorProfiles] (
        [Id]              INT            IDENTITY(1,1) NOT NULL,
        [FullName]        NVARCHAR(200)  NOT NULL,
        [Title]           NVARCHAR(200)  NOT NULL,       -- e.g. "मुख्य संपादक / Chief Editor"
        [ImageUrl]        NVARCHAR(1000) NULL,
        [ShortBio]        NVARCHAR(500)  NOT NULL,       -- One-line intro shown on card
        [FullBio]         NVARCHAR(MAX)  NOT NULL,       -- Full life journey (HTML or plain text)
        [Experience]      NVARCHAR(200)  NULL,           -- e.g. "25+ वर्षों का अनुभव"
        [Education]       NVARCHAR(500)  NULL,
        [Awards]          NVARCHAR(MAX)  NULL,           -- JSON array or plain text
        [Email]           NVARCHAR(300)  NULL,
        [Phone]           NVARCHAR(20)   NULL,
        [TwitterUrl]      NVARCHAR(500)  NULL,
        [FacebookUrl]     NVARCHAR(500)  NULL,
        [LinkedInUrl]     NVARCHAR(500)  NULL,
        [IsActive]        BIT            NOT NULL DEFAULT 1,
        [DisplayOrder]    INT            NOT NULL DEFAULT 0,
        [CreatedAt]       DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt]       DATETIME2      NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT [PK_EditorProfiles] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    CREATE NONCLUSTERED INDEX [IX_EditorProfiles_Active]
        ON [dbo].[EditorProfiles] ([IsActive], [DisplayOrder]);

    PRINT 'EditorProfiles table created successfully.';
END
ELSE
    PRINT 'EditorProfiles table already exists. Skipping.';
GO

-- Verify
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'EditorProfiles'
ORDER BY ORDINAL_POSITION;
GO
