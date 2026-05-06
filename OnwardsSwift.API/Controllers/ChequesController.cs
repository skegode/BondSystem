using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Enums;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;
using OnwardsSwift.Infrastructure.Services;
using Dapper;

namespace OnwardsSwift.API.Controllers
{
    public class ChequesController : AppController
    {
        private readonly IChequeDiscountService _chq;
        private readonly IClientService         _clients;
        private readonly DapperContext          _ctx;
        private readonly WorkflowService        _workflow;

        public ChequesController(IChequeDiscountService chq, IClientService clients,
                                  DapperContext ctx, WorkflowService workflow)
        {
            _chq      = chq;
            _clients  = clients;
            _ctx      = ctx;
            _workflow = workflow;
        }

        public async Task<IActionResult> Index()
        {
            using var conn = _ctx.Create();

            // Using your specific SQL with the CompanyName alias
            const string sql = @"
        SELECT 
            cd.Id,
            cd.ClientId,
            cd.ChequeNumber,
            cd.DrawerBank,
            cd.ChequeAmount,
            cd.DiscountRate,
            (cd.ChequeAmount - (cd.ChequeAmount * cd.DiscountRate / 100)) AS AdvanceAmount,
            cd.ExpiryDate,
            cd.Status,
            cd.CreatedAt,
            c.CompanyName AS ClientName 
        FROM ChequeDiscounting cd
        INNER JOIN Clients c ON cd.ClientId = c.Id
        ORDER BY cd.CreatedAt DESC";

            var cheques = await conn.QueryAsync<dynamic>(sql);

            return View(cheques);
        }
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            using var conn = _ctx.Create();
            ViewBag.Clients = await conn.QueryAsync<dynamic>("SELECT Id, CompanyName FROM Clients ORDER BY CompanyName");

