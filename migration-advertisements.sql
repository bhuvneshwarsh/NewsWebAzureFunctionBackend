-- ============================================================
-- CloudNews Lite — Advertisement Feature Migration
-- Run in Azure Data Studio / VS Code MSSQL extension
-- ============================================================

IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Advertisements' AND xtype='U')
BEGIN
    CREATE TABLE [dbo].[Advertisements] (
        [Id]           INT            IDENTITY(1,1) NOT NULL,
        [Title]        NVARCHAR(200)  NOT NULL,        -- Internal name (not shown on site)
        [AdImageUrl]   NVARCHAR(1000) NOT NULL,        -- Uploaded to Azure Blob
        [ClickUrl]     NVARCHAR(1000) NULL,            -- Where clicking the ad goes
        [AdvertiserName] NVARCHAR(200) NULL,           -- Who placed this ad
        [Placement]    NVARCHAR(50)   NOT NULL,        -- 'banner_top' | 'sidebar' | 'inline' | 'banner_bottom'
        [Width]        INT            NULL,            -- Optional: original width in px
        [Height]       INT            NULL,            -- Optional: original height in px
        [StartDate]    DATE           NULL,            -- null = show immediately
        [EndDate]      DATE           NULL,            -- null = show indefinitely
        [IsActive]     BIT            NOT NULL DEFAULT 1,
        [DisplayOrder] INT            NOT NULL DEFAULT 0,
        [Impressions]  INT            NOT NULL DEFAULT 0,  -- how many times shown
        [Clicks]       INT            NOT NULL DEFAULT 0,  -- how many times clicked
        [Notes]        NVARCHAR(500)  NULL,            -- Internal notes
        [CreatedAt]    DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt]    DATETIME2      NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT [PK_Advertisements] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    CREATE NONCLUSTERED INDEX [IX_Ads_Placement_Active]
        ON [dbo].[Advertisements] ([Placement], [IsActive], [DisplayOrder]);

    PRINT 'Advertisements table created successfully.';
END
ELSE
    PRINT 'Advertisements table already exists. Skipping.';
GO

-- Verify
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Advertisements'
ORDER BY ORDINAL_POSITION;
GO
