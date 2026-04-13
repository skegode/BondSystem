using System.Threading.Tasks;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.API.Controllers
{
    public class ApprovalsController : Controller
    {
        private readonly IBidBondService _bidBondService;
        private readonly DapperContext _ctx;

        public ApprovalsController(IBidBondService bidBondService, DapperContext ctx)
        {
            _bidBondService = bidBondService;
            _ctx = ctx;
        }

  
        public async Task<IActionResult> Pending()
        {
            // Fetches bonds where isApproved is 0 or NULL
            var pendingBonds = await _bidBondService.GetPendingApprovalsAsync();
            return View(pendingBonds);
        }


        public async Task<IActionResult> Review(int id)
        {
            using var conn = _ctx.Create();

            var sql = @"
        SELECT 
            b.*,
            ISNULL(b.ApplicationFee, 0) + ISNULL(b.BankCharge, 0) AS PayableCharge,
            U.Fullname AS Agent,
            O.Name AS ProcuringEntityName,
            c.companyname AS ClientName, 
            bt.ProductName AS BondTypeName,
            cc.cashcoveramount AS CCAmount,
            cc.MaturityDate AS CCMaturitityDate,
            cc.Reference AS CCReference,
            cc.ReceiptPath AS CCReceiptPath,
            cc.CreatedAt AS CCDate
        FROM Bonds b
        INNER JOIN Clients c ON b.ClientId = c.Id
        INNER JOIN producttypes bt ON b.BondTypeId = bt.Id
        INNER JOIN Obligees O ON O.ID = B.ProcuringEntity
        LEFT JOIN SystemUsers U ON U.Id = B.AgentId
        LEFT JOIN CashCovers cc ON cc.BondId = b.Id
        WHERE b.id = @Id";

            var r = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { Id = id });

            if (r == null) return NotFound();

            var model = new BidBondResponse
            {
                Id = r.Id,
                ClientName = r.ClientName?.ToString(),
                BondTypeName = r.BondTypeName?.ToString(),
                TenderNumber = r.TenderNumber?.ToString(),
                TenderName = r.TenderName?.ToString(),
                ProcuringEntityName = r.ProcuringEntityName?.ToString(), // Name from Obligees table
                AgentName = r.Agent?.ToString() ?? "Direct Application", // Display Agent if exists
                Amount = (decimal)(r.Amount ?? 0),
                PayableCharge = (decimal)(r.PayableCharge ?? 0), // Hidden Commission/App fee breakdown
                TenorDays = (int)(r.TenorDays ?? 0),
                TenderClosingDate = r.TenderClosingDate,
                IsDeferredPayment = r.IsDeferredPayment ?? false,
                TenderDocPath = r.TenderDocPath?.ToString(),
                CR12Path = r.CR12Path?.ToString(),
                PaymentReceiptPath = r.PaymentReceiptPath?.ToString(),
                CreatedAt = r.CreatedAt,

                // Cash Cover Mapping
                HasCashCover = r.CCAmount != null,
                CashCoverAmount = (decimal?)r.CCAmount,
                CashCoverReceiptRef = r.CCReference?.ToString(),
                CashCoverReceiptPath = r.CCReceiptPath?.ToString(),
                CashCoverMaturityDate = r.CCMaturitityDate,
                CashCoverDate = r.CCDate
            };

            return View(model);
        }
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ProcessBond(ApproveBondRequest req)
        {
            var userId = HttpContext.Session.GetInt32("UserId") ?? 0;

            if (!req.IsApproved)
            {
                await _ctx.Create().ExecuteAsync("UPDATE Bonds SET isApproved = 2, StatusNotes = @Notes WHERE Id = @Id",
                    new { Notes = req.Remarks, Id = req.BondId });
                return RedirectToAction(nameof(Pending));
            }

            using var conn = _ctx.Create();
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
                // 1. Get Bond Details
                var bond = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT ClientId, ApplicationFee, BankCharge, IsDeferredPayment,PaymentReference FROM Bonds WHERE Id = @Id",
                    new { Id = req.BondId }, trans);

                // Explicit casting to handle dynamic types safely
                decimal appFee = (decimal)(bond.ApplicationFee ?? 0);
                decimal bankCharge = (decimal)(bond.BankCharge ?? 0);
                decimal payable = appFee + bankCharge;

                bool isDeferred = (bool)(bond.IsDeferredPayment ?? false);
                int isPaid = isDeferred ? 0 : 1;
                DateTime? dueDate = isDeferred ? DateTime.Now.AddDays(30) : (DateTime?)null;
                string paymentreference= (string)(bond.PaymentReference ?? "");

                // 2. Update Bond Status
                await conn.ExecuteAsync(@"
            UPDATE Bonds SET isApproved = 1, ApprovedBy = @By, ApprovedAt = GETDATE(), StatusNotes = @Notes 
            WHERE Id = @Id", new { By = userId, Notes = req.Remarks, Id = req.BondId }, trans);

                // 3. Create Invoice
                var invId = await conn.QuerySingleAsync<int>(@"
            INSERT INTO Invoices (ClientId, BondId, Amount, Status, DueDate, CreatedAt)
            VALUES (@ClientId, @BondId, @Amount, @Status, @Due, GETDATE());
            SELECT CAST(SCOPE_IDENTITY() as int);",
                    new
                    {
                        ClientId = bond.ClientId,
                        BondId = req.BondId,
                        Amount = payable,
                        Status = isPaid,
                        Due = dueDate
                    }, trans);

                // 4. Raise Revenue (Application Fee) to Ledger (Credit Revenue)
                await conn.ExecuteAsync(@"
            INSERT INTO GeneralLedger (TransactionType, ReferenceId, Amount, Description, EntryType, CreatedAt)
            VALUES ('REVENUE', @InvId, @Amount, 'Application Fee for Bond', 'CREDIT', GETDATE())",
                    new { InvId = invId, Amount = appFee }, trans);

                // 5. Raise Bank Charge (Debit Expense)
                await conn.ExecuteAsync(@"
            INSERT INTO GeneralLedger (TransactionType, ReferenceId, Amount, Description, EntryType, CreatedAt)
            VALUES ('BANK_CHARGE', @InvId, @Amount, 'Bank Processing Fee', 'DEBIT', GETDATE())",
                    new { InvId = invId, Amount = bankCharge }, trans);

                // 6. Record Initial Statement Entry (The Debit) - REMOVED 'Balance' column
                await conn.ExecuteAsync(@"
            INSERT INTO ClientStatements (ClientId, InvoiceId, Description, Debit, Credit, CreatedAt)
            VALUES (@ClientId, @InvId, 'Invoice for Bond Application', @Amount, 0, GETDATE())",
                    new { ClientId = bond.ClientId, InvId = invId, Amount = payable }, trans);

                // 7. If NOT deferred, record the Payment (Credit)
                if (!isDeferred)
                {

                    // Append the reference if it exists, otherwise just keep the base description
                    string description = string.IsNullOrWhiteSpace(paymentreference)
                        ? "Payment Received - Receipt Verified"
                        : $"Payment Received - Receipt Verified (Ref: {paymentreference})";

                    await conn.ExecuteAsync(@"
                INSERT INTO ClientStatements (ClientId, InvoiceId, Description, Debit, Credit, CreatedAt)
                VALUES (@ClientId, @InvId, @Description, 0, @Amount, GETDATE())",
                        new
                        {
                            ClientId = bond.ClientId,
                            InvId = invId,
                            Description = description,
                            Amount = payable
                        }, trans);

                    // Invoice is already created with status 1 if not deferred, 
                    // but this double-check ensures status integrity
                    await conn.ExecuteAsync("UPDATE Invoices SET Status = 1 WHERE Id = @Id",
                        new { Id = invId }, trans);
                }

                trans.Commit();
                TempData["Success"] = "Bond Approved and Financial Records Updated.";
            }
            catch (Exception ex)
            {
                trans.Rollback();
                TempData["Error"] = "Accounting Error: " + ex.Message;
            }

            return RedirectToAction(nameof(Pending));
        }

        /// <summary>
        /// Processes the manager's decision (Approve or Reject).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Process(ApproveBondRequest req)
        {
            // Retrieve current Admin/Manager ID from Session
            int? adminId = HttpContext.Session.GetInt32("UserId");

            if (adminId == null)
            {
                return RedirectToAction("Login", "Auth");
            }

            if (req.BondId <= 0)
            {
                TempData["Error"] = "Invalid Bond ID provided.";
                return RedirectToAction(nameof(Pending));
            }

            bool result = await _bidBondService.ApproveAsync(req, adminId.Value);

            if (result)
            {
                TempData["Success"] = req.IsApproved
                    ? "Bond application has been successfully approved."
                    : "Bond application has been rejected.";
            }
            else
            {
                TempData["Error"] = "A database error occurred while processing the approval.";
            }

            return RedirectToAction(nameof(Pending));
        }

        /// <summary>
        /// Default action redirects to the Pending list.
        /// </summary>
        public IActionResult Index()
        {
            return RedirectToAction(nameof(Pending));
        }
    }
}