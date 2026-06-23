using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Enums;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;

namespace OnwardsSwift.API.Controllers
{
    [Authorize]
    public class TransactionsController : AppController
    {
        private readonly OnwardsSwift.Infrastructure.Data.DapperContext _ctx;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IConfiguration _configuration;

        public TransactionsController(OnwardsSwift.Infrastructure.Data.DapperContext ctx, IWebHostEnvironment webHostEnvironment, IConfiguration configuration)
        {
            _ctx = ctx;
            _webHostEnvironment = webHostEnvironment;
            _configuration = configuration;
        }

        [HttpGet]
        public async Task<IActionResult> Index()
        {
            using var conn = _ctx.Create();

            var chequeSql = @"
SELECT TOP 200
    'Cheque Encashment' AS [Type],
    r.Id,
    r.ApplicantName AS Reference,
    COALESCE(mu.full_name, NULLIF(r.CreatedBy, ''), '') AS SubmittedBy,
    r.CreatedAt,
    LEFT(ISNULL(r.Purpose, ''), 140) AS Summary,
    CASE WHEN EXISTS(SELECT 1 FROM dbo.OfficialUseRecords o WHERE o.RequestId = r.Id) THEN 1 ELSE 0 END AS IsStep3Completed
FROM dbo.ChequeEncashmentRequests r
LEFT JOIN dbo.MobileUsers mu ON mu.id = TRY_CAST(r.CreatedBy AS BIGINT)
ORDER BY r.CreatedAt DESC;";

            var bondSql = @"
SELECT TOP 200
    'Bond Application' AS [Type],
    b.Id,
    ISNULL(NULLIF(b.ApplicantName, ''), b.Procuring) AS Reference,
    COALESCE(mu.full_name, NULLIF(b.CreatedBy, ''), '') AS SubmittedBy,
    b.CreatedAt,
    LEFT(CONCAT(ISNULL(b.GuaranteeFigures, ''), ' ', ISNULL(b.GuaranteeWords, '')), 140) AS Summary
FROM dbo.BondApplications b
LEFT JOIN dbo.MobileUsers mu ON mu.id = TRY_CAST(b.CreatedBy AS BIGINT)
ORDER BY b.CreatedAt DESC;";

            var vm = new PortalProfileIndexViewModel();
            vm.ChequeSubmissions = (await conn.QueryAsync<TransactionReviewListItem>(chequeSql)).ToList();

            if (await TableExistsAsync(conn, "dbo.BondApplications"))
            {
                vm.BondSubmissions = (await conn.QueryAsync<TransactionReviewListItem>(bondSql)).ToList();
            }

            return View(vm);
        }

