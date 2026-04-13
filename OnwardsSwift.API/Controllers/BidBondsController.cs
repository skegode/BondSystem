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
        private readonly IClientService _clients;
        private readonly DapperContext _ctx;
        private readonly IConfiguration Configuration;

        public BidBondsController(IBidBondService bonds , IClientService clients, DapperContext ctx, IConfiguration _configuration)
        {
            _bonds = bonds; 
            _clients = clients;
            _ctx = ctx;
            Configuration = _configuration;

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
            using var db = GetConnection();

            // 1. Query filtered Product Types where ParentProductTypeId = 1
            // We use ProductName as the display text
            var productTypesQuery = @"SELECT Id, ProductName AS Name 
                              FROM ProductTypes 
                              WHERE ProductType = 1";

            var bondTypes = await db.QueryAsync(productTypesQuery);
            ViewBag.ProductTypes = new SelectList(bondTypes, "Id", "Name");

            // 2. Query Internal Banks for Step 3 & 4
            var internalBanksQuery = @"SELECT Id, BankName + ' (' + AccountNumber + ')' as BankText 
                               FROM InternalBanks";

            var internalBanks = await db.QueryAsync(internalBanksQuery);
            ViewBag.InternalBanks = new SelectList(internalBanks, "Id", "BankText");

            // 3. Populate standard dropdowns
            // Assuming PopulateDropdowns uses EF, you can keep it or replace its contents 
            // with Dapper queries for Clients, Agents, and Banks.
            await PopulateDropdowns();

            // 4. Initialize model with smart defaults
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
                if (sessionUserId == null) return RedirectToAction("Login", "Auth");

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

                Success($"Application for {model.TenderNumber} submitted successfully.");
                return RedirectToAction("Index");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", $"System Error: {ex.Message}");
                await PopulateDropdowns();
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
            using var conn = _ctx.Create();
            var rateData = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT Rate, [Commission] as comm, ApplicationFee, ApplicationFeeType, CommissionType
                FROM ProductRates 
                WHERE BankPartnerId = @BankId AND ProductType = @ProdType AND IsActive = 1",
                new { BankId = bankId, ProdType = productType });

            if (rateData == null) return Json(new { rate = 0, comm = 0, appFee = 0, appFeeType = 1 });

            return Json(new
            {
                rate = rateData.Rate,
                comm = rateData.comm,
                commType = rateData.CommissionType,
                appFee = rateData.ApplicationFee,
                appFeeType = rateData.ApplicationFeeType
            });
        }

        private async Task PopulateDropdowns()
        {
            using var conn = _ctx.Create();

            var clients = await conn.QueryAsync<dynamic>("SELECT Id, CompanyName FROM Clients WHERE IsDeleted = 0");
            var obligees = await conn.QueryAsync<dynamic>("SELECT Id, Name FROM Obligees ORDER BY Name ASC");
            var banks = await conn.QueryAsync<dynamic>("SELECT Id, BankName as Name FROM Banks WHERE IsActive = 1");

            var agents = await conn.QueryAsync<dynamic>("SELECT Id, FullName FROM SystemUsers WHERE IsActive = 1 AND IsDeleted = 0");

            ViewBag.Clients = new SelectList(clients, "Id", "CompanyName");
            ViewBag.ProcuringEntities = new SelectList(obligees, "Id", "Name");
            ViewBag.Banks = new SelectList(banks, "Id", "Name");
            ViewBag.Agents = new SelectList(agents, "Id", "FullName");
        }
    }
}

 

