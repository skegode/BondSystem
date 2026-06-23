using System;
using System.IO;
using System.Globalization;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.API.Services;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Infrastructure.Data;
using Dapper; // <--- Critical: Needed for .QueryAsync

namespace OnwardsSwift.API.Controllers
{
    public class ReportsController : Controller
    {
        private readonly DapperContext _ctx;

        public ReportsController(DapperContext ctx)
        {
            _ctx = ctx;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> UserCommissions(DateTime? from, DateTime? to, string? userId, int? facilityType)
        {
            if (string.IsNullOrWhiteSpace(userId)) userId = null;
            var startDate = from?.Date ?? new DateTime(DateTime.Now.Year, 1, 1);
            var endDate = (to?.Date.AddDays(1).AddTicks(-1)) ?? DateTime.Now.Date.AddDays(1).AddTicks(-1);

            using var conn = _ctx.Create();

            var bondColumns = (await conn.QueryAsync<string>(@"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Bonds'")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hasIsDeleted = bondColumns.Contains("IsDeleted");
            var hasBondNumber = bondColumns.Contains("BondNumber");
            var hasClientId = bondColumns.Contains("ClientId");
            var hasAgentId = bondColumns.Contains("AgentId");
            var hasCreatedAt = bondColumns.Contains("CreatedAt");
            var hasCommissionAmount = bondColumns.Contains("CommissionAmount");
            var hasBankCharge = bondColumns.Contains("BankCharge");
            var hasTotalBankCharge = bondColumns.Contains("TotalBankCharge");
            var hasApplicationFee = bondColumns.Contains("ApplicationFee");
            var hasProcuringEntity = bondColumns.Contains("ProcuringEntity");
            var hasBondTypeId = bondColumns.Contains("BondTypeId");
            var hasFacilityType = bondColumns.Contains("FacilityType");
            var hasIsApproved = bondColumns.Contains("isApproved");

            var referenceExpr = hasBondNumber
                ? "ISNULL(NULLIF(LTRIM(RTRIM(CAST(b.BondNumber AS NVARCHAR(100)))), ''), CAST(b.Id AS NVARCHAR(50)))"
                : "CAST(b.Id AS NVARCHAR(50))";
            var clientJoin = hasClientId
                ? "LEFT JOIN Clients c ON CAST(c.Id AS NVARCHAR(50)) = CAST(b.ClientId AS NVARCHAR(50))"
                : "LEFT JOIN Clients c ON 1 = 0";
            var userJoin = hasAgentId
                ? "LEFT JOIN SystemUsers u ON CAST(u.Id AS NVARCHAR(50)) = CAST(b.AgentId AS NVARCHAR(50))"
                : "LEFT JOIN SystemUsers u ON 1 = 0";
            var isDeletedCondition = hasIsDeleted ? "AND ISNULL(b.IsDeleted, 0) = 0" : string.Empty;
            var isApprovedCondition = hasIsApproved ? "AND ISNULL(b.isApproved, 0) = 1" : string.Empty;
            var userFilterCondition = hasAgentId ? "AND (@userId IS NULL OR CAST(b.AgentId AS NVARCHAR(50)) = @userId)" : string.Empty;
            var rawFacilityExpr = hasBondTypeId && hasFacilityType
                ? "COALESCE(NULLIF(b.BondTypeId, 0), NULLIF(b.FacilityType, 0), 1)"
                : hasBondTypeId
                    ? "COALESCE(NULLIF(b.BondTypeId, 0), 1)"
                    : hasFacilityType
                        ? "COALESCE(NULLIF(b.FacilityType, 0), 1)"
                        : "1";
            // Normalize product type so legacy/non-standard values still map to Bid Bond.
            var facilityTypeExpr = $"CASE WHEN ({rawFacilityExpr}) = 2 THEN 2 WHEN ({rawFacilityExpr}) = 3 THEN 3 ELSE 1 END";
            var facilityTypeCondition = $"AND (@facilityType IS NULL OR ({facilityTypeExpr}) = @facilityType)";
            // Product type in Bonds is stored as BondTypeId. Fallback to FacilityType where available.

            var createdAtExpr = hasCreatedAt ? "b.CreatedAt" : "GETDATE()";
            var commissionExpr = hasCommissionAmount ? "ISNULL(b.CommissionAmount, 0)" : "0";
            var bankChargeExpr = hasTotalBankCharge
                ? "ISNULL(b.TotalBankCharge, ISNULL(b.BankCharge, 0))"
                : hasBankCharge
                    ? "ISNULL(b.BankCharge, 0)"
                    : "0";
            var commissionBaseExpr = hasApplicationFee
                ? $"ISNULL(b.ApplicationFee, ({commissionExpr} - {bankChargeExpr}))"
                : $"({commissionExpr} - {bankChargeExpr})";
            var procuringEntityExpr = hasProcuringEntity ? "ISNULL(CAST(b.ProcuringEntity AS NVARCHAR(300)), '')" : "''";

            var sql = $@"
SELECT
    {createdAtExpr} AS ApplicationDate,
    {referenceExpr} AS Reference,
    ISNULL(c.CompanyName, '') AS ClientName,
    {procuringEntityExpr} AS ProcuringEntity,
    ISNULL(CAST(u.Id AS NVARCHAR(50)), '') AS UserId,
    ISNULL(u.FullName, 'Unassigned') AS UserName,
    ISNULL(u.CommissionPercent, 0) AS CommissionPercent,
    {commissionExpr} AS ClientCharges,
    {bankChargeExpr} AS BankCharges,
    {facilityTypeExpr} AS FacilityType,
    CASE
        WHEN ({commissionBaseExpr}) > 0
        THEN ({commissionBaseExpr})
        ELSE 0
    END AS CommissionBase,
    CASE
        WHEN ({commissionBaseExpr}) > 0
        THEN ({commissionBaseExpr}) * ISNULL(u.CommissionPercent, 0) / 100.0
        ELSE 0
    END AS CommissionAmount
FROM Bonds b
{clientJoin}
{userJoin}
WHERE {createdAtExpr} BETWEEN @from AND @to
  {isDeletedCondition}
    {isApprovedCondition}
  {userFilterCondition}
  {facilityTypeCondition}
ORDER BY {createdAtExpr} DESC";

            var details = (await conn.QueryAsync<UserCommissionLine>(sql, new { from = startDate, to = endDate, userId, facilityType })).ToList();

            if (!string.IsNullOrWhiteSpace(userId))
            {
                details = details
                    .Where(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (facilityType.HasValue)
            {
                details = details
                    .Where(x => x.FacilityType == facilityType.Value)
                    .ToList();
            }

            var summary = details
                .GroupBy(x => new { x.UserId, x.UserName, x.CommissionPercent })
                .Select(g => new UserCommissionSummaryLine
                {
                    UserId = g.Key.UserId,
                    UserName = g.Key.UserName,
                    CommissionPercent = g.Key.CommissionPercent,
                    Applications = g.Count(),
                    ClientCharges = g.Sum(x => x.ClientCharges),
                    BankCharges = g.Sum(x => x.BankCharges),
                    CommissionBase = g.Sum(x => x.CommissionBase),
                    CommissionAmount = g.Sum(x => x.CommissionAmount)
                })
                .OrderByDescending(x => x.CommissionAmount)
                .ToList();

            var model = new UserCommissionReportViewModel
            {
                Details = details,
                Summary = summary,
                TotalApplications = details.Count,
                TotalCommissionBase = details.Sum(x => x.CommissionBase),
                TotalCommission = details.Sum(x => x.CommissionAmount),
                Users = (await conn.QueryAsync<UserDropdownItem>(@"
                    SELECT Id, FullName 
                    FROM SystemUsers 
                    WHERE ISNULL(IsDeleted, 0) = 0 
                    ORDER BY FullName")).ToList()
            };

            ViewBag.From = from.HasValue ? from.Value.ToString("yyyy-MM-dd") : string.Empty;
            ViewBag.To = to.HasValue ? to.Value.ToString("yyyy-MM-dd") : string.Empty;
            ViewBag.UserId = userId ?? string.Empty;
            ViewBag.FacilityType = facilityType?.ToString() ?? string.Empty;
            return View(model);
        }

        public async Task<IActionResult> UserCommissionsPdf(DateTime? from, DateTime? to, string? userId, int? facilityType)
        {
            if (string.IsNullOrWhiteSpace(userId)) userId = null;
            var startDate = from?.Date ?? new DateTime(DateTime.Now.Year, 1, 1);
            var endDate = (to?.Date.AddDays(1).AddTicks(-1)) ?? DateTime.Now.Date.AddDays(1).AddTicks(-1);

            using var conn = _ctx.Create();

            var bondColumns = (await conn.QueryAsync<string>(@"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Bonds'")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hasIsDeleted = bondColumns.Contains("IsDeleted");
            var hasBondNumber = bondColumns.Contains("BondNumber");
            var hasClientId = bondColumns.Contains("ClientId");
            var hasAgentId = bondColumns.Contains("AgentId");
            var hasCreatedAt = bondColumns.Contains("CreatedAt");
            var hasCommissionAmount = bondColumns.Contains("CommissionAmount");
            var hasBankCharge = bondColumns.Contains("BankCharge");
            var hasTotalBankCharge = bondColumns.Contains("TotalBankCharge");
            var hasApplicationFee = bondColumns.Contains("ApplicationFee");
            var hasProcuringEntity = bondColumns.Contains("ProcuringEntity");
            var hasBondTypeId = bondColumns.Contains("BondTypeId");
            var hasFacilityType = bondColumns.Contains("FacilityType");
            var hasIsApproved = bondColumns.Contains("isApproved");

            var referenceExpr = hasBondNumber
                ? "ISNULL(NULLIF(LTRIM(RTRIM(CAST(b.BondNumber AS NVARCHAR(100)))), ''), CAST(b.Id AS NVARCHAR(50)))"
                : "CAST(b.Id AS NVARCHAR(50))";
            var clientJoin = hasClientId
                ? "LEFT JOIN Clients c ON CAST(c.Id AS NVARCHAR(50)) = CAST(b.ClientId AS NVARCHAR(50))"
                : "LEFT JOIN Clients c ON 1 = 0";
            var userJoin = hasAgentId
                ? "LEFT JOIN SystemUsers u ON CAST(u.Id AS NVARCHAR(50)) = CAST(b.AgentId AS NVARCHAR(50))"
                : "LEFT JOIN SystemUsers u ON 1 = 0";
            var isDeletedCondition = hasIsDeleted ? "AND ISNULL(b.IsDeleted, 0) = 0" : string.Empty;
            var isApprovedCondition = hasIsApproved ? "AND ISNULL(b.isApproved, 0) = 1" : string.Empty;
            var userFilterCondition = hasAgentId ? "AND (@userId IS NULL OR CAST(b.AgentId AS NVARCHAR(50)) = @userId)" : string.Empty;
            var rawFacilityExpr = hasBondTypeId && hasFacilityType
                ? "COALESCE(NULLIF(b.BondTypeId, 0), NULLIF(b.FacilityType, 0), 1)"
                : hasBondTypeId
                    ? "COALESCE(NULLIF(b.BondTypeId, 0), 1)"
                    : hasFacilityType
                        ? "COALESCE(NULLIF(b.FacilityType, 0), 1)"
                        : "1";
            // Normalize product type so legacy/non-standard values still map to Bid Bond.
            var facilityTypeExpr = $"CASE WHEN ({rawFacilityExpr}) = 2 THEN 2 WHEN ({rawFacilityExpr}) = 3 THEN 3 ELSE 1 END";
            var facilityTypeCondition = $"AND (@facilityType IS NULL OR ({facilityTypeExpr}) = @facilityType)";
            var createdAtExpr = hasCreatedAt ? "b.CreatedAt" : "GETDATE()";
            var commissionExpr = hasCommissionAmount ? "ISNULL(b.CommissionAmount, 0)" : "0";
            var bankChargeExpr = hasTotalBankCharge
                ? "ISNULL(b.TotalBankCharge, ISNULL(b.BankCharge, 0))"
                : hasBankCharge
                    ? "ISNULL(b.BankCharge, 0)"
                    : "0";
            var commissionBaseExpr = hasApplicationFee
                ? $"ISNULL(b.ApplicationFee, ({commissionExpr} - {bankChargeExpr}))"
                : $"({commissionExpr} - {bankChargeExpr})";
            var procuringEntityExpr = hasProcuringEntity ? "ISNULL(CAST(b.ProcuringEntity AS NVARCHAR(300)), '')" : "''";

            var sql = $@"
SELECT
    {createdAtExpr} AS ApplicationDate,
    {referenceExpr} AS Reference,
    ISNULL(c.CompanyName, '') AS ClientName,
    {procuringEntityExpr} AS ProcuringEntity,
    ISNULL(CAST(u.Id AS NVARCHAR(50)), '') AS UserId,
    ISNULL(u.FullName, 'Unassigned') AS UserName,
    ISNULL(u.CommissionPercent, 0) AS CommissionPercent,
    {commissionExpr} AS ClientCharges,
    {bankChargeExpr} AS BankCharges,
    {facilityTypeExpr} AS FacilityType,
    CASE WHEN ({commissionBaseExpr}) > 0 THEN ({commissionBaseExpr}) ELSE 0 END AS CommissionBase,
    CASE WHEN ({commissionBaseExpr}) > 0 THEN ({commissionBaseExpr}) * ISNULL(u.CommissionPercent, 0) / 100.0 ELSE 0 END AS CommissionAmount
FROM Bonds b
{clientJoin}
{userJoin}
WHERE {createdAtExpr} BETWEEN @from AND @to
  {isDeletedCondition}
    {isApprovedCondition}
  {userFilterCondition}
  {facilityTypeCondition}
ORDER BY {createdAtExpr} DESC";

            var details = (await conn.QueryAsync<UserCommissionLine>(sql, new { from = startDate, to = endDate, userId, facilityType })).ToList();

            if (!string.IsNullOrWhiteSpace(userId))
            {
                details = details
                    .Where(x => string.Equals(x.UserId, userId, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            if (facilityType.HasValue)
            {
                details = details
                    .Where(x => x.FacilityType == facilityType.Value)
                    .ToList();
            }

            var summary = details
                .GroupBy(x => new { x.UserId, x.UserName, x.CommissionPercent })
                .Select(g => new UserCommissionSummaryLine
                {
                    UserId = g.Key.UserId,
                    UserName = g.Key.UserName,
                    CommissionPercent = g.Key.CommissionPercent,
                    Applications = g.Count(),
                    ClientCharges = g.Sum(x => x.ClientCharges),
                    BankCharges = g.Sum(x => x.BankCharges),
                    CommissionBase = g.Sum(x => x.CommissionBase),
                    CommissionAmount = g.Sum(x => x.CommissionAmount)
                })
                .OrderByDescending(x => x.CommissionAmount)
                .ToList();

            var model = new UserCommissionReportViewModel
            {
                Details = details,
                Summary = summary,
                TotalApplications = details.Count,
                TotalCommissionBase = details.Sum(x => x.CommissionBase),
                TotalCommission = details.Sum(x => x.CommissionAmount)
            };

            string? filterUserName = null;
            if (!string.IsNullOrWhiteSpace(userId))
            {
                var user = await conn.QueryFirstOrDefaultAsync<UserDropdownItem>(
                    "SELECT Id, FullName FROM SystemUsers WHERE CAST(Id AS NVARCHAR(50)) = @userId",
                    new { userId });
                filterUserName = user?.FullName;
            }

            var filterProductType = facilityType switch
            {
                1 => "Bid Bond",
                2 => "Performance Bond",
                3 => "Advance Payment",
                _ => null
            };

            byte[]? logoBytes = null;
            try
            {
                var logoPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "images", "onwards-logo.jpg");
                if (System.IO.File.Exists(logoPath))
                {
                    logoBytes = await System.IO.File.ReadAllBytesAsync(logoPath);
                }
            }
            catch
            {
                // swallow - generator will fallback to text header
            }

            var bytes = UserCommissionPdfGenerator.Generate(model, from, to, filterUserName, filterProductType, logoBytes);
            var filename = $"UserCommissions_{DateTime.Now:yyyyMMdd_HHmm}.pdf";
            return File(bytes, "application/pdf", filename);
        }

        public async Task<IActionResult> Revenue(DateTime? from, DateTime? to)
        {
            var startDate = from ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var endDate = to ?? DateTime.Now.Date.AddDays(1).AddTicks(-1);

            using var conn = _ctx.Create();

            // JOIN Path: GL -> Invoices -> Clients (for name) & BidBonds (for project context)
            const string sql = @"
        SELECT 
            gl.CreatedAt as Date, 
            i.InvoiceNumber as Reference,
            c.CompanyName as ClientName, 
            gl.Description, 
            gl.Amount,
            b.Id as BondNumber
        FROM GeneralLedger gl
        INNER JOIN Invoices i ON gl.ReferenceId = i.Id
        LEFT JOIN Clients c ON i.ClientId = c.Id
        LEFT JOIN Bonds b ON i.BondId = b.Id
        WHERE gl.TransactionType = 'REVENUE' 
        AND gl.CreatedAt BETWEEN @from AND @to
        ORDER BY gl.CreatedAt DESC";

            var details = await conn.QueryAsync<RevenueLine>(sql, new { from = startDate, to = endDate });

            var model = new RevenueReportViewModel
            {
                Details = details.ToList(),
                TotalRevenue = details.Sum(x => x.Amount)
            };

            ViewBag.From = startDate;
            ViewBag.To = endDate;
            return View(model);
        }

        public async Task<IActionResult> GeneralLedger()
        {
            using var conn = _ctx.Create();
            const string sql = "SELECT * FROM GeneralLedger ORDER BY CreatedAt DESC";
            var entries = await conn.QueryAsync<dynamic>(sql);
            return View(entries);
        }

        public async Task<IActionResult> BankCharges(DateTime? from, DateTime? to)
        {
            var startDate = from ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var endDate = to ?? DateTime.Now.Date.AddDays(1).AddTicks(-1);

            using var conn = _ctx.Create();

            // JOIN Path: GL -> Invoices -> BidBonds (to see which bond caused the charge)
            const string sql = @"
 SELECT 
    gl.CreatedAt as Date, 
    i.InvoiceNumber as Reference,
    b.ProcuringEntity as RelatedEntity, 
    gl.Description, 
    gl.Amount,
    b.Id as BondNumber
FROM GeneralLedger gl
INNER JOIN Invoices i ON gl.ReferenceId = i.Id
LEFT JOIN Bonds b ON i.BondId = b.Id
        WHERE gl.TransactionType = 'BANK_CHARGE'
        AND gl.CreatedAt BETWEEN @from AND @to
        ORDER BY gl.CreatedAt DESC";

            var details = await conn.QueryAsync<BankChargeLine>(sql, new { from = startDate, to = endDate });

            var model = new BankChargeViewModel
            {
                Details = details.ToList(),
                TotalCharges = details.Sum(x => x.Amount)
            };

            ViewBag.From = startDate;
            ViewBag.To = endDate;
            return View(model);
        }

        public async Task<IActionResult> TradeFinance(DateTime? from, DateTime? to)
        {
            var startDate = from?.Date ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var endDate = (to?.Date.AddDays(1).AddTicks(-1)) ?? DateTime.Now.Date.AddDays(1).AddTicks(-1);

            using var conn = _ctx.Create();

            var bondColumns = (await conn.QueryAsync<string>(@"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Bonds'")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hasCreatedAt = bondColumns.Contains("CreatedAt");
            var hasIsDeleted = bondColumns.Contains("IsDeleted");
            var hasBankCharge = bondColumns.Contains("BankCharge");
            var hasCommissionAmount = bondColumns.Contains("CommissionAmount");
            var hasTaxPercentage = bondColumns.Contains("TaxPercentage");
            var hasTaxCalculation = bondColumns.Contains("TaxCalculation");
            var hasApplicationFee = bondColumns.Contains("ApplicationFee");
            var hasIsApproved = bondColumns.Contains("isApproved");

            var createdAtExpr = hasCreatedAt ? "b.CreatedAt" : "GETDATE()";
            var processingDateExpr = $"ISNULL(b.ProcessingDate, {createdAtExpr})";
            var isDeletedCondition = hasIsDeleted ? "AND ISNULL(b.IsDeleted, 0) = 0" : string.Empty;
            var isApprovedCondition = hasIsApproved ? "AND ISNULL(b.isApproved, 0) = 1" : string.Empty;
            var clientChargesExpr = hasCommissionAmount ? "ISNULL(b.CommissionAmount, 0)" : "0";
            var bankChargesExpr = hasBankCharge ? "ISNULL(b.BankCharge, 0)" : "0";
            var taxCalculationExpr = $"ROUND({bankChargesExpr} * 0.20, 2)";
            var netProfitExpr = $"({clientChargesExpr} - ({bankChargesExpr} + ROUND({bankChargesExpr} * 0.20, 2)))";

            var sql = $@"
SELECT
    {processingDateExpr} AS ApplicationDate,
    ISNULL(c.CompanyName, '') AS ClientName,
    ISNULL(b.Amount, 0) AS BondAmount,
    ISNULL(b.TenorDays, 0) AS TenorDays,
    ISNULL(bk.BankName, 'N/A') AS IssuingBank,
    {clientChargesExpr} AS ClientCharges,
    {bankChargesExpr} AS BankCharges,
    {taxCalculationExpr} AS TaxCalculation,
    {netProfitExpr} AS NetProfit
FROM Bonds b
LEFT JOIN Clients c ON c.Id = b.ClientId
LEFT JOIN Banks bk ON bk.Id = b.IssuingBank
WHERE {processingDateExpr} BETWEEN @from AND @to
  {isDeletedCondition}
  {isApprovedCondition}
ORDER BY {processingDateExpr} DESC";

            var details = (await conn.QueryAsync<TradeFinanceReportLine>(sql, new { from = startDate, to = endDate })).ToList();

            var model = new TradeFinanceReportViewModel
            {
                FromDate = startDate,
                ToDate = endDate,
                Details = details,
                TotalApplications = details.Count,
                TotalBondAmount = details.Sum(x => x.BondAmount),
                TotalClientCharges = details.Sum(x => x.ClientCharges),
                TotalTaxCalculation = details.Sum(x => x.TaxCalculation),
                TotalBankCharges = details.Sum(x => x.BankCharges),
                TotalNetProfit = details.Sum(x => x.NetProfit)
            };
        return View(model);
        }

        public async Task<IActionResult> TradeFinanceExcel(DateTime? from, DateTime? to)
        {
            var startDate = from?.Date ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var endDate = (to?.Date.AddDays(1).AddTicks(-1)) ?? DateTime.Now.Date.AddDays(1).AddTicks(-1);

            using var conn = _ctx.Create();

            var bondColumns = (await conn.QueryAsync<string>(@"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Bonds'")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hasCreatedAt = bondColumns.Contains("CreatedAt");
            var hasIsDeleted = bondColumns.Contains("IsDeleted");
            var hasBankCharge = bondColumns.Contains("BankCharge");
            var hasCommissionAmount = bondColumns.Contains("CommissionAmount");
            var hasTaxPercentage = bondColumns.Contains("TaxPercentage");
            var hasTaxCalculation = bondColumns.Contains("TaxCalculation");
            var hasApplicationFee = bondColumns.Contains("ApplicationFee");
            var hasIsApproved = bondColumns.Contains("isApproved");

            var createdAtExpr = hasCreatedAt ? "b.CreatedAt" : "GETDATE()";
            var processingDateExpr = $"ISNULL(b.ProcessingDate, {createdAtExpr})";
            var isDeletedCondition = hasIsDeleted ? "AND ISNULL(b.IsDeleted, 0) = 0" : string.Empty;
            var isApprovedCondition = hasIsApproved ? "AND ISNULL(b.isApproved, 0) = 1" : string.Empty;
            var clientChargesExpr = hasCommissionAmount ? "ISNULL(b.CommissionAmount, 0)" : "0";
            var bankChargesExpr = hasBankCharge ? "ISNULL(b.BankCharge, 0)" : "0";
            var taxCalculationExpr = $"ROUND({bankChargesExpr} * 0.20, 2)";
            var netProfitExpr = $"({clientChargesExpr} - ({bankChargesExpr} + ROUND({bankChargesExpr} * 0.20, 2)))";

            var sql = $@"
SELECT
    {processingDateExpr} AS ApplicationDate,
    ISNULL(c.CompanyName, '') AS ClientName,
    ISNULL(b.Amount, 0) AS BondAmount,
    ISNULL(b.TenorDays, 0) AS TenorDays,
    ISNULL(bk.BankName, 'N/A') AS IssuingBank,
    {clientChargesExpr} AS ClientCharges,
    {bankChargesExpr} AS BankCharges,
    {taxCalculationExpr} AS TaxCalculation,
    {netProfitExpr} AS NetProfit
FROM Bonds b
LEFT JOIN Clients c ON c.Id = b.ClientId
LEFT JOIN Banks bk ON bk.Id = b.IssuingBank
WHERE {processingDateExpr} BETWEEN @from AND @to
  {isDeletedCondition}
  {isApprovedCondition}
ORDER BY {processingDateExpr} DESC";

            var details = (await conn.QueryAsync<TradeFinanceReportLine>(sql, new { from = startDate, to = endDate })).ToList();

            // Build CSV (numbers use invariant formatting so Excel parses them)
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Date,Client,BondAmount,TenorDays,IssuingBank,ClientCharges,BankCharges,TaxCalculation,NetProfit");
            foreach (var d in details)
            {
                var date = d.ApplicationDate.ToString("dd-MMM-yyyy");
                var client = d.ClientName?.Replace("\"", "\"\"") ?? string.Empty;
                sb.Append('"').Append(date).Append('"').Append(',');
                sb.Append('"').Append(client).Append('"').Append(',');
                sb.Append(d.BondAmount.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(d.TenorDays).Append(',');
                var bank = (d.IssuingBank ?? string.Empty).Replace("\"", "\"\"");
                sb.Append('"').Append(bank).Append('"').Append(',');
                sb.Append(d.ClientCharges.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(d.BankCharges.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
                sb.Append(d.TaxCalculation.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
                sb.AppendLine(d.NetProfit.ToString("F2", CultureInfo.InvariantCulture));
            }

            // Totals row (match PDF summary)
            var totalBondAmount = details.Sum(x => x.BondAmount);
            var totalClientCharges = details.Sum(x => x.ClientCharges);
            var totalBankCharges = details.Sum(x => x.BankCharges);
            var totalTax = details.Sum(x => x.TaxCalculation);
            var totalNet = details.Sum(x => x.NetProfit);

            sb.Append(','); // Date empty
            sb.Append('"').Append("TOTALS").Append('"').Append(',');
            sb.Append(totalBondAmount.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(',');
            sb.Append(',');
            sb.Append(totalClientCharges.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(totalBankCharges.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.Append(totalTax.ToString("F2", CultureInfo.InvariantCulture)).Append(',');
            sb.AppendLine(totalNet.ToString("F2", CultureInfo.InvariantCulture));

            // Prefix UTF-8 BOM so Excel recognizes encoding
            var preamble = System.Text.Encoding.UTF8.GetPreamble();
            var data = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
            var bytes = new byte[preamble.Length + data.Length];
            Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
            Buffer.BlockCopy(data, 0, bytes, preamble.Length, data.Length);
            var filename = $"TradeFinance_{startDate:yyyyMMdd}_to_{endDate:yyyyMMdd}.csv";
            return File(bytes, "text/csv", filename);
        }

        public async Task<IActionResult> PendingClientCharges()
        {
            using var conn = _ctx.Create();

            var bondColumns = (await conn.QueryAsync<string>(@"
                SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Bonds'")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hasBondNumber       = bondColumns.Contains("BondNumber");
            var hasIsDeleted        = bondColumns.Contains("IsDeleted");
            var hasCreatedAt        = bondColumns.Contains("CreatedAt");
            var hasCommissionAmount = bondColumns.Contains("CommissionAmount");
            var hasBankCharge       = bondColumns.Contains("BankCharge");
            var hasTaxCalculation   = bondColumns.Contains("TaxCalculation");
            var hasBondPayments     = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = 'BondPayments'") > 0;

            var referenceExpr      = hasBondNumber
                ? "ISNULL(NULLIF(LTRIM(RTRIM(CAST(b.BondNumber AS NVARCHAR(100)))), ''), CAST(b.Id AS NVARCHAR(50)))"
                : "CAST(b.Id AS NVARCHAR(50))";
            var createdAtExpr      = hasCreatedAt ? "b.CreatedAt" : "GETDATE()";
            var isDeletedCondition = hasIsDeleted  ? "AND ISNULL(b.IsDeleted, 0) = 0"  : string.Empty;
            var grossClientChargesExpr = hasCommissionAmount ? "ISNULL(b.CommissionAmount, 0)" : "0";
            var paidAmountExpr = hasBondPayments
                ? "ISNULL((SELECT SUM(bp.AmountPaid) FROM BondPayments bp WHERE bp.BondId = b.Id), 0)"
                : "0";
            var clientChargesExpr = $"CASE WHEN ({grossClientChargesExpr} - {paidAmountExpr}) > 0 THEN ({grossClientChargesExpr} - {paidAmountExpr}) ELSE 0 END";
            var bankChargesExpr    = hasBankCharge ? "ISNULL(b.BankCharge, 0)" : "0";
            var taxExpr            = hasTaxCalculation
                ? "ISNULL(b.TaxCalculation, 0)"
                : $"ROUND({bankChargesExpr} * 0.20, 2)";
            var netProfitExpr      = $"({clientChargesExpr} - ({bankChargesExpr} + {taxExpr}))";

                        var sql = $@"
SELECT
    {createdAtExpr}                AS ApplicationDate,
    {referenceExpr}                AS Reference,
    ISNULL(c.CompanyName, 'N/A')   AS ClientName,
    ISNULL(b.Amount, 0)            AS BondAmount,
    ISNULL(b.TenorDays, 0)         AS TenorDays,
    ISNULL(bk.BankName, 'N/A')     AS IssuingBank,
    {clientChargesExpr}            AS ClientCharges,
    {bankChargesExpr}              AS BankCharges,
    {taxExpr}                      AS TaxCalculation,
    {netProfitExpr}                AS NetProfit
FROM Bonds b
LEFT JOIN Clients c  ON c.Id  = b.ClientId
LEFT JOIN Banks   bk ON bk.Id = b.IssuingBank
WHERE 1 = 1
  {isDeletedCondition}
    AND ({clientChargesExpr}) > 0
ORDER BY {createdAtExpr} DESC";

            var lines = (await conn.QueryAsync<PendingBondLine>(sql)).ToList();

            var model = new PendingClientChargesViewModel
            {
                TotalClients        = lines.Select(x => x.ClientName).Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                TotalBonds          = lines.Count,
                TotalClientCharges  = lines.Sum(x => x.ClientCharges),
                TotalBankCharges    = lines.Sum(x => x.BankCharges),
                TotalTaxCalculation = lines.Sum(x => x.TaxCalculation),
                TotalNetProfit      = lines.Sum(x => x.NetProfit),
                Bonds               = lines
            };

            return View(model);
        }
    }
}