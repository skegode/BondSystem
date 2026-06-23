-- Adds payment method support for bond payment summary updates.
IF NOT EXISTS (
    SELECT 1
    FROM INFORMATION_SCHEMA.COLUMNS
    WHERE TABLE_NAME = 'Bonds'
      AND COLUMN_NAME = 'PaymentMethod'
)
BEGIN
    ALTER TABLE Bonds ADD PaymentMethod NVARCHAR(20) NULL;
END;

-- Normalize existing values if this script is re-run after manual data updates.
IF COL_LENGTH('Bonds', 'PaymentMethod') IS NOT NULL
BEGIN
    EXEC sp_executesql N'
        UPDATE Bonds
        SET PaymentMethod = UPPER(LTRIM(RTRIM(PaymentMethod)))
        WHERE PaymentMethod IS NOT NULL;
    ';
END;
