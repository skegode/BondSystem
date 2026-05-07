-- Convert Bonds.ProcuringEntity from INT (FK to Obligees) to NVARCHAR(255)
-- so that procuring entity names can be entered as free text.

-- Step 1: Drop the foreign key constraint if it exists
DECLARE @fk NVARCHAR(200);
SELECT @fk = name
FROM sys.foreign_keys
WHERE parent_object_id = OBJECT_ID('Bonds') AND name LIKE '%ProcuringEntity%';
IF @fk IS NOT NULL
    EXEC('ALTER TABLE Bonds DROP CONSTRAINT ' + @fk);
GO

-- Step 2: Populate a temp column with the resolved name before changing type
ALTER TABLE Bonds ADD ProcuringEntityName_temp NVARCHAR(255) NULL;
GO

UPDATE b
SET    b.ProcuringEntityName_temp = o.Name
FROM   Bonds b
LEFT JOIN Obligees o ON o.Id = TRY_CAST(b.ProcuringEntity AS INT);
GO

-- Step 3: Drop old column and rename temp column
ALTER TABLE Bonds DROP COLUMN ProcuringEntity;
GO

EXEC sp_rename 'Bonds.ProcuringEntityName_temp', 'ProcuringEntity', 'COLUMN';
GO
