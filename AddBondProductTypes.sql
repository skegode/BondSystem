-- Adds additional bond-related products if they do not already exist.
-- Run this script against OnwardsSwiftDB.

IF NOT EXISTS (SELECT 1 FROM ProductTypes WHERE ProductName = 'Counter indemnity')
BEGIN
    INSERT INTO ProductTypes (ProductName, ProductType)
    VALUES ('Counter indemnity', 1);
    PRINT 'Inserted: Counter indemnity';
END
ELSE
BEGIN
    PRINT 'Exists: Counter indemnity';
END

IF NOT EXISTS (SELECT 1 FROM ProductTypes WHERE ProductName = 'WIBA')
BEGIN
    INSERT INTO ProductTypes (ProductName, ProductType)
    VALUES ('WIBA', 1);
    PRINT 'Inserted: WIBA';
END
ELSE
BEGIN
    PRINT 'Exists: WIBA';
END

IF NOT EXISTS (SELECT 1 FROM ProductTypes WHERE ProductName = 'CAR')
BEGIN
    INSERT INTO ProductTypes (ProductName, ProductType)
    VALUES ('CAR', 1);
    PRINT 'Inserted: CAR';
END
ELSE
BEGIN
    PRINT 'Exists: CAR';
END
