using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.API.Controllers
{
    public class FinanceController : Controller
    {
        private readonly DapperContext _ctx;

        public FinanceController(DapperContext ctx)
        {
            _ctx = ctx;
        }

        // GET: Finance/Invoices
        public async Task<IActionResult> Invoices()
        {
            using var conn = _ctx.Create();

            const string sql = @"
                SELECT i.*, c.CompanyName as ClientName,
                (i.Amount - ISNULL((SELECT SUM(Credit) FROM ClientStatements WHERE InvoiceId = i.Id), 0)) as RemainingBalance
                FROM Invoices i 
                INNER JOIN Clients c ON i.ClientId = c.Id 
                ORDER BY i.Status ASC, i.CreatedAt DESC";

            var invoices = await conn.QueryAsync<dynamic>(sql);

            var banks = await conn.QueryAsync<SelectListItem>(
                "SELECT Id as Value, BankName as Text FROM InternalBanks ORDER BY BankName ASC");

            ViewBag.InternalBanks = banks;
            return View(invoices);
        }

        // POST: Finance/MarkAsPaid (Handles Partial & Full Payments)
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> MarkAsPaid(int invoiceId, int internalBankId, decimal amountPaid)
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;

            using var conn = _ctx.Create();
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
                var inv = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT ClientId, Amount, InvoiceNumber FROM Invoices WHERE Id = @Id",
                    new { Id = invoiceId }, trans);

                if (inv == null) return NotFound();

                await conn.ExecuteAsync(@"
                    INSERT INTO ClientStatements (ClientId, InvoiceId, Description, Debit, Credit, CreatedAt)
                    VALUES (@ClientId, @InvId, @Desc, 0, @Amount, GETDATE())",
                    new
                    {
                        ClientId = inv.ClientId,
                        InvId = invoiceId,
                        Desc = $"Payment - Inv #{inv.InvoiceNumber}",
                        Amount = amountPaid
                    }, trans);

                await conn.ExecuteAsync(@"
                    INSERT INTO GeneralLedger (TransactionType, ReferenceId, Amount, Description, EntryType, CreatedAt)
                    VALUES ('BANK_DEPOSIT', @InvId, @Amount, @Desc, 'DEBIT', GETDATE())",
                    new
                    {
                        InvId = invoiceId,
                        Amount = amountPaid,
                        Desc = $"Cash Receipt - Inv #{inv.InvoiceNumber}"
                    }, trans);

                const string statusCheckSql = @"
                    SELECT CASE 
                        WHEN (SELECT SUM(Credit) FROM ClientStatements WHERE InvoiceId = @Id) >= @Total 
                        THEN 1 ELSE 0 END";

                int isSettled = await conn.ExecuteScalarAsync<int>(statusCheckSql,
                    new { Id = invoiceId, Total = (decimal)inv.Amount }, trans);

                if (isSettled == 1)
                {
                    await conn.ExecuteAsync(
                        "UPDATE Invoices SET Status = 1, DatePaid = GETDATE() WHERE Id = @Id",
                        new { Id = invoiceId }, trans);
                }

                trans.Commit();
                TempData["Success"] = $"Successfully posted payment of KES {amountPaid:N2}";
            }
            catch (Exception ex)
            {
                trans.Rollback();
                TempData["Error"] = "Accounting Engine Error: " + ex.Message;
            }

            return RedirectToAction(nameof(Invoices));
        }

        public async Task<IActionResult> Statements(int? clientId, DateTime? from, DateTime? to, int[]? bondTypeIds = null)
        {
            using var conn = _ctx.Create();
            var clients = await conn.QueryAsync<dynamic>(
                "SELECT Id, CompanyName FROM Clients ORDER BY CompanyName ASC");
            var productTypes = await conn.QueryAsync<dynamic>("SELECT Id, ProductName FROM ProductTypes ORDER BY ProductName");
            ViewBag.Clients = clients;
            ViewBag.ProductTypeList = productTypes;
            ViewBag.SelectedClientId = clientId;
            ViewBag.DefaultFrom = (from?.Date ?? DateTime.Today.AddMonths(-1)).ToString("yyyy-MM-dd");
            ViewBag.DefaultTo = (to?.Date ?? DateTime.Today).ToString("yyyy-MM-dd");
            ViewBag.SelectedBondTypeIds = bondTypeIds ?? Array.Empty<int>();
            return View();
        }

        // GET: Finance/GenerateStatement
        public async Task<IActionResult> GenerateStatement(int clientId, DateTime? from, DateTime? to, int[]? bondTypeIds = null)
        {
            var dateFrom = from?.Date ?? DateTime.Today.AddMonths(-1);
            var dateTo = to?.Date ?? DateTime.Today;

            if (dateFrom > dateTo)
            {
                TempData["Error"] = "Invalid date range. 'From Date' must be earlier than or equal to 'To Date'.";
                return RedirectToAction(nameof(Statements), new { clientId, from = dateFrom.ToString("yyyy-MM-dd"), to = dateTo.ToString("yyyy-MM-dd") });
            }

            using var conn = _ctx.Create();

            // 1. Fetch Client Details
            var client = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, CompanyName as Name, Email, Phone as PhoneNumber FROM Clients WHERE Id = @Id",
                new { Id = clientId });

            // 2. Fetch full history for the client (base ledger lines)
            const string sql = @"
        SELECT CreatedAt as TransactionDate, Description, Debit, Credit
        FROM ClientStatements
        WHERE ClientId = @Id";

            var allHistory = (await conn.QueryAsync<StatementLine>(sql, new { Id = clientId })).ToList();

            // 3. Fetch bonds with schema-aware SQL (supports environments with partial migrations)
            // Fetch ProductTypes available for this client's bonds (for the filter display on preview page)
            var productTypeList = await conn.QueryAsync<dynamic>("SELECT Id, ProductName FROM ProductTypes ORDER BY ProductName");
            ViewBag.ProductTypeList = productTypeList;

            var bondColumns = (await conn.QueryAsync<string>(@"
                SELECT c.name
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                WHERE o.name = 'Bonds' AND o.type = 'U'")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var bondNumberExpr = bondColumns.Contains("BondNumber")
                ? "ISNULL(b.BondNumber, CAST(b.Id AS NVARCHAR(50)))"
                : "CAST(b.Id AS NVARCHAR(50))";
            var appFeeExpr = bondColumns.Contains("ApplicationFee") ? "ISNULL(b.ApplicationFee, 0)" : "0";
            var commissionExpr = bondColumns.Contains("CommissionAmount") ? "ISNULL(b.CommissionAmount, 0)" : "0";
            var bankChargeExpr = bondColumns.Contains("BankCharge") ? "ISNULL(b.BankCharge, 0)" : "0";
            var createdAtExpr = bondColumns.Contains("CreatedAt") ? "b.CreatedAt" : "GETDATE()";

            var activeIds = bondTypeIds?.Where(id => id > 0).ToList() ?? new List<int>();

            var whereConditions = new List<string> { "b.ClientId = @Id" };
            if (bondColumns.Contains("CreatedAt"))
                whereConditions.Add("b.CreatedAt BETWEEN @From AND @To");
            if (activeIds.Count > 0)
                whereConditions.Add("b.BondTypeId IN @BondTypeIds");
            var whereClause = "WHERE " + string.Join(" AND ", whereConditions);
            var orderByClause = bondColumns.Contains("CreatedAt") ? "ORDER BY b.CreatedAt DESC" : "ORDER BY b.Id DESC";

            var bondSql = $@"
                SELECT
                    b.Id,
                    {bondNumberExpr} AS BondNumber,
                    {appFeeExpr} AS ApplicationFee,
                    {commissionExpr} AS CommissionAmount,
                    {bankChargeExpr} AS BankCharge,
                    {createdAtExpr} AS CreatedAt,
                    ISNULL(pt.ProductName, '') AS ProductType
                FROM Bonds b
                LEFT JOIN ProductTypes pt ON pt.Id = b.BondTypeId
                {whereClause}
                {orderByClause}";

            var bonds = (await conn.QueryAsync<dynamic>(bondSql, new { Id = clientId, From = dateFrom, To = dateTo, BondTypeIds = activeIds })).ToList();
            ViewBag.Bonds = bonds;

            // 4. Inject bond-created transaction lines into the ledger so they appear inline
            foreach (var b in bonds)
            {
                DateTime created = b.CreatedAt;
                decimal appFee = b.ApplicationFee != null ? (decimal)b.ApplicationFee : 0m;
                decimal commission = b.CommissionAmount != null ? (decimal)b.CommissionAmount : 0m;
                decimal bankCharge = b.BankCharge != null ? (decimal)b.BankCharge : 0m;

                // Aggregate client-facing charges as Debit, bank charge as Credit (keeps audit trail compact)
                var line = new StatementLine
                {
                    TransactionDate = created,
                    Description = $"Bond { (string.IsNullOrEmpty((string)b.BondNumber) ? ("#" + b.Id) : b.BondNumber) } - Charges",
                    Debit = appFee + commission,
                    Credit = bankCharge
                };

                allHistory.Add(line);
            }

            var format = Request.Query["format"].ToString();

            // 5. Calculation Engine (recompute running balances including injected bond lines)
            decimal runningBalance = 0;
            decimal broughtForward = 0;
            var filteredLines = new List<StatementLine>();

            var endOfDayTo = dateTo.AddDays(1).AddTicks(-1);

            // sort all history by date ascending
            var ordered = allHistory.OrderBy(x => x.TransactionDate).ToList();

            foreach (var tx in ordered)
            {
                runningBalance += (tx.Debit - tx.Credit);

                if (tx.TransactionDate < dateFrom)
                {
                    broughtForward = runningBalance;
                }
                else if (tx.TransactionDate >= dateFrom && tx.TransactionDate <= endOfDayTo)
                {
                    tx.RunningBalance = runningBalance;
                    filteredLines.Add(tx);
                }
            }

            ViewBag.ClientId = clientId;
            ViewBag.Client = client;
            ViewBag.From = dateFrom;
            ViewBag.To = dateTo;
            ViewBag.Bof = broughtForward;
            ViewBag.TotalDue = runningBalance;
            ViewBag.SelectedBondTypeIds = activeIds;

            // CSV export
            if (!string.IsNullOrWhiteSpace(format) && format.Equals("csv", StringComparison.OrdinalIgnoreCase))
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Client:,{client?.Name}");
                sb.AppendLine($"Phone:,{client?.PhoneNumber}");
                sb.AppendLine($"Email:,{client?.Email}");
                sb.AppendLine($"Period:,{dateFrom:yyyy-MM-dd} to {dateTo:yyyy-MM-dd}");
                sb.AppendLine();

                sb.AppendLine("Date,Description,Debit,Credit,RunningBalance");

                foreach (var ln in filteredLines)
                {
                    string desc = ln.Description?.Replace("\"", "\"\"") ?? string.Empty;
                    sb.AppendLine($"{ln.TransactionDate:yyyy-MM-dd},\"{desc}\",{ln.Debit:0.00},{ln.Credit:0.00},{ln.RunningBalance:0.00}");
                }

                sb.AppendLine();
                sb.AppendLine($",Brought Forward,,,{broughtForward:0.00}");
                sb.AppendLine($",Closing Balance,,,{runningBalance:0.00}");


                var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                var clientNameSafe = (client?.Name ?? "client").ToString().Replace(" ", "_").Replace(",", "");
                var fileName = $"{clientNameSafe}_Statement_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                return File(bytes, "text/csv", fileName);
            }
                if (!string.IsNullOrWhiteSpace(format) && format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var pdfBytes = OnwardsSwift.API.Services.StatementPdfGenerator.Generate(
                        Convert.ToString(client?.Name) ?? string.Empty,
                        Convert.ToString(client?.Email) ?? string.Empty,
                        Convert.ToString(client?.PhoneNumber) ?? string.Empty,
                        filteredLines,
                        dateFrom,
                        dateTo,
                        broughtForward,
                        runningBalance);

                    var clientNameSafePdf = (client?.Name ?? "client").ToString().Replace(" ", "_").Replace(",", "");
                    var pdfFile = $"{clientNameSafePdf}_Statement_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                    return File(pdfBytes, "application/pdf", pdfFile);
                }

            ViewBag.Lines = filteredLines;
            return View(filteredLines);
        }

        // GET: Finance/GenerateInvoicePdf
        public async Task<IActionResult> GenerateInvoicePdf(int clientId, DateTime? from, DateTime? to)
        {
            var dateFrom = from?.Date ?? DateTime.Today.AddMonths(-1);
            var dateTo = to?.Date ?? DateTime.Today;

            if (dateFrom > dateTo)
            {
                TempData["Error"] = "Invalid date range. 'From Date' must be earlier than or equal to 'To Date'.";
                return RedirectToAction(nameof(Statements), new { clientId, from = dateFrom.ToString("yyyy-MM-dd"), to = dateTo.ToString("yyyy-MM-dd") });
            }

            using var conn = _ctx.Create();

            var client = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, CompanyName as Name, Email, Phone as PhoneNumber FROM Clients WHERE Id = @Id",
                new { Id = clientId });

            var invoiceColumns = (await conn.QueryAsync<string>(@"
                SELECT c.name
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                WHERE o.name = 'Invoices' AND o.type = 'U'"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var invoiceNumberExpr = invoiceColumns.Contains("InvoiceNumber") ? "ISNULL(InvoiceNumber, CAST(Id AS NVARCHAR(50)))" : "CAST(Id AS NVARCHAR(50))";
            var descExpr = invoiceColumns.Contains("Description") ? "ISNULL(Description, 'CHARGES')" : "'CHARGES'";
            var amountExpr = invoiceColumns.Contains("Amount") ? "ISNULL(Amount, 0)" : "0";
            var createdAtExpr = invoiceColumns.Contains("CreatedAt") ? "CreatedAt" : "GETDATE()";
            var dueDateExpr = invoiceColumns.Contains("DueDate") ? "DueDate" : (invoiceColumns.Contains("InvoiceDueDate") ? "InvoiceDueDate" : createdAtExpr);
            var whereByDate = invoiceColumns.Contains("CreatedAt")
                ? "AND CreatedAt BETWEEN @From AND @To"
                : (invoiceColumns.Contains("DueDate") ? "AND DueDate BETWEEN @From AND @To" : string.Empty);

            var invoiceSql = $@"
                SELECT
                    {invoiceNumberExpr} AS InvoiceNumber,
                    {descExpr} AS Description,
                    CAST(1 AS DECIMAL(18,2)) AS Quantity,
                    {amountExpr} AS UnitPrice,
                    {amountExpr} AS Amount,
                    {createdAtExpr} AS InvoiceDate,
                    {dueDateExpr} AS DueDate
                FROM Invoices
                WHERE ClientId = @Id {whereByDate}
                ORDER BY {createdAtExpr} DESC";

            var lines = (await conn.QueryAsync<OnwardsSwift.API.Services.InvoicePdfLine>(
                invoiceSql,
                new { Id = clientId, From = dateFrom, To = dateTo })).ToList();

            var pdfBytes = OnwardsSwift.API.Services.InvoicePdfGenerator.Generate(
                Convert.ToString(client?.Name) ?? string.Empty,
                Convert.ToString(client?.Email) ?? string.Empty,
                Convert.ToString(client?.PhoneNumber) ?? string.Empty,
                dateFrom,
                dateTo,
                lines);

            var clientNameSafe = (client?.Name ?? "client").ToString().Replace(" ", "_").Replace(",", "");
            var fileName = $"{clientNameSafe}_Invoice_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
            return File(pdfBytes, "application/pdf", fileName);
        }
    }
}