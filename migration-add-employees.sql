-- ============================================================
-- CloudNews Lite — Phase: Team Feature
-- Migration: Add Employees table
-- Run this in Azure Data Studio or VS Code MSSQL extension
-- against your CloudNewsDB database
-- ============================================================

IF NOT EXISTS (SELECT * FROM sysobjects WHERE name='Employees' AND xtype='U')
BEGIN
    CREATE TABLE [dbo].[Employees] (
        [Id]           INT            IDENTITY(1,1) NOT NULL,
        [EmployeeId]   NVARCHAR(30)   NOT NULL,        -- e.g. EMP-2024-00001
        [FullName]     NVARCHAR(200)  NOT NULL,
        [Designation]  NVARCHAR(150)  NOT NULL,
        [Email]        NVARCHAR(300)  NULL,
        [Mobile]       NVARCHAR(20)   NULL,
        [Address]      NVARCHAR(500)  NULL,
        [DateOfBirth]  DATE           NULL,
        [ImageUrl]     NVARCHAR(1000) NULL,
        [GovtIdNumber] NVARCHAR(100)  NULL,
        [GovtIdType]   NVARCHAR(50)   NULL,
        [ValidUpto]    DATE           NULL,
        [IsActive]     BIT            NOT NULL DEFAULT 1,
        [DisplayOrder] INT            NOT NULL DEFAULT 0,
        [CreatedAt]    DATETIME2      NOT NULL DEFAULT GETUTCDATE(),
        [UpdatedAt]    DATETIME2      NOT NULL DEFAULT GETUTCDATE(),

        CONSTRAINT [PK_Employees] PRIMARY KEY CLUSTERED ([Id] ASC)
    );

    -- Unique index on EmployeeId
    CREATE UNIQUE NONCLUSTERED INDEX [IX_Employees_EmployeeId]
        ON [dbo].[Employees] ([EmployeeId] ASC);

    -- Index for active employees query
    CREATE NONCLUSTERED INDEX [IX_Employees_IsActive_DisplayOrder]
        ON [dbo].[Employees] ([IsActive] ASC, [DisplayOrder] ASC);

    PRINT 'Employees table created successfully.';
END
ELSE
BEGIN
    PRINT 'Employees table already exists. Skipping.';
END
GO

-- ============================================================
-- Verify the table was created
-- ============================================================
SELECT
    COLUMN_NAME,
    DATA_TYPE,
    CHARACTER_MAXIMUM_LENGTH,
    IS_NULLABLE,
    COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Employees'
ORDER BY ORDINAL_POSITION;
GO
