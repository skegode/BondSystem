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


namespace OnwardsSwift.API.Controllers
{
    public class BidBondsController : AppController
    {
        private readonly IBidBondService _bonds;
        private readonly IClientService  _clients;
        private readonly DapperContext   _ctx;
        private readonly IConfiguration  Configuration;
        private readonly WorkflowService _workflow;

        public BidBondsController(IBidBondService bonds, IClientService clients, DapperContext ctx,
                                   IConfiguration _configuration, WorkflowService workflow)
        {
            _bonds         = bonds;
            _clients       = clients;
            _ctx           = ctx;
            Configuration  = _configuration;
            _workflow      = workflow;
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
                TenorDays = 90
            };

            return View(model);
        }

  

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateBidBondRequest model, IFormFile TenderDoc, IFormFile CR12, IFormFile PaymentReceipt, IFormFile CashCoverReceipt)
        {
            try
            {
                int? sessionUserId = HttpContext.Session.GetInt32("UserId");
                if (sessionUserId == null) return RedirectToAction("Login", "Account");

                // 1. Conditional Validation: Only require payment if NOT deferred
                if (!model.IsDeferredPayment)
                {
                    if (model.PaymentBankId == null)
                        ModelState.AddModelError("PaymentBankId", "Collection bank is required for immediate payment.");
                    if (PaymentReceipt == null)
                        ModelState.AddModelError("PaymentReceipt", "Payment receipt is required.");
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

            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "uploads", "bonds");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{prefix}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return $"/uploads/bonds/{fileName}"; // Relative path for DB
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
            await PopulateFormLookups();
            return View(bond);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CreateBidBondRequest model, IFormFile? TenderDoc, IFormFile? CR12, IFormFile? PaymentReceipt, IFormFile? CashCoverReceipt)
        {
            try
            {
                var existing = await GetEditableBondAsync(id);
                if (existing == null) return NotFound();

                model = MergeEditableBond(existing, model);

                if (TenderDoc != null) model.TenderDocPath = await SaveFileAsync(TenderDoc, "Tenders");
                if (CR12 != null) model.CR12Path = await SaveFileAsync(CR12, "LegalDocs");
                if (PaymentReceipt != null) model.PaymentReceiptPath = await SaveFileAsync(PaymentReceipt, "Receipts");
                if (CashCoverReceipt != null) model.CashCoverReceiptPath = await SaveFileAsync(CashCoverReceipt, "CashCoverDocs");

                using var conn = _ctx.Create();
                conn.Open();
                using var trans = conn.BeginTransaction();

                await conn.ExecuteAsync(@"
                    UPDATE Bonds SET
                        ClientId            = @ClientId,
                        AgentId             = @AgentId,
                        BondTypeId          = @BondTypeId,
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
                        ApplicationFee      = @ApplicationFee,
                        CommissionAmount    = @ClientCharges,
                        BankCharge          = @BankCharges,
                        IsDeferredPayment   = @IsDeferredPayment,
                        PaymentBankId       = @PaymentBankId,
                        PaymentReference    = @PaymentReference,
                        PaymentReceiptPath  = @PaymentReceiptPath,
                        Notes               = @Notes,
                        DisbursementAccount = @DisbursementAccount,
                        DisbursementBank    = @DisbursementBank
                    WHERE Id = @Id",
                    new {
                        model.ClientId,
                        AgentId = model.AgentId == 0 ? (int?)null : model.AgentId,
                        model.BondTypeId,
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
                        model.ApplicationFee,
                        ClientCharges = model.ClientCharges,
                        BankCharges = model.BankCharges,
                        model.IsDeferredPayment,
                        model.PaymentBankId,
                        model.PaymentReference,
                        model.PaymentReceiptPath,
                        model.Notes,
                        model.DisbursementAccount,
                        model.DisbursementBank,
                        Id = id
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
            var productTypes = await conn.QueryAsync<dynamic>(@"SELECT Id, ProductName AS Name FROM ProductTypes WHERE ProductType = 1");
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
            return await conn.QueryFirstOrDefaultAsync<CreateBidBondRequest>(@"
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
                    b.PaymentReceiptPath,
                    b.TenderDocPath,
                    b.CR12Path,
                        b.BankRate AS AppliedRate,
                        b.ApplicationFee AS NetProfit,
                        b.CommissionAmount AS ClientCharges,
                        b.BankCharge AS BankCharges,
                    b.Notes,
                    b.DisbursementAccount,
                    b.DisbursementBank
                FROM Bonds b
                LEFT JOIN CashCovers cc ON cc.BondId = b.Id
                WHERE b.Id = @Id", new { Id = id });
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
                PaymentBankId = incoming.PaymentBankId ?? existing.PaymentBankId,
                PaymentReference = string.IsNullOrWhiteSpace(incoming.PaymentReference) ? existing.PaymentReference : incoming.PaymentReference,
                PaymentReceiptPath = string.IsNullOrWhiteSpace(incoming.PaymentReceiptPath) ? existing.PaymentReceiptPath : incoming.PaymentReceiptPath,
                TenderDocPath = string.IsNullOrWhiteSpace(incoming.TenderDocPath) ? existing.TenderDocPath : incoming.TenderDocPath,
                CR12Path = string.IsNullOrWhiteSpace(incoming.CR12Path) ? existing.CR12Path : incoming.CR12Path,
                CompanyProfilePath = string.IsNullOrWhiteSpace(incoming.CompanyProfilePath) ? existing.CompanyProfilePath : incoming.CompanyProfilePath,
                IndemnityDocPath = string.IsNullOrWhiteSpace(incoming.IndemnityDocPath) ? existing.IndemnityDocPath : incoming.IndemnityDocPath,
                AppliedRate = incoming.AppliedRate == 0 ? existing.AppliedRate : incoming.AppliedRate,
                ApplicationFee = incoming.ApplicationFee == 0 ? existing.ApplicationFee : incoming.ApplicationFee,
                	CommissionAmount = incoming.CommissionAmount == 0 ? existing.CommissionAmount : incoming.CommissionAmount,
                	ClientCharges = incoming.ClientCharges == 0 ? existing.ClientCharges : incoming.ClientCharges,
                	BankCharges = incoming.BankCharges == 0 ? existing.BankCharges : incoming.BankCharges,
                	NetProfit = incoming.NetProfit == 0 ? existing.NetProfit : incoming.NetProfit,
                ClientName = string.IsNullOrWhiteSpace(incoming.ClientName) ? existing.ClientName : incoming.ClientName,
                Notes = string.IsNullOrWhiteSpace(incoming.Notes) ? existing.Notes : incoming.Notes,
                DisbursementAccount = string.IsNullOrWhiteSpace(incoming.DisbursementAccount) ? existing.DisbursementAccount : incoming.DisbursementAccount,
                DisbursementBank = string.IsNullOrWhiteSpace(incoming.DisbursementBank) ? existing.DisbursementBank : incoming.DisbursementBank
            };
        }
    }
}

 

