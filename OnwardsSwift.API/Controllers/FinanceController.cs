using System.Data;
using Dapper;
using Microsoft.AspNetCore.Connections;
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

            // Querying invoices and calculating the current balance by subtracting credits from ClientStatements
            const string sql = @"
                SELECT i.*, c.CompanyName as ClientName,
                (i.Amount - ISNULL((SELECT SUM(Credit) FROM ClientStatements WHERE InvoiceId = i.Id), 0)) as RemainingBalance
                FROM Invoices i 
                INNER JOIN Clients c ON i.ClientId = c.Id 
                ORDER BY i.Status ASC, i.CreatedAt DESC";

            var invoices = await conn.QueryAsync<dynamic>(sql);

            // Fetching internal banks for the dropdown
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
                // 1. Fetch Invoice Details
                var inv = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT ClientId, Amount, InvoiceNumber FROM Invoices WHERE Id = @Id",
                    new { Id = invoiceId }, trans);

                if (inv == null) return NotFound();

                // 2. Record the Payment in ClientStatements (The Credit)
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

                // 3. Record Bank Entry in General Ledger (The Debit)
                await conn.ExecuteAsync(@"
                    INSERT INTO GeneralLedger (TransactionType, ReferenceId, Amount, Description, EntryType, CreatedAt)
                    VALUES ('BANK_DEPOSIT', @InvId, @Amount, @Desc, 'DEBIT', GETDATE())",
                    new
                    {
                        InvId = invoiceId,
                        Amount = amountPaid,
                        Desc = $"Cash Receipt - Inv #{inv.InvoiceNumber}"
                    }, trans);

                // 4. Logic to check if the invoice is now fully settled
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

        public IActionResult Statements() => View();
        // GET: Finance/GenerateStatement
        public async Task<IActionResult> GenerateStatement(int clientId, DateTime from, DateTime to)
        {
            using var conn = _ctx.Create();

            // 1. Fetch Client Details (Keep dynamic here or use a Client DTO)
            var client = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT CompanyName as Name, Email, Phone as PhoneNumber FROM Clients WHERE Id = @Id",
                new { Id = clientId });

            // 2. Fetch History using the DTO
            const string sql = @"
        SELECT CreatedAt as TransactionDate, Description, Debit, Credit 
        FROM ClientStatements 
        WHERE ClientId = @Id 
        ORDER BY CreatedAt ASC";

            // Query as the specific DTO type
            var allHistory = await conn.QueryAsync<StatementLine>(sql, new { Id = clientId });

            // 3. Calculation Engine
            decimal runningBalance = 0;
            decimal broughtForward = 0;
            var filteredLines = new List<StatementLine>();

            // Adjust 'to' date to include the entire end day
            var endOfDayTo = to.Date.AddDays(1).AddTicks(-1);

            foreach (var tx in allHistory)
            {
                runningBalance += (tx.Debit - tx.Credit);

                if (tx.TransactionDate < from)
                {
                    broughtForward = runningBalance;
                }
                else if (tx.TransactionDate >= from && tx.TransactionDate <= endOfDayTo)
                {
                    tx.RunningBalance = runningBalance; // This will now work!
                    filteredLines.Add(tx);
                }
            }

            ViewBag.Client = client;
            ViewBag.From = from;
            ViewBag.To = to;
            ViewBag.Bof = broughtForward;
            ViewBag.TotalDue = runningBalance;

            return View(filteredLines);
        }
    }
}
