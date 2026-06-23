-- Add ProcessingDate column to Bonds table
IF NOT EXISTS (
    SELECT 1 FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Bonds' AND COLUMN_NAME = 'ProcessingDate'
)
BEGIN
    ALTER TABLE Bonds ADD ProcessingDate DATE NULL;
    PRINT 'Added ProcessingDate column to Bonds';
END
GO
