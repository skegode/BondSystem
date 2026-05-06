using Dapper;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;
using OnwardsSwift.Infrastructure.Services;

namespace OnwardsSwift.API.Controllers
{
    public class ApprovalsController : AppController
    {
        private readonly IBidBondService  _bidBondService;
        private readonly WorkflowService  _workflow;
        private readonly DapperContext    _ctx;

        public ApprovalsController(IBidBondService bidBondService, WorkflowService workflow, DapperContext ctx)
        {
            _bidBondService = bidBondService;
            _workflow       = workflow;
            _ctx            = ctx;
        }

        // ── Queue ──────────────────────────────────────────────

        public async Task<IActionResult> Index() => RedirectToAction(nameof(Pending));

        public async Task<IActionResult> Pending()
        {
            int    userId  = HttpContext.Session.GetInt32("UserId") ?? 0;
            string role    = HttpContext.Session.GetString("UserRole") ?? "";
            bool   isAdmin = role == "Admin";

            List<ApprovalQueueItem> bondItems = isAdmin
                ? await _workflow.GetAllPendingAsync("BOND")
                : await _workflow.GetPendingForUserAsync(userId, "BOND");

            List<ApprovalQueueItem> chequeItems = isAdmin
                ? await _workflow.GetAllPendingAsync("CHEQUE")
                : await _workflow.GetPendingForUserAsync(userId, "CHEQUE");

            var all = bondItems.Concat(chequeItems)
                               .OrderBy(x => x.SubmittedAt)
                               .ToList();

            return View(all);
        }

        // ── Review ─────────────────────────────────────────────

        public async Task<IActionResult> Review(int id)
        {
            using var conn = _ctx.Create();

            var sql = @"
                SELECT b.*,
                    ISNULL(b.ApplicationFee, 0) + ISNULL(b.BankCharge, 0) AS PayableCharge,
                    U.Fullname AS Agent,
                    b.ProcuringEntity AS ProcuringEntityName,
                    c.CompanyName AS ClientName,
                    bt.ProductName AS BondTypeName,
                    cc.CashCoverAmount AS CCAmount,
                    cc.MaturityDate    AS CCMaturitityDate,
                    cc.Reference       AS CCReference,
                    cc.ReceiptPath     AS CCReceiptPath,
                    cc.CreatedAt       AS CCDate
                FROM Bonds b
                INNER JOIN Clients c      ON b.ClientId = c.Id
                INNER JOIN ProductTypes bt ON b.BondTypeId = bt.Id
                LEFT JOIN SystemUsers U   ON U.Id = B.AgentId
                LEFT JOIN CashCovers cc   ON cc.BondId = b.Id
                WHERE b.Id = @Id";

            var r = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { Id = id });
            if (r == null) return NotFound();

            var bond = new BidBondResponse
            {
                Id                 = r.Id,
                ClientName         = r.ClientName?.ToString(),
                BondTypeName       = r.BondTypeName?.ToString(),
                TenderNumber       = r.TenderNumber?.ToString(),
                TenderName         = r.TenderName?.ToString(),
                ProcuringEntityName= r.ProcuringEntityName?.ToString(),
                AgentName          = r.Agent?.ToString() ?? "Direct Application",
                Amount             = (decimal)(r.Amount ?? 0),
                PayableCharge      = (decimal)(r.PayableCharge ?? 0),
                TenorDays          = (int)(r.TenorDays ?? 0),
                TenderClosingDate  = r.TenderClosingDate,
                IsDeferredPayment  = r.IsDeferredPayment ?? false,
                TenderDocPath      = r.TenderDocPath?.ToString(),
                CR12Path           = r.CR12Path?.ToString(),
                PaymentReceiptPath = r.PaymentReceiptPath?.ToString(),
                CreatedAt          = r.CreatedAt,
                HasCashCover       = r.CCAmount != null,
                CashCoverAmount         = (decimal?)r.CCAmount,
                CashCoverReceiptRef     = r.CCReference?.ToString(),
                CashCoverReceiptPath    = r.CCReceiptPath?.ToString(),
                CashCoverMaturityDate   = r.CCMaturitityDate,
                CashCoverDate           = r.CCDate
            };

            int  userId  = HttpContext.Session.GetInt32("UserId") ?? 0;
            string role  = HttpContext.Session.GetString("UserRole") ?? "";

