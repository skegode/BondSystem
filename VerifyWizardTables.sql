-- VerifyWizardTables.sql
-- Run this after running AddBondApplicationsTable.sql, AddChequeEncashmentTables.sql, AddOfficialUseRecordsTable.sql

SET NOCOUNT ON;

PRINT 'Database: ' + DB_NAME();

SELECT
    t.name AS TableName,
    CASE WHEN t.object_id IS NULL THEN 0 ELSE 1 END AS ExistsFlag
FROM (VALUES
    ('BondApplications'),
    ('ChequeEncashmentRequests'),
    ('ChequeEncashmentCheques'),
    ('ChequeEncashmentAttachments'),
    ('OfficialUseRecords')
) x(name)
LEFT JOIN sys.tables t ON t.name = x.name AND SCHEMA_NAME(t.schema_id) = 'dbo';

SELECT TABLE_NAME, COLUMN_NAME, DATA_TYPE, IS_NULLABLE
FROM INFORMATION_SCHEMA.COLUMNS
WHERE TABLE_SCHEMA = 'dbo'
  AND TABLE_NAME IN (
      'BondApplications',
      'ChequeEncashmentRequests',
      'ChequeEncashmentCheques',
      'ChequeEncashmentAttachments',
      'OfficialUseRecords'
  )
ORDER BY TABLE_NAME, ORDINAL_POSITION;

SELECT 'BondApplications' AS TableName, COUNT(*) AS RowCount FROM dbo.BondApplications
UNION ALL
SELECT 'ChequeEncashmentRequests', COUNT(*) FROM dbo.ChequeEncashmentRequests
UNION ALL
SELECT 'ChequeEncashmentCheques', COUNT(*) FROM dbo.ChequeEncashmentCheques
UNION ALL
SELECT 'ChequeEncashmentAttachments', COUNT(*) FROM dbo.ChequeEncashmentAttachments
UNION ALL
SELECT 'OfficialUseRecords', COUNT(*) FROM dbo.OfficialUseRecords;

SELECT TOP 10 Id, ApplicantName, Procuring, TenderRef, CreatedAt, CreatedBy
FROM dbo.BondApplications
ORDER BY Id DESC;

SELECT TOP 10 Id, ClientId, ApplicantName, Purpose, TermsAccepted, CreatedAt, CreatedBy
FROM dbo.ChequeEncashmentRequests
ORDER BY Id DESC;

SELECT TOP 10 Id, RequestId, CheckedBy, CreatedAt, CreatedBy
FROM dbo.OfficialUseRecords
ORDER BY Id DESC;
