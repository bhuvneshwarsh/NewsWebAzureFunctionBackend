-- ============================================================
-- CloudNews Lite — Employee Login Feature Migration
-- Run in Azure Data Studio / VS Code MSSQL extension
-- ============================================================

-- Step 1: Add LinkedUserId column to Employees table
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Employees' AND COLUMN_NAME = 'LinkedUserId'
)
BEGIN
    ALTER TABLE [dbo].[Employees]
    ADD [LinkedUserId] INT NULL;

    PRINT 'LinkedUserId column added to Employees.';
END
ELSE
    PRINT 'LinkedUserId already exists. Skipping.';
GO

-- Step 2: Add HasLoginAccess flag
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Employees' AND COLUMN_NAME = 'HasLoginAccess'
)
BEGIN
    ALTER TABLE [dbo].[Employees]
    ADD [HasLoginAccess] BIT NOT NULL DEFAULT 0;

    PRINT 'HasLoginAccess column added.';
END
GO

-- Step 3: Add MustChangePassword flag to Users
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'MustChangePassword'
)
BEGIN
    ALTER TABLE [dbo].[Users]
    ADD [MustChangePassword] BIT NOT NULL DEFAULT 0;

    PRINT 'MustChangePassword column added to Users.';
END
GO

-- Step 4: Add LastLoginAt to Users
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Users' AND COLUMN_NAME = 'LastLoginAt'
)
BEGIN
    ALTER TABLE [dbo].[Users]
    ADD [LastLoginAt] DATETIME2 NULL;

    PRINT 'LastLoginAt column added to Users.';
END
GO

-- Verify
SELECT 'Employees columns:' AS Info;
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Employees'
  AND COLUMN_NAME IN ('LinkedUserId', 'HasLoginAccess')
ORDER BY COLUMN_NAME;

SELECT 'Users columns:' AS Info;
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Users'
  AND COLUMN_NAME IN ('MustChangePassword', 'LastLoginAt')
ORDER BY COLUMN_NAME;
GO