            // Standard list for Kenya
            ViewBag.Banks = new List<string> { "KCB Bank", "Equity Bank", "Co-operative Bank", "ABSA Kenya", "Stanbic Bank", "NCBA Bank", "I&M Bank", "DTB" };

            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(
            int clientId,
            string chequeNumber,
            string drawerBank,
            string drawerAccountNo,
            decimal chequeAmount,
            decimal discountRate,
            DateTime expiryDate,
            IFormFile? chequePhoto, // Match the helper input
            string paymentMethod,
            string phone,
            string bankName,
            string accountNo,
            string branchName)
        {
            using var conn = _ctx.Create();
            conn.Open();
            using var trans = conn.BeginTransaction();

            int newChequeId = 0;
            try
            {
                // 1. Save file using your established helper pattern
                string? dbPhotoPath = await SaveFileAsync(chequePhoto, $"CHQ_{chequeNumber}");

                // 2. Math
                decimal discountValue = chequeAmount * (discountRate / 100);
                decimal netAdvance = chequeAmount - discountValue;

                // 3. Insert Cheque Record
                const string chequeSql = @"
            INSERT INTO ChequeDiscounting (
                ClientId, ChequeNumber, DrawerBank, DrawerAccountNo,
                ChequeAmount, DiscountRate, ExpiryDate, PhotoPath,
                Status, CreatedAt, CreatedBy
            )
            VALUES (
                @clientId, @chequeNumber, @drawerBank, @drawerAccountNo,
                @chequeAmount, @discountRate, @expiryDate, @dbPhotoPath,
                'Awaiting Approval', GETDATE(), @User
            );
            SELECT CAST(SCOPE_IDENTITY() as int);";

                newChequeId = await conn.ExecuteScalarAsync<int>(chequeSql, new
                {
                    clientId,
                    chequeNumber,
                    drawerBank,
                    drawerAccountNo,
                    chequeAmount,
                    discountRate,
                    expiryDate,
                    dbPhotoPath,
                    User = User.Identity?.Name ?? "System"
                }, trans);

                // 4. Queue Disbursement
                const string queueSql = @"
            INSERT INTO PaymentQueue (
                ReferenceId, ReferenceType, Amount, PaymentMethod, 
                BankName, BankAccount, BankBranch, 
                CreatedAt, Status, Narration
            )
            VALUES (
                @newChequeId, 'CHEQUE_DISCOUNT', @netAdvance, @paymentMethod,
                @bankName, @account, @branch,
                GETDATE(), 'Awaiting Authorization', @notes
            )";

                await conn.ExecuteAsync(queueSql, new
                {
                    newChequeId,
                    netAdvance,
                    paymentMethod,
                    bankName,
                    account = (paymentMethod == "MPESA" ? phone : accountNo),
                    branch = branchName,
                    notes = $"Cheque Discount Advance: #{chequeNumber}"
                }, trans);

                trans.Commit();
            }
            catch (Exception ex)
            {
                trans.Rollback();
                TempData["Error"] = "Submission Error: " + ex.Message;
                return RedirectToAction(nameof(Create));
            }

            // Start approval workflow AFTER the transaction is fully committed
            int sessionUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
            await _workflow.StartWorkflowAsync(newChequeId, "CHEQUE", sessionUserId);

            TempData["Success"] = "Application submitted and disbursement queued.";
            return RedirectToAction(nameof(Index));
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Authorize(int id, int queueId, decimal bankFee)
        {
            using var conn = _ctx.Create();
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
                // 1. Fetch Cheque & Client details for the Ledger description
                const string sqlData = @"
            SELECT cd.*, c.CompanyName 
            FROM ChequeDiscounting cd
            JOIN Clients c ON cd.ClientId = c.Id
            WHERE cd.Id = @id";

                var cheque = await conn.QueryFirstOrDefaultAsync<dynamic>(sqlData, new { id }, trans);

                if (cheque == null) return NotFound();

                // 2. Math for the Ledger
                decimal revenueAmount = cheque.ChequeAmount * (cheque.DiscountRate / 100);
                decimal netPayout = cheque.ChequeAmount - revenueAmount;
                int? approvedBy = HttpContext.Session.GetInt32("UserId");


                // --- GENERAL LEDGER ENTRIES ---

                // A. Record REVENUE (Credit)
                await conn.ExecuteAsync(@"
            INSERT INTO GeneralLedger (TransactionType, ReferenceId, Amount, Description, EntryType, CreatedAt)
            VALUES ('REVENUE', @id, @revenueAmount, @desc, 'CREDIT', GETDATE())",
                    new { id, revenueAmount, desc = $"Discounting Revenue: Cheque #{cheque.ChequeNumber} ({cheque.CompanyName})" }, trans);

                // B. Record BANK_DEPOSIT / Payout (Debit)
                await conn.ExecuteAsync(@"
            INSERT INTO GeneralLedger (TransactionType, ReferenceId, Amount, Description, EntryType, CreatedAt)
            VALUES ('BANK_DEPOSIT', @id, @netPayout, @desc, 'DEBIT', GETDATE())",
                    new { id, netPayout, desc = $"Disbursement: Advance for Cheque #{cheque.ChequeNumber}" }, trans);

                // C. Record BANK_CHARGE (Debit)
                await conn.ExecuteAsync(@"
            INSERT INTO GeneralLedger (TransactionType, ReferenceId, Amount, Description, EntryType, CreatedAt)
            VALUES ('BANK_CHARGE', @id, @bankFee, @desc, 'DEBIT', GETDATE())",
                    new { id, bankFee, desc = $"Transfer Fee: Payout for Cheque #{cheque.ChequeNumber}" }, trans);


                // --- STATUS UPDATES ---

                // 3. Mark Cheque as Active
                await conn.ExecuteAsync(@"
            UPDATE ChequeDiscounting 
            SET Status = 'Active', 
                ApprovedBy = @approvedBy, 
                ApprovedAt = GETDATE() 
            WHERE Id = @id",
                    new { id, approvedBy }, trans);

                // 4. Mark Payment Queue as Completed
                await conn.ExecuteAsync(@"
            UPDATE PaymentQueue 
            SET Status = 'Completed', 
                AuthorizedAt = GETDATE() 
            WHERE Id = @queueId",
                    new { queueId }, trans);

                trans.Commit();
                TempData["Success"] = $"Cheque #{cheque.ChequeNumber} has been authorized and GL entries posted.";
                return RedirectToAction("Approvals");
            }
            catch (Exception ex)
            {
                trans.Rollback();
                TempData["Error"] = "Authorization Failed: " + ex.Message;
                return RedirectToAction("Verify", new { id });
            }
        }
        public async Task<IActionResult> Approvals()
        {
            using var conn = _ctx.Create();

            const string sql = @"
        SELECT 
            cd.Id,
            c.CompanyName,
            cd.ChequeNumber,
            cd.DrawerBank,
            cd.ChequeAmount,
            (cd.ChequeAmount - (cd.ChequeAmount * cd.DiscountRate / 100)) AS NetAdvance,
            pq.PaymentMethod,
            cd.CreatedAt
        FROM ChequeDiscounting cd
        INNER JOIN Clients c ON cd.ClientId = c.Id
        INNER JOIN PaymentQueue pq ON pq.ReferenceId = cd.Id AND pq.ReferenceType = 'CHEQUE_DISCOUNT'
        WHERE cd.Status = 'Awaiting Approval'
        ORDER BY cd.CreatedAt ASC";

            var pending = await conn.QueryAsync<dynamic>(sql);
            return View(pending);
        }
        public async Task<IActionResult> Verify(int id)
        {
            using var conn = _ctx.Create();
            // Fetch combined data from Cheque and PaymentQueue
            const string sql = @"
        SELECT cd.*, c.CompanyName, pq.Id as QueueId, pq.PaymentMethod,
               (cd.ChequeAmount - (cd.ChequeAmount * cd.DiscountRate / 100)) AS AdvanceAmount
        FROM ChequeDiscounting cd
        JOIN Clients c ON cd.ClientId = c.Id
        JOIN PaymentQueue pq ON pq.ReferenceId = cd.Id AND pq.ReferenceType = 'CHEQUE_DISCOUNT'
        WHERE cd.Id = @id";

            var data = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { id });
            return View(data);
        }

