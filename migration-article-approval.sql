-- ============================================================
-- CloudNews Lite — Article Approval Feature Migration
-- Run in Azure Data Studio / VS Code MSSQL extension
-- ============================================================

-- Step 1: Add ApprovalStatus column
-- Values: 'NotRequired' | 'Pending' | 'Approved' | 'Rejected'
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Articles' AND COLUMN_NAME = 'ApprovalStatus'
)
BEGIN
    ALTER TABLE [dbo].[Articles]
    ADD [ApprovalStatus] NVARCHAR(20) NOT NULL DEFAULT 'NotRequired';
    PRINT 'ApprovalStatus column added.';
END
ELSE PRINT 'ApprovalStatus already exists.';
GO

-- Step 2: Add ApprovalNote — reason shown to employee on rejection
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Articles' AND COLUMN_NAME = 'ApprovalNote'
)
BEGIN
    ALTER TABLE [dbo].[Articles]
    ADD [ApprovalNote] NVARCHAR(500) NULL;
    PRINT 'ApprovalNote column added.';
END
GO

-- Step 3: Add ApprovedById — which SuperAdmin approved/rejected
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Articles' AND COLUMN_NAME = 'ApprovedById'
)
BEGIN
    ALTER TABLE [dbo].[Articles]
    ADD [ApprovedById] INT NULL;
    PRINT 'ApprovedById column added.';
END
GO

-- Step 4: Add ApprovedAt — when it was approved/rejected
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Articles' AND COLUMN_NAME = 'ApprovedAt'
)
BEGIN
    ALTER TABLE [dbo].[Articles]
    ADD [ApprovedAt] DATETIME2 NULL;
    PRINT 'ApprovedAt column added.';
END
GO

-- Step 5: Update existing articles published by SuperAdmin/Admin
-- They are already live so mark as NotRequired (default)
-- Employee articles that are published need Approved status
UPDATE [dbo].[Articles]
SET [ApprovalStatus] = 'NotRequired'
WHERE [ApprovalStatus] = 'NotRequired';  -- already set by default
GO

PRINT 'Migration complete. Verify:';
SELECT COLUMN_NAME, DATA_TYPE, IS_NULLABLE, COLUMN_DEFAULT
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_NAME = 'Articles'
  AND COLUMN_NAME IN ('ApprovalStatus','ApprovalNote','ApprovedById','ApprovedAt')
ORDER BY COLUMN_NAME;
GO
