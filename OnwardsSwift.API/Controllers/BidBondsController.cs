using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Enums;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;
using OnwardsSwift.Infrastructure.Services;
using System.Text;


namespace OnwardsSwift.API.Controllers
{
    public class BidBondsController : AppController
    {
        private readonly IBidBondService _bonds;
        private readonly IClientService  _clients;
        private readonly DapperContext   _ctx;
        private readonly IConfiguration  Configuration;
        private readonly WorkflowService _workflow;
        private readonly IWebHostEnvironment _env;

        public BidBondsController(IBidBondService bonds, IClientService clients, DapperContext ctx,
                                   IConfiguration _configuration, WorkflowService workflow, IWebHostEnvironment env)
        {
            _bonds         = bonds;
            _clients       = clients;
            _ctx           = ctx;
            Configuration  = _configuration;
            _workflow      = workflow;
            _env           = env;
        }

        private SqlConnection GetConnection() => new SqlConnection(Configuration.GetConnectionString("DefaultConnection"));



        public async Task<IActionResult> Index()
    {
        using var connection = new SqlConnection(Configuration.GetConnectionString("DefaultConnection"));

        var sql = @"
        SELECT 
            b.Id AS BondId, 
            b.TenderNumber, 
            b.TenderName,
            b.Amount, 
            b.ProcessingDate,
            b.isApproved AS Status, 
            b.CreatedAt, 
            N.BankName AS IssuingBank,
            ISNULL(c.CompanyName, 'N/A') AS ClientName
        FROM Bonds b
        INNER JOIN Clients c ON c.Id = b.ClientId
        INNER JOIN Banks N ON N.Id = b.IssuingBank
        ORDER BY b.CreatedAt DESC";

        // No <ModelName> here - Dapper returns dynamic objects
        var result = await connection.QueryAsync(sql);

        return View(result);
    }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await PopulateFormLookups();

            var model = new CreateBidBondRequest
            {
                TenderClosingDate = DateTime.Today.AddDays(30),
                ProcessingDate    = DateTime.Today,
                TenorDays = 90,
                ApplicationStatus = "New Application"
            };