            var workflow     = await _workflow.GetInstanceAsync(id, "BOND");
            bool isApprover  = role == "Admin" || await _workflow.IsUserApproverForCurrentStageAsync(id, "BOND", userId);
            var allStages    = await _workflow.GetStagesByModuleAsync("BOND");

            var vm = new BondReviewViewModel
            {
                Bond              = bond,
                Workflow          = workflow,
                IsCurrentApprover = isApprover,
                AllStages         = allStages
            };

            return View(vm);
        }

        // ── Cheque review ──────────────────────────────────────

        public async Task<IActionResult> ReviewCheque(int id)
        {
            using var conn = _ctx.Create();

            var r = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT cd.*,
                       c.CompanyName AS ClientName,
                       (cd.ChequeAmount * cd.DiscountRate / 100)                         AS DiscountAmount,
                       (cd.ChequeAmount - (cd.ChequeAmount * cd.DiscountRate / 100))     AS NetAdvance,
                       pq.PaymentMethod, pq.BankName AS DisburseBank,
                       pq.BankAccount  AS DisburseAccount, pq.BankBranch AS DisburseBranch
                FROM ChequeDiscounting cd
                INNER JOIN Clients      c  ON c.Id  = cd.ClientId
                LEFT  JOIN PaymentQueue pq ON pq.ReferenceId = cd.Id
                                          AND pq.ReferenceType = 'CHEQUE_DISCOUNT'
                WHERE cd.Id = @Id", new { Id = id });

            if (r == null) return NotFound();

            int    userId = HttpContext.Session.GetInt32("UserId") ?? 0;
            string role   = HttpContext.Session.GetString("UserRole") ?? "";

            var workflow    = await _workflow.GetInstanceAsync(id, "CHEQUE");
            bool isApprover = role == "Admin" || await _workflow.IsUserApproverForCurrentStageAsync(id, "CHEQUE", userId);
            var allStages   = await _workflow.GetStagesByModuleAsync("CHEQUE");

            ViewBag.Cheque      = r;
            ViewBag.Workflow    = workflow;
            ViewBag.IsApprover  = isApprover;
            ViewBag.AllStages   = allStages;

            return View();
        }

        // ── Workflow action (approve / reject / return) ────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Act(WorkflowActionRequest req)
        {
            int    userId   = HttpContext.Session.GetInt32("UserId") ?? 0;
            string userName = HttpContext.Session.GetString("UserName") ?? "Unknown";

            WorkflowActionResult result;
            try
            {
                result = await _workflow.ProcessActionAsync(req.InstanceId, userId, userName, req.ActionType, req.Comment);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error processing action: " + ex.Message;
                if (req.ChequeId > 0)
                    return RedirectToAction(nameof(ReviewCheque), new { id = req.ChequeId });
                return RedirectToAction(nameof(Review), new { id = req.BondId });
            }

            if (!result.Success)
            {
                TempData["Error"] = result.Message;
                if (req.ChequeId > 0)
                    return RedirectToAction(nameof(ReviewCheque), new { id = req.ChequeId });
                return RedirectToAction(nameof(Review), new { id = req.BondId });
            }

            // Final approval of a bond — trigger financial posting
            if (result.IsComplete && result.ModuleType == "BOND" && req.BondId > 0)
                return await FinalApproveBond(req.BondId, req.Comment, userId);

            // Final approval of a cheque — trigger GL posting + payment authorization
            if (result.IsComplete && result.ModuleType == "CHEQUE" && req.ChequeId > 0)
                return await FinalApproveCheque(req.ChequeId, req.Comment, userId);

            if (result.IsComplete)
                TempData["Success"] = "Approved. Workflow complete.";
            else if (result.IsRejected)
                TempData["Error"] = "Application rejected.";
            else
                TempData["Success"] = $"Moved to: {result.NextStageName}.";

            // Return to the correct review page if not complete/rejected
            if (!result.IsComplete && !result.IsRejected)
            {
                if (req.ChequeId > 0)
                    return RedirectToAction(nameof(ReviewCheque), new { id = req.ChequeId });
                if (req.BondId > 0)
                    return RedirectToAction(nameof(Review), new { id = req.BondId });
            }

            return RedirectToAction(nameof(Pending));
        }