        // Read-only report: surfaces rows that look like accidental duplicates from a failed/
        // retried mobile sync (same applicant + same purpose/entity, created within an hour of
        // each other), so staff can review and decide what to remove via the existing Delete
        // actions below -- nothing here deletes automatically.
        [HttpGet]
        public async Task<IActionResult> PossibleDuplicates()
        {
            using var conn = _ctx.Create();

            var chequeDuplicates = (await conn.QueryAsync<dynamic>(@"
SELECT r.Id, r.ApplicantName, r.Phone, r.IdNumber, r.Purpose, r.CreatedAt, r.CreatedBy
FROM dbo.ChequeEncashmentRequests r
WHERE r.Phone IS NOT NULL AND r.Phone <> ''
  AND EXISTS (
      SELECT 1 FROM dbo.ChequeEncashmentRequests r2
      WHERE r2.Phone = r.Phone
        AND ISNULL(r2.IdNumber, '') = ISNULL(r.IdNumber, '')
        AND ISNULL(r2.Purpose, '') = ISNULL(r.Purpose, '')
        AND r2.Id <> r.Id
        AND ABS(DATEDIFF(MINUTE, r2.CreatedAt, r.CreatedAt)) <= 60
  )
ORDER BY r.Phone, r.IdNumber, r.Purpose, r.CreatedAt;")).ToList();

            var bondDuplicates = new List<dynamic>();
            if (await TableExistsAsync(conn, "dbo.BondApplications"))
            {
                bondDuplicates = (await conn.QueryAsync<dynamic>(@"
SELECT b.Id, b.ApplicantName,
       COALESCE(NULLIF(b.ApplicantPhone, ''), b.ApplicantCode) AS Phone,
       b.Procuring, b.CreatedAt, b.CreatedBy
FROM dbo.BondApplications b
WHERE COALESCE(NULLIF(b.ApplicantPhone, ''), b.ApplicantCode) IS NOT NULL
  AND COALESCE(NULLIF(b.ApplicantPhone, ''), b.ApplicantCode) <> ''
  AND EXISTS (
      SELECT 1 FROM dbo.BondApplications b2
      WHERE COALESCE(NULLIF(b2.ApplicantPhone, ''), b2.ApplicantCode) = COALESCE(NULLIF(b.ApplicantPhone, ''), b.ApplicantCode)
        AND ISNULL(b2.Procuring, '') = ISNULL(b.Procuring, '')
        AND b2.Id <> b.Id
        AND ABS(DATEDIFF(MINUTE, b2.CreatedAt, b.CreatedAt)) <= 60
  )
ORDER BY Phone, b.Procuring, b.CreatedAt;")).ToList();
            }

            ViewBag.ChequeDuplicates = chequeDuplicates;
            ViewBag.BondDuplicates = bondDuplicates;
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> EditCheque(int id)
        {
            using var conn = _ctx.Create();
            var request = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
SELECT TOP 1 Id, ClientId, ApplicantName, IdNumber, PostalAddress, Phone, Purpose, TermsAccepted
FROM dbo.ChequeEncashmentRequests
WHERE Id = @id;", new { id });
            if (request == null) return NotFound();

            var model = new ChequeEncashmentViewModel
            {
                Id = request.Id,
                ClientId = request.ClientId,
                ApplicantName = request.ApplicantName ?? string.Empty,
                IdNumber = request.IdNumber ?? string.Empty,
                PostalAddress = request.PostalAddress ?? string.Empty,
                Phone = request.Phone ?? string.Empty,
                Purpose = request.Purpose ?? string.Empty,
                TermsAccepted = request.TermsAccepted is bool b && b
            };

            model.Cheques = (await conn.QueryAsync<ChequeItem>(@"
SELECT ChequeNumber AS Number, Amount, Dated, Drawer, Bank, Branch, Payee
FROM dbo.ChequeEncashmentCheques
WHERE RequestId = @id
ORDER BY Id;", new { id })).ToList();

            model.ExistingAttachments = (await conn.QueryAsync<AttachmentItem>(@"
SELECT FileName, FilePath, ContentType
FROM dbo.ChequeEncashmentAttachments
WHERE RequestId = @id
ORDER BY Id;", new { id })).ToList();

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditCheque(int id, ChequeEncashmentViewModel model)
        {
            if (!model.TermsAccepted)
            {
                ModelState.AddModelError(nameof(model.TermsAccepted), "Please accept the Terms and Conditions before saving.");
            }

            if (!ModelState.IsValid)
            {
                return View(model);
            }

            using var conn = _ctx.Create();
            try
            {
                var updateSql = @"
UPDATE dbo.ChequeEncashmentRequests
SET ApplicantName = @ApplicantName,
    IdNumber = @IdNumber,
    PostalAddress = @PostalAddress,
    Phone = @Phone,
    Purpose = @Purpose,
    TermsAccepted = @TermsAccepted
WHERE Id = @Id;";

                await conn.ExecuteAsync(updateSql, new
                {
                    Id = id,
                    model.ApplicantName,
                    model.IdNumber,
                    model.PostalAddress,
                    model.Phone,
                    model.Purpose,
                    TermsAccepted = model.TermsAccepted ? 1 : 0
                });

                await conn.ExecuteAsync("DELETE FROM dbo.ChequeEncashmentCheques WHERE RequestId = @Id", new { Id = id });

                if (model.Cheques != null && model.Cheques.Any())
                {
                    var insertChequeSql = @"
INSERT INTO dbo.ChequeEncashmentCheques
    (RequestId, ChequeNumber, Amount, Dated, Drawer, Bank, Branch, Payee)
VALUES
    (@RequestId, @ChequeNumber, @Amount, @Dated, @Drawer, @Bank, @Branch, @Payee);";

                    foreach (var c in model.Cheques.Where(c => !string.IsNullOrWhiteSpace(c.Number)))
                    {
                        await conn.ExecuteAsync(insertChequeSql, new
                        {
                            RequestId = id,
                            ChequeNumber = c.Number,
                            Amount = c.Amount,
                            Dated = c.Dated,
                            Drawer = c.Drawer,
                            Bank = c.Bank,
                            Branch = c.Branch,
                            Payee = c.Payee
                        });
                    }
                }

                if (model.Attachments != null && model.Attachments.Length > 0)
                {
                    var insertAttachSql = @"
INSERT INTO dbo.ChequeEncashmentAttachments
    (RequestId, FilePath, FileName, ContentType, CreatedAt)
VALUES
    (@RequestId, @FilePath, @FileName, @ContentType, GETUTCDATE());";

                    foreach (var file in model.Attachments)
                    {
                        if (file == null || file.Length == 0) continue;
                        var path = await SaveFile(file, "CE_Edit");
                        await conn.ExecuteAsync(insertAttachSql, new
                        {
                            RequestId = id,
                            FilePath = path,
                            FileName = file.FileName,
                            ContentType = file.ContentType
                        });
                    }
                }

                Success("Cheque encashment request updated successfully.");
                return RedirectToAction("ChequeDetails", new { id });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Failed to update cheque encashment request: " + ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> EditBond(int id)
        {
            using var conn = _ctx.Create();
            if (!await TableExistsAsync(conn, "dbo.BondApplications"))
            {
                return NotFound();
            }

            var row = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
SELECT TOP 1 *
FROM dbo.BondApplications
WHERE Id = @id;", new { id });
            if (row == null) return NotFound();

            var model = new BondApplicationViewModel
            {
                Id = row.Id,
                ApplicantName = row.ApplicantName ?? string.Empty,
                ApplicantAddress = row.ApplicantAddress ?? string.Empty,
                ApplicantCode = row.ApplicantCode ?? string.Empty,
                ApplicantTown = row.ApplicantTown ?? string.Empty,
                Procuring = row.Procuring ?? string.Empty,
                ProcAddress = row.ProcAddress ?? string.Empty,
                ProcCode = row.ProcCode ?? string.Empty,
                ProcTown = row.ProcTown ?? string.Empty,
                GuaranteeFigures = row.GuaranteeFigures ?? string.Empty,
                GuaranteeWords = row.GuaranteeWords ?? string.Empty,
                TypeBid = ((row.BondTypes ?? string.Empty).ToString().Contains("Bid/Tender bond")),
                TypePerformance = ((row.BondTypes ?? string.Empty).ToString().Contains("Performance bond")),
                TypeAdvance = ((row.BondTypes ?? string.Empty).ToString().Contains("Advance Payment Guarantee")),
                TypeRetention = ((row.BondTypes ?? string.Empty).ToString().Contains("Retention bond")),
                TypeOther = row.TypeOther ?? string.Empty,
                TenderRef = row.TenderRef ?? string.Empty,
                GuaranteeFrom = row.GuaranteeFrom,
                GuaranteeTo = row.GuaranteeTo,
                SigName1 = row.SigName1 ?? string.Empty,
                SigSignature1 = row.SigSignature1 ?? string.Empty,
                SigName2 = row.SigName2 ?? string.Empty,
                SigSignature2 = row.SigSignature2 ?? string.Empty,
                IndemnityDateDay = row.IndemnityDateDay ?? string.Empty,
                IndemnityDateMonth = row.IndemnityDateMonth ?? string.Empty,
                IndemnityDateYear = row.IndemnityDateYear ?? string.Empty,
                IndemnityName1 = row.IndemnityName1 ?? string.Empty,
                IndemnitySignature1 = row.IndemnitySignature1 ?? string.Empty,
                IndemnityName2 = row.IndemnityName2 ?? string.Empty,
                IndemnitySignature2 = row.IndemnitySignature2 ?? string.Empty,
                CompanySealStamp = row.CompanySealStamp ?? string.Empty,
            };

            if (!string.IsNullOrWhiteSpace((string?)row.AttachmentSummary))
            {
                model.ExistingAttachments = ((string)row.AttachmentSummary).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
            }

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditBond(int id, BondApplicationViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            using var conn = _ctx.Create();
            try
            {
                var bondTypes = string.Join(", ", new[]
                {
                    model.TypeBid ? "Bid/Tender bond" : null,
                    model.TypePerformance ? "Performance bond" : null,
                    model.TypeAdvance ? "Advance Payment Guarantee" : null,
                    model.TypeRetention ? "Retention bond" : null,
                }.Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>());

                var existingSummary = await conn.ExecuteScalarAsync<string?>("SELECT AttachmentSummary FROM dbo.BondApplications WHERE Id = @Id", new { Id = id });
                var attachments = new List<string>();
                if (!string.IsNullOrWhiteSpace(existingSummary))
                {
                    attachments.AddRange(existingSummary.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
                }

                if (model.Attachments != null && model.Attachments.Length > 0)
                {
                    foreach (var file in model.Attachments)
                    {
                        if (file == null || file.Length == 0) continue;
                        var webPath = await SaveBondFile(file, "BA_Edit");
                        if (!string.IsNullOrWhiteSpace(webPath)) attachments.Add(webPath);
                    }
                }

                var updateSql = @"
UPDATE dbo.BondApplications
SET ApplicantName = @ApplicantName,
    ApplicantAddress = @ApplicantAddress,
    ApplicantCode = @ApplicantCode,
    ApplicantTown = @ApplicantTown,
    Procuring = @Procuring,
    ProcAddress = @ProcAddress,
    ProcCode = @ProcCode,
    ProcTown = @ProcTown,
    GuaranteeFigures = @GuaranteeFigures,
    GuaranteeWords = @GuaranteeWords,
    BondTypes = @BondTypes,
    TypeOther = @TypeOther,
    TenderRef = @TenderRef,
    GuaranteeFrom = @GuaranteeFrom,
    GuaranteeTo = @GuaranteeTo,
    SigName1 = @SigName1,
    SigSignature1 = @SigSignature1,
    SigName2 = @SigName2,
    SigSignature2 = @SigSignature2,
    IndemnityDateDay = @IndemnityDateDay,
    IndemnityDateMonth = @IndemnityDateMonth,
    IndemnityDateYear = @IndemnityDateYear,
    IndemnityName1 = @IndemnityName1,
    IndemnitySignature1 = @IndemnitySignature1,
    IndemnityName2 = @IndemnityName2,
    IndemnitySignature2 = @IndemnitySignature2,
    CompanySealStamp = @CompanySealStamp,
    AttachmentSummary = @AttachmentSummary
WHERE Id = @Id;";

                await conn.ExecuteAsync(updateSql, new
                {
                    Id = id,
                    model.ApplicantName,
                    model.ApplicantAddress,
                    model.ApplicantCode,
                    model.ApplicantTown,
                    model.Procuring,
                    model.ProcAddress,
                    model.ProcCode,
                    model.ProcTown,
                    model.GuaranteeFigures,
                    model.GuaranteeWords,
                    BondTypes = bondTypes,
                    model.TypeOther,
                    model.TenderRef,
                    GuaranteeFrom = model.GuaranteeFrom,
                    GuaranteeTo = model.GuaranteeTo,
                    model.SigName1,
                    model.SigSignature1,
                    model.SigName2,
                    model.SigSignature2,
                    model.IndemnityDateDay,
                    model.IndemnityDateMonth,
                    model.IndemnityDateYear,
                    model.IndemnityName1,
                    model.IndemnitySignature1,
                    model.IndemnityName2,
                    model.IndemnitySignature2,
                    model.CompanySealStamp,
                    AttachmentSummary = string.Join(" | ", attachments)
                });

                Success("Bond application updated successfully.");
                return RedirectToAction("BondDetails", new { id });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Failed to update bond application: " + ex.Message);
                return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> OnboardingDetail(int id)
        {
            using var conn = _ctx.Create();

            // Step 2 — Cheque Encashment
            var request = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
SELECT TOP 1 Id, ClientId, ApplicantName, IdNumber, PostalAddress, Phone, Purpose, TermsAccepted, CreatedAt, CreatedBy,
       Category, PaymentMethod, DisburseBank, DisburseAccount
FROM dbo.ChequeEncashmentRequests
WHERE Id = @id;", new { id });

            if (request == null) return NotFound();

            var chequeData = new ChequeEncashmentReviewViewModel
            {
                Id            = request.Id,
                ClientId      = request.ClientId,
                ApplicantName = request.ApplicantName  ?? string.Empty,
                IdNumber      = request.IdNumber       ?? string.Empty,
                PostalAddress = request.PostalAddress  ?? string.Empty,
                Phone         = request.Phone          ?? string.Empty,
                Purpose       = request.Purpose        ?? string.Empty,
                TermsAccepted = request.TermsAccepted is bool b && b,
                CreatedAt     = request.CreatedAt,
                CreatedBy     = request.CreatedBy      ?? string.Empty,
                Category        = request.Category        ?? "Individual",
                PaymentMethod   = request.PaymentMethod   ?? "MPESA",
                DisburseBank    = request.DisburseBank,
                DisburseAccount = request.DisburseAccount
            };

            chequeData.Cheques = (await conn.QueryAsync<ChequeItem>(@"
SELECT ChequeNumber AS Number, Amount, Dated, Drawer, Bank, Branch, Payee
FROM dbo.ChequeEncashmentCheques
WHERE RequestId = @id ORDER BY Id;", new { id })).ToList();

            chequeData.Attachments = (await conn.QueryAsync<AttachmentItem>(@"
SELECT FileName, FilePath, ContentType
FROM dbo.ChequeEncashmentAttachments
WHERE RequestId = @id ORDER BY Id;", new { id })).ToList();

            var vm = new OnboardingWizardProfileViewModel { ChequeData = chequeData };

            // Step 1 — Client (if linked)
            if (request.ClientId != null && (int)request.ClientId > 0)
            {
                var client = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
SELECT TOP 1 Id, CompanyName, Email, Phone, ClientType, KraPin FROM Clients WHERE Id = @cid;",
                    new { cid = (int)request.ClientId });
                if (client != null)
                {
                    vm.ClientId     = client.Id;
                    vm.ClientName   = client.CompanyName;
                    vm.ClientEmail  = client.Email;
                    vm.ClientPhone  = client.Phone;
                    vm.ClientType   = client.ClientType?.ToString();
                    vm.ClientKraPin = client.KraPin;
                }
            }

            // Step 3 — Official Use (guard for table existence)
            if (await TableExistsAsync(conn, "dbo.OfficialUseRecords"))
            {
                var officialUseRow = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
SELECT TOP 1 Id, RequestId, CheckedBy, CheckedSignature, CheckedDate,
    ConfirmedWith, Designation, BuildingStreet, DrawerStatus, ReasonForPayment,
    AccountConfirmedBy, AccountStatus,
    HeadOfTradeFinance, HeadOfTradeSignature, HeadOfTradeDate,
    InChargeFinance, InChargeFinanceSignature, InChargeFinanceDate,
    CEO, CEOSignature, CEODate, PaidByName, PaidBySignature, CreatedAt, CreatedBy
FROM dbo.OfficialUseRecords
WHERE RequestId = @id
ORDER BY Id DESC;", new { id });

                if (officialUseRow != null)
                {
                    vm.OfficialUse = new OfficialUseReviewViewModel
                    {
                        Id                       = officialUseRow.Id,
                        RequestId                = officialUseRow.RequestId,
                        CheckedBy                = officialUseRow.CheckedBy,
                        CheckedSignature         = officialUseRow.CheckedSignature,
                        CheckedDate              = officialUseRow.CheckedDate,
                        ConfirmedWith            = officialUseRow.ConfirmedWith,
                        Designation              = officialUseRow.Designation,
                        BuildingStreet           = officialUseRow.BuildingStreet,
                        DrawerStatus             = officialUseRow.DrawerStatus,
                        ReasonForPayment         = officialUseRow.ReasonForPayment,
                        AccountConfirmedBy       = officialUseRow.AccountConfirmedBy,
                        AccountStatus            = officialUseRow.AccountStatus,
                        HeadOfTradeFinance       = officialUseRow.HeadOfTradeFinance,
                        HeadOfTradeSignature     = officialUseRow.HeadOfTradeSignature,
                        HeadOfTradeDate          = officialUseRow.HeadOfTradeDate,
                        InChargeFinance          = officialUseRow.InChargeFinance,
                        InChargeFinanceSignature = officialUseRow.InChargeFinanceSignature,
                        InChargeFinanceDate      = officialUseRow.InChargeFinanceDate,
                        CEO                      = officialUseRow.CEO,
                        CEOSignature             = officialUseRow.CEOSignature,
                        CEODate                  = officialUseRow.CEODate,
                        PaidByName               = officialUseRow.PaidByName,
                        PaidBySignature          = officialUseRow.PaidBySignature,
                        CreatedAt                = officialUseRow.CreatedAt,
                        CreatedBy                = officialUseRow.CreatedBy
                    };
                }
            }

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> ChequeDetails(int id)
        {
            using var conn = _ctx.Create();

            var request = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
SELECT TOP 1 Id, ClientId, ApplicantName, IdNumber, PostalAddress, Phone, Purpose, TermsAccepted, Status, CreatedAt, CreatedBy
FROM dbo.ChequeEncashmentRequests
WHERE Id = @id;", new { id });

            if (request == null) return NotFound();

            var vm = new ChequeEncashmentReviewViewModel
            {
                Id            = request.Id,
                ClientId      = request.ClientId,
                ApplicantName = request.ApplicantName ?? string.Empty,
                IdNumber      = request.IdNumber      ?? string.Empty,
                PostalAddress = request.PostalAddress ?? string.Empty,
                Phone         = request.Phone         ?? string.Empty,
                Purpose       = request.Purpose       ?? string.Empty,
                TermsAccepted = request.TermsAccepted is bool b && b,
                CreatedAt     = request.CreatedAt,
                CreatedBy     = request.CreatedBy     ?? string.Empty,
                CurrentStatus = request.Status ?? (int)FacilityStatus.Pending
            };

            vm.Cheques = (await conn.QueryAsync<ChequeItem>(@"
SELECT ChequeNumber AS Number, Amount, Dated, Drawer, Bank, Branch, Payee
FROM dbo.ChequeEncashmentCheques
WHERE RequestId = @id
ORDER BY Id;", new { id })).ToList();

            vm.Attachments = (await conn.QueryAsync<AttachmentItem>(@"
SELECT FileName, FilePath, ContentType
FROM dbo.ChequeEncashmentAttachments
WHERE RequestId = @id
ORDER BY Id;", new { id })).ToList();

            vm.StatusHistory = await LoadStatusHistoryAsync(conn, "cheque", id);

            return View(vm);
        }

        [HttpGet]
        public async Task<IActionResult> BondDetails(int id)
        {
            using var conn = _ctx.Create();
            if (!await TableExistsAsync(conn, "dbo.BondApplications"))
            {
                return NotFound();
            }

            var row = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
SELECT TOP 1 *
FROM dbo.BondApplications
WHERE Id = @id;", new { id });

            if (row == null) return NotFound();

            var data = new BondApplicationViewModel
            {
                ApplicantName = row.ApplicantName ?? string.Empty,
                ApplicantAddress = row.ApplicantAddress ?? string.Empty,
                ApplicantCode = row.ApplicantCode ?? string.Empty,
                ApplicantTown = row.ApplicantTown ?? string.Empty,
                Procuring = row.Procuring ?? string.Empty,
                ProcAddress = row.ProcAddress ?? string.Empty,
                ProcCode = row.ProcCode ?? string.Empty,
                ProcTown = row.ProcTown ?? string.Empty,
                GuaranteeFigures = row.GuaranteeFigures ?? string.Empty,
                GuaranteeWords = row.GuaranteeWords ?? string.Empty,
                TypeBid = ((row.BondTypes ?? string.Empty).ToString().Contains("Bid/Tender bond")),
                TypePerformance = ((row.BondTypes ?? string.Empty).ToString().Contains("Performance bond")),
                TypeAdvance = ((row.BondTypes ?? string.Empty).ToString().Contains("Advance Payment Guarantee")),
                TypeRetention = ((row.BondTypes ?? string.Empty).ToString().Contains("Retention bond")),
                TypeOther = row.TypeOther ?? string.Empty,
                TenderRef = row.TenderRef ?? string.Empty,
                GuaranteeFrom = row.GuaranteeFrom,
                GuaranteeTo = row.GuaranteeTo,
                SigName1 = row.SigName1 ?? string.Empty,
                SigSignature1 = row.SigSignature1 ?? string.Empty,
                SigName2 = row.SigName2 ?? string.Empty,
                SigSignature2 = row.SigSignature2 ?? string.Empty,
                IndemnityDateDay = row.IndemnityDateDay ?? string.Empty,
                IndemnityDateMonth = row.IndemnityDateMonth ?? string.Empty,
                IndemnityDateYear = row.IndemnityDateYear ?? string.Empty,
                IndemnityName1 = row.IndemnityName1 ?? string.Empty,
                IndemnitySignature1 = row.IndemnitySignature1 ?? string.Empty,
                IndemnityName2 = row.IndemnityName2 ?? string.Empty,
                IndemnitySignature2 = row.IndemnitySignature2 ?? string.Empty,
                CompanySealStamp = row.CompanySealStamp ?? string.Empty
            };

            var vm = new BondApplicationReviewViewModel
            {
                Id = row.Id,
                Data = data,
                CreatedAt = row.CreatedAt,
                CreatedBy = row.CreatedBy ?? string.Empty,
                Attachments = string.IsNullOrWhiteSpace((string?)row.AttachmentSummary)
                    ? new List<string>()
                    : ((string)row.AttachmentSummary).Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList(),
                CurrentStatus = row.Status ?? (int)FacilityStatus.Pending
            };

            vm.StatusHistory = await LoadStatusHistoryAsync(conn, "bond", id);

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteCheque(int id)
        {
            using var conn = _ctx.Create();
            try
            {
                if (await TableExistsAsync(conn, "dbo.OfficialUseRecords"))
                {
                    await conn.ExecuteAsync("DELETE FROM dbo.OfficialUseRecords WHERE RequestId = @id", new { id });
                }

                await conn.ExecuteAsync("DELETE FROM dbo.ChequeEncashmentRequests WHERE Id = @id", new { id });
                Success("Cheque encashment request deleted.");
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "Failed to delete cheque encashment request: " + ex.Message);
                return RedirectToAction(nameof(Index));
            }
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteBond(int id)
        {
            using var conn = _ctx.Create();
            if (!await TableExistsAsync(conn, "dbo.BondApplications"))
            {
                return NotFound();
            }

            await conn.ExecuteAsync("DELETE FROM dbo.BondApplications WHERE Id = @id", new { id });
            Success("Bond application deleted.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateChequeStatus(int id, FacilityStatus status, string? note)
        {
            using var conn = _ctx.Create();

            var exists = await conn.ExecuteScalarAsync<int?>("SELECT TOP 1 Id FROM dbo.ChequeEncashmentRequests WHERE Id = @id", new { id });
            if (!exists.HasValue) return NotFound();

            await conn.ExecuteAsync(@"
UPDATE dbo.ChequeEncashmentRequests SET Status = @status, StatusNote = @note WHERE Id = @id;",
                new { id, status = (int)status, note });

            await WriteStatusHistoryAsync(conn, "cheque", id, status, note, CurrentUserName);

            Success($"Status updated to {status}.");
            return RedirectToAction(nameof(ChequeDetails), new { id });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateBondStatus(int id, FacilityStatus status, string? note)
        {
            using var conn = _ctx.Create();
            if (!await TableExistsAsync(conn, "dbo.BondApplications")) return NotFound();

            var exists = await conn.ExecuteScalarAsync<int?>("SELECT TOP 1 Id FROM dbo.BondApplications WHERE Id = @id", new { id });
            if (!exists.HasValue) return NotFound();

            await conn.ExecuteAsync(@"
UPDATE dbo.BondApplications SET Status = @status, StatusNote = @note WHERE Id = @id;",
                new { id, status = (int)status, note });

            await WriteStatusHistoryAsync(conn, "bond", id, status, note, CurrentUserName);

            Success($"Status updated to {status}.");
            return RedirectToAction(nameof(BondDetails), new { id });
        }

        private static async Task<List<RequestStatusEntry>> LoadStatusHistoryAsync(IDbConnection conn, string sourceType, int sourceId)
        {
            if (!await TableExistsAsync(conn, "dbo.RequestStatusHistory")) return new List<RequestStatusEntry>();

            var rows = (await conn.QueryAsync<dynamic>(@"
SELECT Status, StatusNote, ChangedBy, ChangedAtUtc
FROM dbo.RequestStatusHistory
WHERE SourceType = @sourceType AND SourceId = @sourceId
ORDER BY ChangedAtUtc ASC, Id ASC;", new { sourceType, sourceId })).ToList();

            return rows.Select(r => new RequestStatusEntry
            {
                Status       = ((FacilityStatus)(int)r.Status).ToString(),
                StatusNote   = (string?)r.StatusNote,
                ChangedBy    = (string?)r.ChangedBy,
                ChangedAtUtc = (DateTime)r.ChangedAtUtc
            }).ToList();
        }

        private static async Task WriteStatusHistoryAsync(IDbConnection conn, string sourceType, int sourceId, FacilityStatus status, string? note, string? changedBy)
        {
            await conn.ExecuteAsync(@"
INSERT INTO dbo.RequestStatusHistory (SourceType, SourceId, Status, StatusNote, ChangedBy, ChangedAtUtc)
VALUES (@sourceType, @sourceId, @status, @note, @changedBy, SYSUTCDATETIME());",
                new { sourceType, sourceId, status = (int)status, note, changedBy });
        }

        private string ResolveUploadsRoot()
        {
            var uploadsRoot = _configuration["FileStorage:UploadsRoot"];
            if (string.IsNullOrWhiteSpace(uploadsRoot))
            {
                uploadsRoot = "wwwroot/uploads";
            }

            return Path.IsPathRooted(uploadsRoot)
                ? uploadsRoot
                : Path.Combine(_webHostEnvironment.ContentRootPath, uploadsRoot);
        }

        private async Task<string> SaveFile(IFormFile file, string folder)
        {
            var uploadsRoot = ResolveUploadsRoot();
            var targetFolder = Path.Combine(uploadsRoot, folder);
            Directory.CreateDirectory(targetFolder);

            var fileName = Path.GetRandomFileName() + Path.GetExtension(file.FileName);
            var filePath = Path.Combine(targetFolder, fileName);

            await using var stream = new FileStream(filePath, FileMode.Create);
            await file.CopyToAsync(stream);

            var relativePath = Path.Combine("/uploads", folder, fileName).Replace("\\", "/");
            return relativePath;
        }

        private async Task<string> SaveBondFile(IFormFile file, string folder)
        {
            return await SaveFile(file, folder);
        }

        private static async Task<bool> TableExistsAsync(IDbConnection conn, string tableName)
        {
            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT CASE WHEN OBJECT_ID(@tableName, 'U') IS NULL THEN 0 ELSE 1 END",
                new { tableName });

            return exists == 1;
        }
    }
}