            return View(model);
        }

  

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateBidBondRequest model, IFormFile? TenderDoc, IFormFile? CR12, IFormFile? PaymentReceipt, IFormFile? CashCoverReceipt)
        {
            try
            {
                int? sessionUserId = HttpContext.Session.GetInt32("UserId");
                if (sessionUserId == null) return RedirectToAction("Login", "Account");

                // 1. Check only core DTO fields
                if (!ModelState.IsValid)
                {
                    await PopulateFormLookups();
                    return View(model);
                }

                // 2. Process Core Files
                if (TenderDoc != null) model.TenderDocPath = await SaveFileAsync(TenderDoc, "Tenders");
                if (CR12 != null) model.CR12Path = await SaveFileAsync(CR12, "LegalDocs");

                // 3. Process Optional Payment Files
                if (PaymentReceipt != null) model.PaymentReceiptPath = await SaveFileAsync(PaymentReceipt, "Receipts");
                if (CashCoverReceipt != null) model.CashCoverReceiptPath = await SaveFileAsync(CashCoverReceipt, "CashCoverDocs");

            
            
                // 4. Save to Database
                var bondId = await _bonds.CreateAsync(model, sessionUserId.Value);

                // 5. Start approval workflow
                await _workflow.StartWorkflowAsync(bondId, "BOND", sessionUserId.Value);

                Success($"Application for {model.TenderNumber} submitted successfully.");
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"System Error: {ex.Message}");
                await PopulateFormLookups();
                return View(model);
            }
        }

        private async Task<string?> SaveFileAsync(IFormFile? file, string prefix)
        {
            if (file == null || file.Length == 0) return null;

            var uploadsFolder = GetUploadsFolder("bonds");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{prefix}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return $"/uploads/bonds/{fileName}"; // Relative path for DB
        }

        private string GetUploadsFolder(string category)
        {
            var uploadsRootSetting = Configuration["FileStorage:UploadsRoot"] ?? Path.Combine("wwwroot", "uploads");
            var uploadsRootPath = Path.IsPathRooted(uploadsRootSetting)
                ? uploadsRootSetting
                : Path.Combine(_env.ContentRootPath, uploadsRootSetting);

            return Path.Combine(uploadsRootPath, category);
        }

        public async Task<IActionResult> Details(int id)
        {
            var bond = await _bonds.GetByIdAsync(id);
            if (bond == null) return NotFound();
            return View(bond);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var bond = await GetEditableBondAsync(id);
            if (bond == null) return NotFound();

            var paymentSummary = await GetBondPaymentSummaryAsync(id, bond.ClientCharges);
            ViewBag.TotalCharged = paymentSummary.TotalCharged;
            ViewBag.TotalPaid = paymentSummary.TotalPaid;
            ViewBag.Outstanding = paymentSummary.Outstanding;
            ViewBag.CurrentIsPaid = !bond.IsDeferredPayment;
            bond.IsPaid = !bond.IsDeferredPayment;
            bond.AmountPaid = null;
            bond.PaymentNotes = paymentSummary.LastPaymentNotes;

            await PopulateFormLookups();
            return View(bond);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CreateBidBondRequest model, IFormFile? TenderDoc, IFormFile? CR12, IFormFile? PaymentReceipt, IFormFile? CashCoverReceipt)
        {
            try
            {
                const decimal fixedTaxPercentage = 20m;
                var existing = await GetEditableBondAsync(id);
                if (existing == null) return NotFound();

                var requestedIsPaid = model.IsPaid;
                var requestedAmountPaid = model.AmountPaid;
                var requestedPaymentMethod = model.PaymentMethod;
                var requestedPaymentNotes = model.PaymentNotes;

                model = MergeEditableBond(existing, model);
                model.IsPaid = requestedIsPaid;
                model.AmountPaid = requestedAmountPaid;
                model.PaymentMethod = requestedPaymentMethod;
                model.PaymentNotes = requestedPaymentNotes;
                model.IsDeferredPayment = !model.IsPaid;

                if (TenderDoc != null) model.TenderDocPath = await SaveFileAsync(TenderDoc, "Tenders");
                if (CR12 != null) model.CR12Path = await SaveFileAsync(CR12, "LegalDocs");
                if (PaymentReceipt != null) model.PaymentReceiptPath = await SaveFileAsync(PaymentReceipt, "Receipts");
                if (CashCoverReceipt != null) model.CashCoverReceiptPath = await SaveFileAsync(CashCoverReceipt, "CashCoverDocs");

                using var conn = _ctx.Create();
                conn.Open();
                using var trans = conn.BeginTransaction();

                var hasBondPayments = await conn.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1)
                    FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_NAME = 'BondPayments'", transaction: trans) > 0;

                var hasPaymentMethodColumn = await conn.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1)
                    FROM sys.columns c
                    INNER JOIN sys.objects o ON c.object_id = o.object_id
                    WHERE o.name = 'Bonds' AND o.type = 'U' AND c.name = 'PaymentMethod'", transaction: trans) > 0;

                var hasApplicationStatusColumn = await conn.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1)
                    FROM sys.columns c
                    INNER JOIN sys.objects o ON c.object_id = o.object_id
                    WHERE o.name = 'Bonds' AND o.type = 'U' AND c.name = 'ApplicationStatus'", transaction: trans) > 0;

                var hasAmendmentFeeColumn = await conn.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1)
                    FROM sys.columns c
                    INNER JOIN sys.objects o ON c.object_id = o.object_id
                    WHERE o.name = 'Bonds' AND o.type = 'U' AND c.name = 'AmendmentFee'", transaction: trans) > 0;

                model.PaymentReference = string.IsNullOrWhiteSpace(model.PaymentReference)
                    ? null
                    : model.PaymentReference.Trim();
                model.PaymentMethod = string.IsNullOrWhiteSpace(model.PaymentMethod)
                    ? null
                    : model.PaymentMethod.Trim().ToUpperInvariant();
                model.PaymentNotes = string.IsNullOrWhiteSpace(model.PaymentNotes)
                    ? null
                    : model.PaymentNotes.Trim();
                model.ApplicationStatus = string.IsNullOrWhiteSpace(model.ApplicationStatus)
                    ? "New Application"
                    : model.ApplicationStatus.Trim();
                model.AmendmentFee = Math.Max(model.AmendmentFee, 0);

                if (string.Equals(model.ApplicationStatus, "Amendment", StringComparison.OrdinalIgnoreCase))
                {
                    model.ClientCharges = model.AmendmentFee;
                }

                var allowedMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    "MPESA", "BANK", "CHEQUE"
                };

                if (model.PaymentMethod != null && !allowedMethods.Contains(model.PaymentMethod))
                    throw new InvalidOperationException("Invalid payment method selected.");

                decimal totalCharged = Math.Max(model.ClientCharges, 0);
                decimal paidToDate = 0;
                if (hasBondPayments)
                {
                    paidToDate = await conn.ExecuteScalarAsync<decimal>(@"
                        SELECT ISNULL(SUM(AmountPaid), 0)
                        FROM BondPayments
                        WHERE BondId = @BondId",
                        new { BondId = id }, trans);
                }

                decimal outstandingBefore = Math.Max(totalCharged - paidToDate, 0);
                decimal requestedAmount = Math.Max(model.AmountPaid ?? 0, 0);
                decimal captureAmount = 0;

                if (model.IsPaid)
                {
                    captureAmount = outstandingBefore > 0
                        ? (requestedAmount > 0 ? Math.Min(requestedAmount, outstandingBefore) : outstandingBefore)
                        : 0;
                }
                else if (requestedAmount > 0)
                {
                    captureAmount = Math.Min(requestedAmount, outstandingBefore);
                }

                if (captureAmount > 0 && !hasBondPayments)
                {
                    // Attempt to create the BondPayments table on-the-fly so partial payments can be captured.
                    // This mirrors AddBondPaymentsTable.sql but runs safely only when the table is missing.
                    await conn.ExecuteAsync(@"
                        IF NOT EXISTS (SELECT 1 FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_NAME = 'BondPayments')
                        BEGIN
                            CREATE TABLE BondPayments
                            (
                                Id INT IDENTITY(1,1) PRIMARY KEY,
                                BondId INT NOT NULL,
                                ClientId INT NOT NULL,
                                AmountPaid DECIMAL(18,2) NOT NULL,
                                PaymentMethod NVARCHAR(20) NULL,
                                PaymentReference NVARCHAR(100) NULL,
                                Notes NVARCHAR(300) NULL,
                                PaymentDate DATETIME NOT NULL CONSTRAINT DF_BondPayments_PaymentDate DEFAULT(GETDATE()),
                                CreatedAt DATETIME NOT NULL CONSTRAINT DF_BondPayments_CreatedAt DEFAULT(GETDATE()),
                                CreatedBy NVARCHAR(50) NULL
                            );
                            CREATE INDEX IX_BondPayments_BondId ON BondPayments(BondId);
                            CREATE INDEX IX_BondPayments_ClientId_PaymentDate ON BondPayments(ClientId, PaymentDate);
                        END", transaction: trans);

                    // Mark as available for subsequent inserts
                    hasBondPayments = true;
                }

                var applicationStatusSet = hasApplicationStatusColumn
                    ? "ApplicationStatus    = @ApplicationStatus,"
                    : string.Empty;
                var amendmentFeeSet = hasAmendmentFeeColumn
                    ? "AmendmentFee         = @AmendmentFee,"
                    : string.Empty;

                var updateBondSql = hasPaymentMethodColumn
                    ? @"
                    UPDATE Bonds SET
                        ClientId            = @ClientId,
                        AgentId             = @AgentId,
                        BondTypeId          = @BondTypeId,
                        " + applicationStatusSet + @"
                        " + amendmentFeeSet + @"
                        TenderName          = @TenderName,
                        TenderNumber        = @TenderNumber,
                        ProcuringEntity     = @ProcuringEntity,
                        IssuingBank         = @IssuingBank,
                        Amount              = @Amount,
                        TenorDays           = @TenorDays,
                        TenderClosingDate   = @TenderClosingDate,
                        TenderDocPath       = @TenderDocPath,
                        CR12Path            = @CR12Path,
                        BankRate            = @AppliedRate,
                        ApplicationFee      = @NetProfit,
                        CommissionAmount    = @ClientCharges,
                        BankCharge          = @BankCharges,
                        TaxPercentage       = @TaxPercentage,
                        TaxCalculation      = @TaxCalculation,
                        TotalBankCharge     = @TotalBankCharge,
                        IsDeferredPayment   = @IsDeferredPayment,
                        PaymentBankId       = @PaymentBankId,
                        PaymentReference    = @PaymentReference,
                        PaymentMethod       = @PaymentMethod,
                        PaymentReceiptPath  = @PaymentReceiptPath,
                        Notes               = @Notes,
                        DisbursementAccount = @DisbursementAccount,
                        DisbursementBank    = @DisbursementBank,
                        ProcessingDate      = @ProcessingDate
                    WHERE Id = @Id"
                    : @"
                    UPDATE Bonds SET
                        ClientId            = @ClientId,
                        AgentId             = @AgentId,
                        BondTypeId          = @BondTypeId,
                        " + applicationStatusSet + @"
                        " + amendmentFeeSet + @"
                        TenderName          = @TenderName,
                        TenderNumber        = @TenderNumber,
                        ProcuringEntity     = @ProcuringEntity,
                        IssuingBank         = @IssuingBank,
                        Amount              = @Amount,
                        TenorDays           = @TenorDays,
                        TenderClosingDate   = @TenderClosingDate,
                        TenderDocPath       = @TenderDocPath,
                        CR12Path            = @CR12Path,
                        BankRate            = @AppliedRate,
                        ApplicationFee      = @NetProfit,
                        CommissionAmount    = @ClientCharges,
                        BankCharge          = @BankCharges,
                        TaxPercentage       = @TaxPercentage,
                        TaxCalculation      = @TaxCalculation,
                        TotalBankCharge     = @TotalBankCharge,
                        IsDeferredPayment   = @IsDeferredPayment,
                        PaymentBankId       = @PaymentBankId,
                        PaymentReference    = @PaymentReference,
                        PaymentReceiptPath  = @PaymentReceiptPath,
                        Notes               = @Notes,
                        DisbursementAccount = @DisbursementAccount,
                        DisbursementBank    = @DisbursementBank,
                        ProcessingDate      = @ProcessingDate
                    WHERE Id = @Id";

                await conn.ExecuteAsync(updateBondSql,
                    new {
                        NetProfit = model.ClientCharges - (model.BankCharges + Math.Round(model.BankCharges * (fixedTaxPercentage / 100m), 2)),
                        model.ClientId,
                        AgentId = model.AgentId == 0 ? (int?)null : model.AgentId,
                        model.BondTypeId,
                        model.ApplicationStatus,
                        model.AmendmentFee,
                        model.TenderName,
                        model.TenderNumber,
                        model.ProcuringEntity,
                        model.IssuingBank,
                        model.Amount,
                        model.TenorDays,
                        model.TenderClosingDate,
                        model.TenderDocPath,
                        model.CR12Path,
                        model.AppliedRate,
                        ClientCharges = model.ClientCharges,
                        BankCharges = model.BankCharges,
                        TaxPercentage = fixedTaxPercentage,
                        TaxCalculation = Math.Round(model.BankCharges * (fixedTaxPercentage / 100m), 2),
                        TotalBankCharge = model.BankCharges + Math.Round(model.BankCharges * (fixedTaxPercentage / 100m), 2),
                        model.IsDeferredPayment,
                        model.PaymentBankId,
                        model.PaymentReference,
                        model.PaymentMethod,
                        model.PaymentReceiptPath,
                        model.Notes,
                        model.DisbursementAccount,
                        model.DisbursementBank,
                        model.ProcessingDate,
                        Id = id
                    }, trans);

                if (captureAmount > 0)
                {
                    int currentUserId = HttpContext.Session.GetInt32("UserId") ?? 0;
                    await conn.ExecuteAsync(@"
                        INSERT INTO BondPayments
                            (BondId, ClientId, AmountPaid, PaymentMethod, PaymentReference, Notes, PaymentDate, CreatedAt, CreatedBy)
                        VALUES
                            (@BondId, @ClientId, @AmountPaid, @PaymentMethod, @PaymentReference, @Notes, GETDATE(), GETDATE(), @CreatedBy)",
                        new
                        {
                            BondId = id,
                            model.ClientId,
                            AmountPaid = captureAmount,
                            model.PaymentMethod,
                            model.PaymentReference,
                            Notes = model.PaymentNotes,
                            CreatedBy = currentUserId.ToString()
                        }, trans);
                }

                var paidAfter = paidToDate + captureAmount;
                var outstandingAfter = Math.Max(totalCharged - paidAfter, 0);
                var isDeferredAfter = !model.IsPaid || outstandingAfter > 0;

                var updateDeferredSql = hasPaymentMethodColumn
                    ? @"
                        UPDATE Bonds
                        SET IsDeferredPayment = @IsDeferred,
                            PaymentReference = @PaymentReference,
                            PaymentMethod = @PaymentMethod
                        WHERE Id = @BondId"
                    : @"
                        UPDATE Bonds
                        SET IsDeferredPayment = @IsDeferred,
                            PaymentReference = @PaymentReference
                        WHERE Id = @BondId";

                await conn.ExecuteAsync(updateDeferredSql,
                    new
                    {
                        IsDeferred = isDeferredAfter,
                        model.PaymentReference,
                        model.PaymentMethod,
                        BondId = id
                    }, trans);

                if (model.HasCashCover && model.CashCoverAmount > 0)
                {
                    var existingCashCoverId = await conn.QueryFirstOrDefaultAsync<int?>(
                        "SELECT TOP 1 BondId FROM CashCovers WHERE BondId = @Id", new { Id = id }, trans);

                    if (existingCashCoverId.HasValue)
                    {
                        await conn.ExecuteAsync(@"
                            UPDATE CashCovers SET
                                CashCoverAmount = @CashCoverAmount,
                                MaturityDate    = @CashCoverDueDate,
                                BankId          = @CashCoverBankId,
                                Reference       = @CashCoverReference,
                                ReceiptPath     = @CashCoverReceiptPath
                            WHERE BondId = @Id",
                            new {
                                model.CashCoverAmount,
                                CashCoverDueDate = model.CashCoverDueDate ?? DateTime.Now.AddDays(model.TenorDays),
                                model.CashCoverBankId,
                                model.CashCoverReference,
                                model.CashCoverReceiptPath,
                                Id = id
                            }, trans);
                    }
                    else
                    {
                        await conn.ExecuteAsync(@"
                            INSERT INTO CashCovers (BondId, CashCoverAmount, MaturityDate, Status, CreatedAt, CreatedBy, BankId, Reference, ReceiptPath)
                            VALUES (@Id, @CashCoverAmount, @CashCoverDueDate, 'Active', GETDATE(), @CreatedBy, @CashCoverBankId, @CashCoverReference, @CashCoverReceiptPath)",
                            new {
                                Id = id,
                                model.CashCoverAmount,
                                CashCoverDueDate = model.CashCoverDueDate ?? DateTime.Now.AddDays(model.TenorDays),
                                CreatedBy = HttpContext.Session.GetInt32("UserId")?.ToString() ?? "system",
                                model.CashCoverBankId,
                                model.CashCoverReference,
                                model.CashCoverReceiptPath
                            }, trans);
                    }
                }
                else
                {
                    await conn.ExecuteAsync("DELETE FROM CashCovers WHERE BondId = @Id", new { Id = id }, trans);
                }

                trans.Commit();
                Success("Bond application updated successfully.");
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"Update failed: {ex.Message}");
                var paymentSummary = await GetBondPaymentSummaryAsync(id, model.ClientCharges);
                ViewBag.TotalCharged = paymentSummary.TotalCharged;
                ViewBag.TotalPaid = paymentSummary.TotalPaid;
                ViewBag.Outstanding = paymentSummary.Outstanding;
                ViewBag.CurrentIsPaid = model.IsPaid;
                await PopulateFormLookups();
                return View(model);
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            try
            {
                using var conn = _ctx.Create();
                await conn.ExecuteAsync("DELETE FROM Bonds WHERE Id = @Id", new { Id = id });
                Success("Bond application deleted.");
            }
            catch (Exception ex)
            {
                Error($"Delete failed: {ex.Message}");
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id, string? notes)
        {
            Success("Facility approved.");
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Disburse(int id)
        {
            Success("Facility disbursed.");
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string reason)
        {
            Success("Facility rejected.");
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ConvertToPerformance(int id)
        {
            await _bonds.ConvertToPerformanceBondAsync(id, CurrentUserEmail);
            Success("Converted to performance bond.");
            return RedirectToAction(nameof(Details), new { id });
        }
        // 1. New Method to handle the Quote Preview
        [HttpGet]
        public async Task<IActionResult> GenerateQuote(CreateBidBondRequest req)
        {
            // Logic to generate PDF/View for the quote
            // For now, it returns a view or a partial with the request data
            return View("QuotePreview", req);
        }
        [HttpGet]
        public async Task<JsonResult> GetLiveRate(string bankId, int productType)
        {
            if (HttpContext.Session.GetInt32("UserId") == null)
                return Json(new { error = "unauthorized" });

            using var conn = _ctx.Create();
            var rateData = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT Rate, [Commission] as comm, ApplicationFee, ApplicationFeeType, CommissionType
                FROM ProductRates 
                WHERE BankPartnerId = @BankId AND ProductType = @ProdType AND IsActive = 1",
                new { BankId = bankId, ProdType = productType });

            if (rateData == null) return Json(new { rate = 0, comm = 0, appFee = 0, appFeeType = 1 });

            return Json(new
            {
                rate = rateData.Rate*100,
                comm = rateData.comm,
                commType = rateData.CommissionType,
                appFee = rateData.ApplicationFee,
                appFeeType = rateData.ApplicationFeeType
            });
        }

        private async Task PopulateFormLookups()
        {
            using var conn = _ctx.Create();

            var clients = await conn.QueryAsync<dynamic>("SELECT Id, CompanyName FROM Clients WHERE IsDeleted = 0");
            var banks = await conn.QueryAsync<dynamic>("SELECT Id, BankName as Name FROM Banks WHERE IsActive = 1");
            var agents = await conn.QueryAsync<dynamic>("SELECT Id, FullName FROM SystemUsers WHERE IsActive = 1 AND IsDeleted = 0");
            var productTypes = await conn.QueryAsync<dynamic>(@"SELECT Id, ProductName AS Name FROM ProductTypes ORDER BY ProductName");
            var internalBanks = await conn.QueryAsync<dynamic>(@"SELECT Id, BankName + ' (' + AccountNumber + ')' as BankText FROM InternalBanks");

            ViewBag.Clients = new SelectList(clients, "Id", "CompanyName");
            ViewBag.Banks = new SelectList(banks, "Id", "Name");
            ViewBag.Agents = new SelectList(agents, "Id", "FullName");
            ViewBag.ProductTypes = new SelectList(productTypes, "Id", "Name");
            ViewBag.InternalBanks = new SelectList(internalBanks, "Id", "BankText");
        }

        private async Task<CreateBidBondRequest?> GetEditableBondAsync(int id)
        {
            using var conn = _ctx.Create();
            var hasPaymentMethodColumn = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                WHERE o.name = 'Bonds' AND o.type = 'U' AND c.name = 'PaymentMethod'") > 0;

            var hasApplicationStatusColumn = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                WHERE o.name = 'Bonds' AND o.type = 'U' AND c.name = 'ApplicationStatus'") > 0;

            var hasAmendmentFeeColumn = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                WHERE o.name = 'Bonds' AND o.type = 'U' AND c.name = 'AmendmentFee'") > 0;

            var paymentMethodSelect = hasPaymentMethodColumn
                ? "b.PaymentMethod,"
                : "CAST(NULL AS NVARCHAR(50)) AS PaymentMethod,";
            var applicationStatusSelect = hasApplicationStatusColumn
                ? "ISNULL(b.ApplicationStatus, 'New Application') AS ApplicationStatus,"
                : "'New Application' AS ApplicationStatus,";
            var amendmentFeeSelect = hasAmendmentFeeColumn
                ? "ISNULL(b.AmendmentFee, 0) AS AmendmentFee,"
                : "0 AS AmendmentFee,";

            var sql = new StringBuilder(@"
                SELECT 
                    b.ClientId,
                    b.AgentId,
                    b.BondTypeId,
                    b.ProcuringEntity,
                    b.TenderName,
                    b.TenderNumber,
                    b.IssuingBank,
                    b.Amount,
                    b.TenderClosingDate,
                    b.TenorDays,
                    CAST(CASE WHEN cc.BondId IS NULL THEN 0 ELSE 1 END AS bit) AS HasCashCover,
                    cc.CashCoverAmount,
                    cc.BankId AS CashCoverBankId,
                    cc.Reference AS CashCoverReference,
                    cc.ReceiptPath AS CashCoverReceiptPath,
                    cc.MaturityDate AS CashCoverDueDate,
                    b.IsDeferredPayment,
                    b.PaymentBankId,
                    b.PaymentReference,
                    ");
            sql.Append(paymentMethodSelect);
                sql.Append(applicationStatusSelect);
                sql.Append(amendmentFeeSelect);
            sql.Append(@"
                    b.PaymentReceiptPath,
                    b.TenderDocPath,
                    b.CR12Path,
                    b.BankRate AS AppliedRate,
                    b.ApplicationFee AS NetProfit,
                    b.CommissionAmount AS ClientCharges,
                    b.BankCharge AS BankCharges,
                    ISNULL(b.TaxPercentage, 0) AS TaxPercentage,
                    ISNULL(b.TaxCalculation, 0) AS TaxCalculation,
                    ISNULL(b.TotalBankCharge, ISNULL(b.BankCharge, 0)) AS TotalBankCharge,
                    b.Notes,
                    b.DisbursementAccount,
                    b.DisbursementBank,
                    b.ProcessingDate
                FROM Bonds b
                LEFT JOIN CashCovers cc ON cc.BondId = b.Id
                WHERE b.Id = @Id");

            return await conn.QueryFirstOrDefaultAsync<CreateBidBondRequest>(sql.ToString(), new { Id = id });
        }

        private async Task<(decimal TotalCharged, decimal TotalPaid, decimal Outstanding, string? LastPaymentNotes)> GetBondPaymentSummaryAsync(int bondId, decimal totalCharged)
        {
            using var conn = _ctx.Create();
            var hasBondPayments = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = 'BondPayments'") > 0;

            decimal totalPaid = 0;
            string? lastPaymentNotes = null;

            if (hasBondPayments)
            {
                totalPaid = await conn.ExecuteScalarAsync<decimal>(@"
                    SELECT ISNULL(SUM(AmountPaid), 0)
                    FROM BondPayments
                    WHERE BondId = @BondId", new { BondId = bondId });

                lastPaymentNotes = await conn.QueryFirstOrDefaultAsync<string>(@"
                    SELECT TOP 1 Notes
                    FROM BondPayments
                    WHERE BondId = @BondId AND NULLIF(LTRIM(RTRIM(ISNULL(Notes, ''))), '') IS NOT NULL
                    ORDER BY PaymentDate DESC, CreatedAt DESC", new { BondId = bondId });
            }

            var charged = Math.Max(totalCharged, 0);
            var outstanding = Math.Max(charged - totalPaid, 0);
            return (charged, totalPaid, outstanding, lastPaymentNotes);
        }

        private static CreateBidBondRequest MergeEditableBond(CreateBidBondRequest existing, CreateBidBondRequest incoming)
        {
            return new CreateBidBondRequest
            {
                ClientId = incoming.ClientId == 0 ? existing.ClientId : incoming.ClientId,
                AgentId = incoming.AgentId == 0 ? existing.AgentId : incoming.AgentId,
                BondTypeId = incoming.BondTypeId == 0 ? existing.BondTypeId : incoming.BondTypeId,
                ProcuringEntity = string.IsNullOrWhiteSpace(incoming.ProcuringEntity) ? existing.ProcuringEntity : incoming.ProcuringEntity,
                TenderName = string.IsNullOrWhiteSpace(incoming.TenderName) ? existing.TenderName : incoming.TenderName,
                TenderNumber = string.IsNullOrWhiteSpace(incoming.TenderNumber) ? existing.TenderNumber : incoming.TenderNumber,
                IssuingBank = incoming.IssuingBank == 0 ? existing.IssuingBank : incoming.IssuingBank,
                Amount = incoming.Amount == 0 ? existing.Amount : incoming.Amount,
                TenderClosingDate = incoming.TenderClosingDate == default ? existing.TenderClosingDate : incoming.TenderClosingDate,
                TenorDays = incoming.TenorDays == 0 ? existing.TenorDays : incoming.TenorDays,
                HasCashCover = incoming.HasCashCover,
                CashCoverAmount = incoming.CashCoverAmount ?? existing.CashCoverAmount,
                CashCoverBankId = incoming.CashCoverBankId ?? existing.CashCoverBankId,
                CashCoverReference = string.IsNullOrWhiteSpace(incoming.CashCoverReference) ? existing.CashCoverReference : incoming.CashCoverReference,
                CashCoverReceiptPath = string.IsNullOrWhiteSpace(incoming.CashCoverReceiptPath) ? existing.CashCoverReceiptPath : incoming.CashCoverReceiptPath,
                CashCoverDueDate = incoming.CashCoverDueDate ?? existing.CashCoverDueDate,
                IsDeferredPayment = incoming.IsDeferredPayment,
                IsPaid = incoming.IsPaid,
                AmountPaid = incoming.AmountPaid,
                PaymentBankId = incoming.PaymentBankId ?? existing.PaymentBankId,
                PaymentReference = string.IsNullOrWhiteSpace(incoming.PaymentReference) ? existing.PaymentReference : incoming.PaymentReference,
                PaymentMethod = string.IsNullOrWhiteSpace(incoming.PaymentMethod) ? existing.PaymentMethod : incoming.PaymentMethod,
                PaymentNotes = string.IsNullOrWhiteSpace(incoming.PaymentNotes) ? existing.PaymentNotes : incoming.PaymentNotes,
                PaymentReceiptPath = string.IsNullOrWhiteSpace(incoming.PaymentReceiptPath) ? existing.PaymentReceiptPath : incoming.PaymentReceiptPath,
                TenderDocPath = string.IsNullOrWhiteSpace(incoming.TenderDocPath) ? existing.TenderDocPath : incoming.TenderDocPath,
                CR12Path = string.IsNullOrWhiteSpace(incoming.CR12Path) ? existing.CR12Path : incoming.CR12Path,
                CompanyProfilePath = string.IsNullOrWhiteSpace(incoming.CompanyProfilePath) ? existing.CompanyProfilePath : incoming.CompanyProfilePath,
                IndemnityDocPath = string.IsNullOrWhiteSpace(incoming.IndemnityDocPath) ? existing.IndemnityDocPath : incoming.IndemnityDocPath,
                AppliedRate = incoming.AppliedRate == 0 ? existing.AppliedRate : incoming.AppliedRate,
                ApplicationFee = incoming.ApplicationFee == 0 ? existing.ApplicationFee : incoming.ApplicationFee,
                CommissionAmount = incoming.CommissionAmount == 0 ? existing.CommissionAmount : incoming.CommissionAmount,
                ClientCharges = incoming.ClientCharges == 0 ? existing.ClientCharges : incoming.ClientCharges,
                ApplicationStatus = string.IsNullOrWhiteSpace(incoming.ApplicationStatus) ? existing.ApplicationStatus : incoming.ApplicationStatus,
                AmendmentFee = incoming.AmendmentFee,
                BankCharges = incoming.BankCharges == 0 ? existing.BankCharges : incoming.BankCharges,
                TaxPercentage = incoming.TaxPercentage == 0 ? existing.TaxPercentage : incoming.TaxPercentage,
                TaxCalculation = incoming.TaxCalculation == 0 ? existing.TaxCalculation : incoming.TaxCalculation,
                TotalBankCharge = incoming.TotalBankCharge == 0 ? existing.TotalBankCharge : incoming.TotalBankCharge,
                NetProfit = incoming.NetProfit == 0 ? existing.NetProfit : incoming.NetProfit,
                ClientName = string.IsNullOrWhiteSpace(incoming.ClientName) ? existing.ClientName : incoming.ClientName,
                Notes = string.IsNullOrWhiteSpace(incoming.Notes) ? existing.Notes : incoming.Notes,
                DisbursementAccount = string.IsNullOrWhiteSpace(incoming.DisbursementAccount) ? existing.DisbursementAccount : incoming.DisbursementAccount,
                DisbursementBank = string.IsNullOrWhiteSpace(incoming.DisbursementBank) ? existing.DisbursementBank : incoming.DisbursementBank,
                ProcessingDate = incoming.ProcessingDate ?? existing.ProcessingDate
            };
        }
    }
}

 

