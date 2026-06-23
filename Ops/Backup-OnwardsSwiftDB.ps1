# Backup-OnwardsSwiftDB.ps1
# Daily full backup of OnwardsSwiftDB with 14-day retention, for a SQL Server Express
# instance (no SQL Server Agent available). Run this directly on the database server
# (servicesuiteai\SQLEXPRESS01) via Windows Task Scheduler -- see Register-BackupTask.ps1
# in this same folder to register the scheduled task in one step.

$timestamp = Get-Date -Format "yyyyMMdd_HHmmss"
$backupDir = "C:\Program Files\Microsoft SQL Server\MSSQL17.SQLEXPRESS01\MSSQL\Backup"
$backupFile = "$backupDir\OnwardsSwiftDB_$timestamp.bak"

$sql = @"
BACKUP DATABASE OnwardsSwiftDB TO DISK = N'$backupFile';
DECLARE @cutoff DATETIME = DATEADD(DAY, -14, GETDATE());
EXEC master.dbo.xp_delete_file 0, N'$backupDir', N'bak', @cutoff;
"@

sqlcmd -S "localhost,4420" -d master -U tester -P "Ngong123@" -C -Q $sql
