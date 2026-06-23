using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.API.Controllers
{
    public class FinanceController : Controller
    {
        private readonly DapperContext _ctx;
        private readonly IWebHostEnvironment _env;

        public FinanceController(DapperContext ctx, IWebHostEnvironment env)
        {
            _ctx = ctx;
            _env = env;
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
            ViewBag.DefaultFrom = from?.Date.ToString("yyyy-MM-dd") ?? string.Empty;
            ViewBag.DefaultTo = to?.Date.ToString("yyyy-MM-dd") ?? string.Empty;
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
                                @"SELECT
                                        Id,
                                        CompanyName as Name,
                                        ISNULL(ContactPerson, '') as ContactPerson,
                                        ISNULL(Email, '') as Email,
                                        ISNULL(Phone, '') as PhoneNumber,
                                        ISNULL(KraPin, '') as KraPin,
                                        ISNULL(BusinessRegNumber, '') as BusinessRegNumber,
                                        ISNULL(PhysicalAddress, '') as PhysicalAddress,
                                        ISNULL(PostalAddress, '') as PostalAddress
                                    FROM Clients
                                    WHERE Id = @Id",
                                new { Id = clientId });

            // 2. Fetch full history for the client (base ledger lines)
            const string sql = @"
        SELECT CreatedAt as TransactionDate, Description, Debit, Credit
        FROM ClientStatements
        WHERE ClientId = @Id";

            var allHistory = (await conn.QueryAsync<StatementLine>(sql, new { Id = clientId })).ToList();

            // Keep non-bond legacy lines only (e.g. invoice receipts). Bond charges/payments
            // are built from Bonds/BondPayments below to avoid duplicate statement rows.
            allHistory = allHistory
                .Where(x => !IsBondStatementDescription(x.Description))
                .Select(x => new StatementLine
                {
                    TransactionDate = x.TransactionDate,
                    Description = x.Description ?? string.Empty,
                    ProductItem = x.Description ?? string.Empty,
                    ProcuringEntity = x.ProcuringEntity ?? string.Empty,
                    ReferenceNo = string.Empty,
                    BondAmount = x.BondAmount,
                    Debit = x.Debit < 0 ? 0 : x.Debit,
                    Credit = x.Credit < 0 ? 0 : x.Credit
                })
                .ToList();

            var statementGroups = allHistory
                .OrderBy(x => x.TransactionDate)
                .Select(x => (AnchorDate: x.TransactionDate, Rows: new List<StatementLine> { x }))
                .ToList();

            // 3. Fetch bonds with schema-aware SQL (supports environments with partial migrations)
            // Fetch ProductTypes available for this client's bonds (for the filter display on preview page)
            var productTypeList = await conn.QueryAsync<dynamic>("SELECT Id, ProductName FROM ProductTypes ORDER BY ProductName");
            ViewBag.ProductTypeList = productTypeList;

            var bondColumns = (await conn.QueryAsync<string>(@"
                SELECT c.name
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                WHERE o.name = 'Bonds' AND o.type = 'U'")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hasBondPayments = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = 'BondPayments'") > 0;

            var bondNumberExpr = bondColumns.Contains("BondNumber")
                ? "ISNULL(b.BondNumber, CAST(b.Id AS NVARCHAR(50)))"
                : "CAST(b.Id AS NVARCHAR(50))";
            var procuringEntityExpr = bondColumns.Contains("ProcuringEntity")
                ? "ISNULL(NULLIF(LTRIM(RTRIM(b.ProcuringEntity)), ''), '')"
                : "''";
            var bondAmountExpr = bondColumns.Contains("Amount") ? "ISNULL(b.Amount, 0)" : "0";
            var appFeeExpr = bondColumns.Contains("ApplicationFee") ? "ISNULL(b.ApplicationFee, 0)" : "0";
            var commissionExpr = bondColumns.Contains("CommissionAmount") ? "ISNULL(b.CommissionAmount, 0)" : "0";
            var bankChargeExpr = bondColumns.Contains("BankCharge") ? "ISNULL(b.BankCharge, 0)" : "0";
            var clientChargeExpr = bondColumns.Contains("CommissionAmount")
                ? "ISNULL(b.CommissionAmount, ISNULL(b.ApplicationFee, 0) + ISNULL(b.BankCharge, 0))"
                : "ISNULL(b.ApplicationFee, 0) + ISNULL(b.BankCharge, 0)";
            var paidAmountExpr = hasBondPayments
                ? "ISNULL((SELECT SUM(bp.AmountPaid) FROM BondPayments bp WHERE bp.BondId = b.Id), 0)"
                : "0";
            var createdAtExpr = bondColumns.Contains("CreatedAt") ? "b.CreatedAt" : "GETDATE()";
            var hasIsApproved = bondColumns.Contains("isApproved");

            var activeIds = bondTypeIds?.Where(id => id > 0).ToList() ?? new List<int>();

            var whereConditions = new List<string> { "b.ClientId = @Id" };
            if (hasIsApproved)
                whereConditions.Add("ISNULL(b.isApproved, 0) = 1");
            if (activeIds.Count > 0)
                whereConditions.Add("b.BondTypeId IN @BondTypeIds");
            var whereClause = "WHERE " + string.Join(" AND ", whereConditions);
            var orderByClause = bondColumns.Contains("CreatedAt") ? "ORDER BY b.CreatedAt DESC" : "ORDER BY b.Id DESC";

            var bondSql = $@"
                SELECT
                    b.Id,
                    {bondNumberExpr} AS BondNumber,
                    {procuringEntityExpr} AS ProcuringEntity,
                    {bondAmountExpr} AS BondAmount,
                    {appFeeExpr} AS ApplicationFee,
                    {commissionExpr} AS CommissionAmount,
                    {bankChargeExpr} AS BankCharge,
                    {clientChargeExpr} AS ClientCharge,
                    {paidAmountExpr} AS PaidAmount,
                    {createdAtExpr} AS CreatedAt,
                    ISNULL(pt.ProductName, '') AS ProductType,
                    ISNULL(cc.CashCoverAmount, 0) AS CashCoverAmount
                FROM Bonds b
                LEFT JOIN ProductTypes pt ON pt.Id = b.BondTypeId
                LEFT JOIN CashCovers cc ON cc.BondId = b.Id
                {whereClause}
                {orderByClause}";

            var bonds = (await conn.QueryAsync<dynamic>(bondSql, new { Id = clientId, From = dateFrom, To = dateTo, BondTypeIds = activeIds })).ToList();
            ViewBag.Bonds = bonds;

            var paymentsByBond = new Dictionary<int, List<dynamic>>();
            if (hasBondPayments && bonds.Any())
            {
                var bondIds = bonds.Select(b => (int)b.Id).Distinct().ToList();
                var bondPayments = (await conn.QueryAsync<dynamic>(@"
                    SELECT
                        BondId,
                        AmountPaid,
                        PaymentDate,
                        PaymentMethod,
                        PaymentReference,
                        Notes
                    FROM BondPayments
                    WHERE BondId IN @BondIds", new { BondIds = bondIds })).ToList();

                paymentsByBond = bondPayments
                    .GroupBy(p => (int)p.BondId)
                    .ToDictionary(g => g.Key, g => g.OrderBy(x => (DateTime)x.PaymentDate).ToList());
            }

            // 4. Inject bond-created transaction lines into the ledger so they appear inline
            foreach (var b in bonds.OrderBy(x => (DateTime)x.CreatedAt).ThenBy(x => (int)x.Id))
            {
                DateTime created = b.CreatedAt;
                string procuringEntity = b.ProcuringEntity != null ? (string)b.ProcuringEntity : string.Empty;
                decimal bondAmount = b.BondAmount != null ? (decimal)b.BondAmount : 0m;
                decimal clientCharge = b.ClientCharge != null ? (decimal)b.ClientCharge : 0m;
                decimal cashCoverAmount = b.CashCoverAmount != null ? (decimal)b.CashCoverAmount : 0m;
                int bondId = (int)b.Id;
                var bondRef = string.IsNullOrEmpty((string)b.BondNumber) ? ("#" + bondId) : (string)b.BondNumber;
                var productItem = string.IsNullOrWhiteSpace((string?)b.ProductType) ? "Bond Charge" : (string)b.ProductType;

                var bondRows = new List<StatementLine>();

                // Post the client charge as debit; credits are posted per payment installment.
                bondRows.Add(new StatementLine
                {
                    TransactionDate = created,
                    Description = $"Bond {bondRef} - Client Charge",
                    ProductItem = productItem,
                    ProcuringEntity = procuringEntity,
                    ReferenceNo = bondRef,
                    BondAmount = bondAmount,
                    CashCoverAmount = cashCoverAmount,
                    Debit = clientCharge,
                    Credit = 0,
                    ParentBondId = bondId
                });

                if (paymentsByBond.TryGetValue(bondId, out var paymentItems))
                {
                    foreach (var p in paymentItems)
                    {
                        var method = p.PaymentMethod?.ToString();
                        var reference = p.PaymentReference?.ToString();
                        var notes = p.Notes?.ToString();
                        var paymentDesc = $"Payment for Bond {bondRef}";

                        if (!string.IsNullOrWhiteSpace(method))
                            paymentDesc += $" [{method}]";
                        if (!string.IsNullOrWhiteSpace(reference))
                            paymentDesc += $" Ref: {reference}";
                        if (!string.IsNullOrWhiteSpace(notes))
                            paymentDesc += $" - {notes}";

                        bondRows.Add(new StatementLine
                        {
                            TransactionDate = (DateTime)p.PaymentDate,
                            Description = paymentDesc,
                            ProductItem = paymentDesc,
                            ProcuringEntity = string.Empty,
                            ReferenceNo = bondRef,
                            BondAmount = 0,
                            Debit = 0,
                            Credit = (decimal)p.AmountPaid,
                            IsPaymentLine = true,
                            ParentBondId = bondId
                        });
                    }
                }

                statementGroups.Add((created, bondRows));
            }

            var format = Request.Query["format"].ToString();

            // 5. Calculation Engine (recompute running balances including injected bond lines)
            decimal runningBalance = 0;
            decimal broughtForward = 0;
            var filteredLines = new List<StatementLine>();

            var endOfDayTo = dateTo.AddDays(1).AddTicks(-1);

            var orderedLines = new List<StatementLine>();
            var displayOrder = 0;
            foreach (var group in statementGroups.OrderBy(x => x.AnchorDate))
            {
                foreach (var row in group.Rows
                    .OrderBy(x => x.IsPaymentLine ? 1 : 0)
                    .ThenBy(x => x.TransactionDate)
                    .ThenBy(x => x.Description))
                {
                    row.DisplayOrder = ++displayOrder;
                    orderedLines.Add(row);
                }
            }

            foreach (var tx in orderedLines)
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

                sb.AppendLine("Date,ProductItem,ProcuringEntity,BondAmount,CashCover,RefNo,Debit,Credit,RunningBalance");

                foreach (var ln in filteredLines)
                {
                    string productItem = (ln.ProductItem ?? string.Empty).Replace("\"", "\"\"");
                    string procuringEntity = (string.IsNullOrWhiteSpace(ln.ProcuringEntity) ? ln.Description : ln.ProcuringEntity)?.Replace("\"", "\"\"") ?? string.Empty;
                    string refNo = (ln.ReferenceNo ?? string.Empty).Replace("\"", "\"\"");
                    sb.AppendLine($"{ln.TransactionDate:yyyy-MM-dd},\"{productItem}\",\"{procuringEntity}\",{ln.BondAmount:0.00},{ln.CashCoverAmount:0.00},\"{refNo}\",{ln.Debit:0.00},{ln.Credit:0.00},{ln.RunningBalance:0.00}");
                }

                sb.AppendLine();
                sb.AppendLine($",Brought Forward,,,,{broughtForward:0.00}");
                sb.AppendLine($",Closing Balance,,,,{runningBalance:0.00}");


                var bytes = System.Text.Encoding.UTF8.GetBytes(sb.ToString());
                var clientNameSafe = (client?.Name ?? "client").ToString().Replace(" ", "_").Replace(",", "");
                var fileName = $"{clientNameSafe}_Statement_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.csv";
                return File(bytes, "text/csv", fileName);
            }
                if (!string.IsNullOrWhiteSpace(format) && format.Equals("pdf", StringComparison.OrdinalIgnoreCase))
                {
                    var logoBytes = LoadBrandLogoBytes();
                    var pdfBytes = OnwardsSwift.API.Services.StatementPdfGenerator.Generate(
                        Convert.ToString(client?.Name) ?? string.Empty,
                        Convert.ToString(client?.ContactPerson) ?? string.Empty,
                        Convert.ToString(client?.Email) ?? string.Empty,
                        Convert.ToString(client?.PhoneNumber) ?? string.Empty,
                        Convert.ToString(client?.KraPin) ?? string.Empty,
                        Convert.ToString(client?.BusinessRegNumber) ?? string.Empty,
                        Convert.ToString(client?.PhysicalAddress) ?? string.Empty,
                        Convert.ToString(client?.PostalAddress) ?? string.Empty,
                        filteredLines,
                        dateFrom,
                        dateTo,
                        broughtForward,
                        runningBalance,
                        logoBytes);

                    var clientNameSafePdf = (client?.Name ?? "client").ToString().Replace(" ", "_").Replace(",", "");
                    var pdfFile = $"{clientNameSafePdf}_Statement_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";
                    return File(pdfBytes, "application/pdf", pdfFile);
                }

            ViewBag.Lines = filteredLines;
            return View(filteredLines);
        }

        private static bool IsBondStatementDescription(string? description)
        {
            if (string.IsNullOrWhiteSpace(description))
                return false;

            return description.Contains("Bond #", StringComparison.OrdinalIgnoreCase)
                || description.Contains("Payment Received - Bond", StringComparison.OrdinalIgnoreCase)
                || description.Contains("Payment for Bond", StringComparison.OrdinalIgnoreCase);
        }
        // GET: Finance/GenerateInvoicePdf
        public async Task<IActionResult> GenerateInvoicePdf(int clientId, DateTime? from, DateTime? to, int[]? bondTypeIds = null)
        {
            var dateFrom = from?.Date ?? DateTime.Today.AddMonths(-1);
            var dateTo = to?.Date ?? DateTime.Today;
            var endOfDayTo = dateTo.AddDays(1).AddTicks(-1);

            if (dateFrom > dateTo)
            {
                TempData["Error"] = "Invalid date range. 'From Date' must be earlier than or equal to 'To Date'.";
                return RedirectToAction(nameof(Statements), new { clientId, from = dateFrom.ToString("yyyy-MM-dd"), to = dateTo.ToString("yyyy-MM-dd") });
            }

            using var conn = _ctx.Create();

            var client = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id, CompanyName as Name, Email, Phone as PhoneNumber FROM Clients WHERE Id = @Id",
                new { Id = clientId });

            var selectedBondTypeIds = bondTypeIds?.Where(x => x > 0).ToList() ?? new List<int>();
            var lines = await GetInvoiceLinesAsync(conn, clientId, dateFrom, endOfDayTo, selectedBondTypeIds);
            var logoBytes = LoadBrandLogoBytes();

            var pdfBytes = OnwardsSwift.API.Services.InvoicePdfGenerator.Generate(
                Convert.ToString(client?.Name) ?? string.Empty,
                Convert.ToString(client?.ContactPerson) ?? string.Empty,
                Convert.ToString(client?.Email) ?? string.Empty,
                Convert.ToString(client?.PhoneNumber) ?? string.Empty,
                Convert.ToString(client?.KraPin) ?? string.Empty,
                Convert.ToString(client?.BusinessRegNumber) ?? string.Empty,
                Convert.ToString(client?.PhysicalAddress) ?? string.Empty,
                Convert.ToString(client?.PostalAddress) ?? string.Empty,
                dateFrom,
                dateTo,
                lines,
                logoBytes);

            var clientNameSafe = (client?.Name ?? "client").ToString().Replace(" ", "_").Replace(",", "");
            var fileName = $"{clientNameSafe}_Invoice_{dateFrom:yyyyMMdd}_{dateTo:yyyyMMdd}.pdf";

            // Prevent browser caching so the latest generated PDF is always downloaded
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            Response.Headers["Pragma"] = "no-cache";
            Response.Headers["Expires"] = "0";

            return File(pdfBytes, "application/pdf", fileName);
        }

        // GET: Finance/GenerateInvoice
        public async Task<IActionResult> GenerateInvoice(int clientId, DateTime? from, DateTime? to, int[]? bondTypeIds = null)
        {
            var dateFrom = from?.Date ?? DateTime.Today.AddMonths(-1);
            var dateTo = to?.Date ?? DateTime.Today;
            var endOfDayTo = dateTo.AddDays(1).AddTicks(-1);

            if (dateFrom > dateTo)
            {
                TempData["Error"] = "Invalid date range. 'From Date' must be earlier than or equal to 'To Date'.";
                return RedirectToAction(nameof(Statements), new { clientId, from = dateFrom.ToString("yyyy-MM-dd"), to = dateTo.ToString("yyyy-MM-dd") });
            }

            using var conn = _ctx.Create();

                        var client = await conn.QueryFirstOrDefaultAsync<dynamic>(
                                @"SELECT
                                        Id,
                                        CompanyName as Name,
                                        ISNULL(ContactPerson, '') as ContactPerson,
                                        ISNULL(Email, '') as Email,
                                        ISNULL(Phone, '') as PhoneNumber,
                                        ISNULL(KraPin, '') as KraPin,
                                        ISNULL(BusinessRegNumber, '') as BusinessRegNumber,
                                        ISNULL(PhysicalAddress, '') as PhysicalAddress,
                                        ISNULL(PostalAddress, '') as PostalAddress
                                    FROM Clients
                                    WHERE Id = @Id",
                                new { Id = clientId });

            var selectedBondTypeIds = bondTypeIds?.Where(x => x > 0).ToList() ?? new List<int>();
            var lines = await GetInvoiceLinesAsync(conn, clientId, dateFrom, endOfDayTo, selectedBondTypeIds);

            ViewBag.Client = client;
            ViewBag.ClientId = clientId;
            ViewBag.From = dateFrom;
            ViewBag.To = dateTo;
            ViewBag.SelectedBondTypeIds = selectedBondTypeIds;
            ViewBag.TotalCharged = lines.Sum(x => x.ChargedAmount);
            ViewBag.TotalPaid = lines.Sum(x => x.PaidAmount);
            ViewBag.Total = lines.Sum(x => x.Amount);

            return View(lines);
        }

        private byte[]? LoadBrandLogoBytes()
        {
            var logoPath = Path.Combine(_env.WebRootPath ?? string.Empty, "images", "onwards-logo.jpg");
            if (string.IsNullOrWhiteSpace(logoPath) || !System.IO.File.Exists(logoPath))
                return null;

            return System.IO.File.ReadAllBytes(logoPath);
        }

        private async Task<List<OnwardsSwift.API.Services.InvoicePdfLine>> GetInvoiceLinesAsync(
            IDbConnection conn,
            int clientId,
            DateTime dateFrom,
            DateTime dateTo,
            List<int> selectedBondTypeIds)
        {

            // Build invoice rows from bond transaction client charges.
            // In this schema, client charge is typically stored as CommissionAmount
            // (or ClientCharges in some environments).
            var bondColumns = (await conn.QueryAsync<string>(@"
                SELECT c.name
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                WHERE o.name = 'Bonds' AND o.type = 'U'"))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hasBondPayments = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = 'BondPayments'") > 0;

            var bondNumberExpr = bondColumns.Contains("BondNumber")
                ? "ISNULL(b.BondNumber, CAST(b.Id AS NVARCHAR(50)))"
                : "CAST(b.Id AS NVARCHAR(50))";
            var createdAtExpr = bondColumns.Contains("CreatedAt") ? "b.CreatedAt" : "GETDATE()";
            var processingDateExpr = bondColumns.Contains("ProcessingDate")
                ? $"ISNULL(b.ProcessingDate, {createdAtExpr})"
                : createdAtExpr;

            var chargeCandidates = new List<string>();
            if (bondColumns.Contains("ClientCharges"))
                chargeCandidates.Add("NULLIF(b.ClientCharges, 0)");
            if (bondColumns.Contains("CommissionAmount"))
                chargeCandidates.Add("NULLIF(b.CommissionAmount, 0)");
            if (bondColumns.Contains("ApplicationFee"))
                chargeCandidates.Add("NULLIF(b.ApplicationFee, 0)");

            string chargeExpr;
            string? chargeWhereExpr = null;
            if (chargeCandidates.Count > 0)
            {
                // Prefer first non-zero charge source in order.
                chargeExpr = $"ISNULL(COALESCE({string.Join(", ", chargeCandidates)}), 0)";
                chargeWhereExpr = chargeExpr;
            }
            else
            {
                chargeExpr = "0";
            }

            var paidExpr = hasBondPayments
                ? "ISNULL((SELECT SUM(bp.AmountPaid) FROM BondPayments bp WHERE bp.BondId = b.Id), 0)"
                : "0";
            var outstandingExpr = $"CASE WHEN ({chargeExpr} - {paidExpr}) < 0 THEN 0 ELSE ({chargeExpr} - {paidExpr}) END";

            var whereConditions = new List<string> { "b.ClientId = @Id" };
            whereConditions.Add($"{processingDateExpr} BETWEEN @From AND @To");
            if (bondColumns.Contains("isApproved"))
                whereConditions.Add("ISNULL(b.isApproved, 0) = 1");
            if (selectedBondTypeIds.Count > 0 && bondColumns.Contains("BondTypeId"))
                whereConditions.Add("b.BondTypeId IN @BondTypeIds");
            if (!string.IsNullOrWhiteSpace(chargeWhereExpr))
                whereConditions.Add($"{chargeWhereExpr} > 0");

            var joinProductTypes = bondColumns.Contains("BondTypeId")
                ? "LEFT JOIN ProductTypes pt ON pt.Id = b.BondTypeId"
                : string.Empty;
            var productItemExpr = bondColumns.Contains("BondTypeId") ? "ISNULL(pt.ProductName, '')" : "''";
            var procuringEntityExpr = bondColumns.Contains("ProcuringEntity")
                ? "NULLIF(LTRIM(RTRIM(b.ProcuringEntity)), '')"
                : "NULL";
            var bondAmountExpr = bondColumns.Contains("Amount")
                ? "ISNULL(b.Amount, 0)"
                : "0";
            var whereClause = "WHERE " + string.Join(" AND ", whereConditions);

            var invoiceSql = $@"
                SELECT
                    {bondNumberExpr} AS InvoiceNumber,
                    ISNULL({procuringEntityExpr}, 'N/A') AS Description,
                    {productItemExpr} AS ProductItem,
                    {bondAmountExpr} AS Quantity,
                    {chargeExpr} AS UnitPrice,
                    {outstandingExpr} AS Amount,
                    {chargeExpr} AS ChargedAmount,
                    {paidExpr} AS PaidAmount,
                    {outstandingExpr} AS OutstandingAmount,
                    {processingDateExpr} AS InvoiceDate,
                    {processingDateExpr} AS DueDate
                FROM Bonds b
                {joinProductTypes}
                {whereClause}
                ORDER BY {processingDateExpr} DESC";

            var lines = (await conn.QueryAsync<OnwardsSwift.API.Services.InvoicePdfLine>(
                invoiceSql,
                new { Id = clientId, From = dateFrom, To = dateTo, BondTypeIds = selectedBondTypeIds })).ToList();
            return lines;
        }
    }
}