        // GET: Cheques/Portfolio
        public async Task<IActionResult> Portfolio()
        {
            using var conn = _ctx.Create();
            // Fetch Active cheques and calculate days to maturity
            const string sql = @"
        SELECT cd.*, c.CompanyName, 
               DATEDIFF(day, GETDATE(), cd.ExpiryDate) as DaysToMaturity
        FROM ChequeDiscounting cd
        JOIN Clients c ON cd.ClientId = c.Id
        WHERE cd.Status = 'Active' 
        ORDER BY cd.ExpiryDate ASC";

            var data = await conn.QueryAsync<dynamic>(sql);
            return View(data);
        }

        // POST: Cheques/CloseCheque
        [HttpPost]
        public async Task<IActionResult> CloseCheque(int id)
        {
            using var conn = _ctx.Create();
            // Logic: Mark as Closed and record the final inflow in GL
            await conn.ExecuteAsync(@"
        UPDATE ChequeDiscounting SET Status = 'Closed', ClosedAt = GETDATE() WHERE Id = @id;
        
        INSERT INTO GeneralLedger (TransactionType, ReferenceId, Amount, Description, EntryType)
        SELECT 'CHEQUE_CLEARANCE', Id, ChequeAmount, 
               'Funds received for Cheque #' + ChequeNumber, 'CREDIT'
        FROM ChequeDiscounting WHERE Id = @id", new { id });

            TempData["Success"] = "Cheque marked as Closed. Funds confirmed.";
            return RedirectToAction(nameof(Portfolio));
        }

        public async Task<IActionResult> Details(int id)
        {
            var chq = await _chq.GetByIdAsync(id);
            if (chq == null) return NotFound();
            return View(chq);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(Guid id, string? notes)
        {
            Success("Approved."); return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Disburse(Guid id)
        {
            Success("Disbursed."); return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> PresentToBank(int id)
        {
            await _chq.PresentToBankAsync(id, CurrentUserEmail);
            Success("Cheque marked as presented to bank.");
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RecordOutcome(Guid id, bool honoured, string? bounceReason)
        {
            await _chq.RecordOutcomeAsync(new RecordChequeOutcomeRequest
            { FacilityId = id, Honoured = honoured, BounceReason = bounceReason }, CurrentUserEmail);
            Success(honoured ? "Cheque honoured." : "Cheque bounced — recorded.");
            return RedirectToAction(nameof(Details), new { id });
        }

        private async Task PopulateDropdowns()
        {
            var clients = await _clients.GetAllAsync(1, 200, null);
            ViewBag.Clients = new SelectList(clients.Items, "Id", "CompanyName");
            ViewBag.Banks = "";//select * from banks
        }
        private async Task<string?> SaveFileAsync(IFormFile? file, string prefix)
        {
            if (file == null || file.Length == 0) return null;

            // Consistency: saving to cheques folder
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "cheques");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{prefix}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return $"/uploads/cheques/{fileName}"; // Relative path for DB
        }
    }
}