        // ── Document upload (applicant or approver) ────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UploadDocument(int bondId, string documentName, IFormFile file)
        {
            if (file == null || file.Length == 0)
            {
                TempData["Error"] = "Please select a file.";
                return RedirectToAction(nameof(Review), new { id = bondId });
            }

            int    userId   = HttpContext.Session.GetInt32("UserId") ?? 0;
            string userName = HttpContext.Session.GetString("UserName") ?? "Unknown";

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "approval-docs");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            var fileName = $"BOND_{bondId}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);
            using (var stream = new FileStream(filePath, FileMode.Create))
                await file.CopyToAsync(stream);

            await _workflow.RecordUploadedDocumentAsync(
                bondId, "BOND", documentName, $"/uploads/approval-docs/{fileName}",
                userId, userName);

            TempData["Success"] = $"'{documentName}' uploaded successfully.";
            return RedirectToAction(nameof(Review), new { id = bondId });
        }

        // ── Final financial posting (called internally on final approval) ─

        private async Task<IActionResult> FinalApproveBond(int bondId, string? remarks, int userId)
        {
            using var conn = _ctx.Create();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                var bond = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT B.ClientId, B.ApplicationFee, B.BankCharge, B.IsDeferredPayment,
                           B.PaymentReference, B.Amount,
                           B.ProcuringEntity,
                           C.CompanyName AS ClientName
                    FROM Bonds B
                    INNER JOIN Clients  C ON C.Id = B.ClientId
                    WHERE B.Id = @Id", new { Id = bondId }, trans);

                decimal appFee      = (decimal)(bond.ApplicationFee ?? 0);
                decimal bankCharge  = (decimal)(bond.BankCharge ?? 0);
                decimal payable     = appFee + bankCharge;
                string  entity      = (string)(bond.ProcuringEntity ?? "Unknown Entity");
                decimal bondValue   = (decimal)(bond.Amount ?? 0);
                string  clientName  = (string)(bond.ClientName ?? "Unknown Client");
                bool    isDeferred  = (bool)(bond.IsDeferredPayment ?? false);
                int     isPaid      = isDeferred ? 0 : 1;
                DateTime? dueDate   = isDeferred ? DateTime.Now.AddDays(30) : (DateTime?)null;
                string  payRef      = (string)(bond.PaymentReference ?? "");

                string baseDesc         = $"Bond #{bondId} for {entity} of amount {bondValue:N2}";
                string ledgerRevDesc    = $"Revenue: Application Fee | Bond #{bondId} | {clientName} | {entity}";
                string ledgerBankDesc   = $"Expense: Bank Processing Fee | Bond #{bondId} | {clientName}";

                await conn.ExecuteAsync(@"
                    UPDATE Bonds SET isApproved=1, ApprovedBy=@By, ApprovedAt=GETDATE(), StatusNotes=@Notes
                    WHERE Id=@Id",
                    new { By = userId, Notes = remarks, Id = bondId }, trans);

                var invId = await conn.QuerySingleAsync<int>(@"
                    INSERT INTO Invoices (ClientId, BondId, Amount, Status, DueDate, CreatedAt)
                    VALUES (@ClientId, @BondId, @Amount, @Status, @Due, GETDATE());
                    SELECT CAST(SCOPE_IDENTITY() AS INT);",
                    new { ClientId = bond.ClientId, BondId = bondId, Amount = payable, Status = isPaid, Due = dueDate },
                    trans);

                await conn.ExecuteAsync(@"
                    INSERT INTO GeneralLedger (TransactionType, ReferenceId, Amount, Description, EntryType, CreatedAt)
                    VALUES ('REVENUE', @InvId, @Amount, @Desc, 'CREDIT', GETDATE())",
                    new { InvId = invId, Desc = ledgerRevDesc, Amount = appFee }, trans);

                await conn.ExecuteAsync(@"
                    INSERT INTO GeneralLedger (TransactionType, ReferenceId, Amount, Description, EntryType, CreatedAt)
                    VALUES ('BANK_CHARGE', @InvId, @Amount, @Desc, 'DEBIT', GETDATE())",
                    new { InvId = invId, Desc = ledgerBankDesc, Amount = bankCharge }, trans);

                await conn.ExecuteAsync(@"
                    INSERT INTO ClientStatements (ClientId, InvoiceId, Description, Debit, Credit, CreatedAt)
                    VALUES (@ClientId, @InvId, @Desc, @Amount, 0, GETDATE())",
                    new { ClientId = bond.ClientId, Desc = baseDesc, InvId = invId, Amount = payable }, trans);

                if (!isDeferred)
                {
                    string payDesc = string.IsNullOrWhiteSpace(payRef)
                        ? $"Payment Received - Bond #{bondId}"
                        : $"Payment Received - Bond #{bondId} (Ref: {payRef})";

                    await conn.ExecuteAsync(@"
                        INSERT INTO ClientStatements (ClientId, InvoiceId, Description, Debit, Credit, CreatedAt)
                        VALUES (@ClientId, @InvId, @Description, 0, @Amount, GETDATE())",
                        new { ClientId = bond.ClientId, InvId = invId, Description = payDesc, Amount = payable },
                        trans);

                    await conn.ExecuteAsync("UPDATE Invoices SET Status=1 WHERE Id=@Id", new { Id = invId }, trans);
                }

                trans.Commit();
                TempData["Success"] = "Bond approved and financial records updated.";
            }
            catch (Exception ex)
            {
                trans.Rollback();
                TempData["Error"] = "Accounting error: " + ex.Message;
            }

            return RedirectToAction(nameof(Pending));
        }

        // ── Final financial posting for cheque ───────────────

        private async Task<IActionResult> FinalApproveCheque(int chequeId, string? remarks, int userId)
        {
            using var conn = _ctx.Create();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                var cheque = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT cd.ClientId, cd.ChequeNumber, cd.ChequeAmount, cd.DiscountRate,
                           c.CompanyName
                    FROM ChequeDiscounting cd
                    INNER JOIN Clients c ON c.Id = cd.ClientId
                    WHERE cd.Id = @Id", new { Id = chequeId }, trans);

                if (cheque == null) throw new Exception("Cheque record not found.");

                decimal chequeAmount = (decimal)(cheque.ChequeAmount ?? 0);
                decimal discountRate = (decimal)(cheque.DiscountRate  ?? 0);
                decimal revenueAmt   = chequeAmount * (discountRate / 100);
                decimal netPayout    = chequeAmount - revenueAmt;
                string  chequeNum    = cheque.ChequeNumber?.ToString() ?? chequeId.ToString();
                string  company      = cheque.CompanyName?.ToString()  ?? "Unknown";

                // Mark cheque Active
                await conn.ExecuteAsync(@"
                    UPDATE ChequeDiscounting
                    SET Status='Active', ApprovedBy=@By, ApprovedAt=GETDATE(), StatusNotes=@Notes
                    WHERE Id=@Id",
                    new { By = userId, Notes = remarks, Id = chequeId }, trans);

                // GL – Revenue credit
                await conn.ExecuteAsync(@"
                    INSERT INTO GeneralLedger (TransactionType, ReferenceId, Amount, Description, EntryType, CreatedAt)
                    VALUES ('REVENUE', @Id, @Amt, @Desc, 'CREDIT', GETDATE())",
                    new { Id = chequeId, Amt = revenueAmt,
                          Desc = $"Discounting Revenue: Cheque #{chequeNum} ({company})" }, trans);

                // GL – Disbursement debit
                await conn.ExecuteAsync(@"
                    INSERT INTO GeneralLedger (TransactionType, ReferenceId, Amount, Description, EntryType, CreatedAt)
                    VALUES ('BANK_DEPOSIT', @Id, @Amt, @Desc, 'DEBIT', GETDATE())",
                    new { Id = chequeId, Amt = netPayout,
                          Desc = $"Disbursement: Advance for Cheque #{chequeNum}" }, trans);

                // Authorize payment queue entry
                await conn.ExecuteAsync(@"
                    UPDATE PaymentQueue
                    SET Status='Authorized', AuthorizedAt=GETDATE()
                    WHERE ReferenceId=@Id AND ReferenceType='CHEQUE_DISCOUNT'",
                    new { Id = chequeId }, trans);

                trans.Commit();
                TempData["Success"] = $"Cheque #{chequeNum} approved. GL entries posted and disbursement authorized.";
            }
            catch (Exception ex)
            {
                trans.Rollback();
                TempData["Error"] = "Accounting error: " + ex.Message;
            }

            return RedirectToAction(nameof(Pending));
        }
    }
}
