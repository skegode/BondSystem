using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces;
using System.Linq;
using System.Text.Json;
using System.IO;
using System.Text;

namespace OnwardsSwift.API.Controllers
{
    public class FormsController : AppController
    {
        private readonly OnwardsSwift.Core.Interfaces.IClientService _clients;
        private readonly OnwardsSwift.Infrastructure.Data.DapperContext _ctx;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IConfiguration _configuration;
        private readonly OnwardsSwift.Infrastructure.Services.PermissionService _permissions;

        public FormsController(OnwardsSwift.Core.Interfaces.IClientService clients, OnwardsSwift.Infrastructure.Data.DapperContext ctx, IWebHostEnvironment webHostEnvironment, IConfiguration configuration, OnwardsSwift.Infrastructure.Services.PermissionService permissions)
        {
            _clients = clients;
            _ctx = ctx;
            _webHostEnvironment = webHostEnvironment;
            _configuration = configuration;
            _permissions = permissions;
        }

        /// <summary>
        /// Official Use (Step 3) is staff-only and web-portal-only. Returns null when the
        /// current user is authorized; otherwise the IActionResult the caller should return.
        /// </summary>
        private async Task<IActionResult?> CheckOfficialUseAccessAsync(string returnUrl)
        {
            if (User.Identity?.IsAuthenticated != true)
            {
                return RedirectToAction("Login", "Account", new { returnUrl });
            }

            if (!await _permissions.UserHasPermissionAsync(User, OnwardsSwift.Infrastructure.Services.PermissionService.OfficialUseEdit))
            {
                return Forbid();
            }

            return null;
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> OnboardingWizard(int step = 1, int? clientId = null, int? requestId = null)
        {
            if (step == 3)
            {
                var denied = await CheckOfficialUseAccessAsync(Request.Path + Request.QueryString);
                if (denied != null) return denied;
            }

            var vm = new OnboardingWizardViewModel();
            vm.OfficialUse.RequestId = requestId;

            // If step 3 is requested without a requestId, resolve the latest unfinished request for this client.
            // Do not auto-load the latest request for step 2, because a new transaction should start blank.
            if (!requestId.HasValue && clientId.HasValue && step == 3)
            {
                using var conn = _ctx.Create();
                requestId = await conn.QuerySingleOrDefaultAsync<int?>(@"
SELECT TOP 1 r.Id
FROM dbo.ChequeEncashmentRequests r
LEFT JOIN dbo.OfficialUseRecords ou ON ou.RequestId = r.Id
WHERE r.ClientId = @clientId
  AND ou.Id IS NULL
ORDER BY r.CreatedAt DESC, r.Id DESC;", new { clientId = clientId.Value });
                if (requestId.HasValue)
                {
                    vm.OfficialUse.RequestId = requestId;
                }
            }

            // If requestId exists, load the existing cheque encashment request and official use details so the wizard can edit them
            if (requestId.HasValue)
            {
                using var conn = _ctx.Create();
                var request = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
SELECT TOP 1 Id, ClientId, ApplicantName, IdNumber, PostalAddress, Phone, Purpose, TermsAccepted, DeclarantName, DeclarantRole, DeclarantDate,
       Category, PaymentMethod, DisburseBank, DisburseAccount
FROM dbo.ChequeEncashmentRequests
WHERE Id = @id;", new { id = requestId.Value });

                bool hasOfficialUseRecord = false;
                if (request != null)
                {
                    vm.ChequeEncashment.Id = request.Id;
                    vm.ChequeEncashment.ClientId = request.ClientId;
                    vm.ChequeEncashment.ApplicantName = request.ApplicantName ?? string.Empty;
                    vm.ChequeEncashment.IdNumber = request.IdNumber ?? string.Empty;
                    vm.ChequeEncashment.PostalAddress = request.PostalAddress ?? string.Empty;
                    vm.ChequeEncashment.Phone = request.Phone ?? string.Empty;
                    vm.ChequeEncashment.Purpose = request.Purpose ?? string.Empty;
                    vm.ChequeEncashment.TermsAccepted = request.TermsAccepted is bool b && b;
                    vm.ChequeEncashment.DeclarantName = request.DeclarantName ?? string.Empty;
                    vm.ChequeEncashment.DeclarantRole = request.DeclarantRole ?? string.Empty;
                    vm.ChequeEncashment.DeclarantDate = request.DeclarantDate ?? string.Empty;
                    vm.ChequeEncashment.Category = request.Category ?? "Individual";
                    vm.ChequeEncashment.PaymentMethod = request.PaymentMethod ?? "MPESA";
                    vm.ChequeEncashment.DisburseBank = request.DisburseBank;
                    vm.ChequeEncashment.DisburseAccount = request.DisburseAccount;

                    vm.ChequeEncashment.Cheques = (await conn.QueryAsync<OnwardsSwift.Core.DTOs.ChequeItem>(@"
SELECT ChequeNumber AS Number, Amount, Dated, Drawer, Bank, Branch, Payee
FROM dbo.ChequeEncashmentCheques
WHERE RequestId = @id
ORDER BY Id;", new { id = requestId.Value })).ToList();

                    vm.ChequeEncashment.ExistingAttachments = (await conn.QueryAsync<OnwardsSwift.Core.DTOs.AttachmentItem>(@"
SELECT FileName, FilePath, ContentType
FROM dbo.ChequeEncashmentAttachments
WHERE RequestId = @id
ORDER BY Id;", new { id = requestId.Value })).ToList();

                    if (await conn.ExecuteScalarAsync<int>(@"SELECT CASE WHEN OBJECT_ID('dbo.OfficialUseRecords','U') IS NULL THEN 0 ELSE 1 END") == 1)
                    {
                        var officialUse = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
SELECT TOP 1 CheckedBy, CheckedDate, ConfirmedWith, Designation, BuildingStreet, DrawerStatus, ReasonForPayment,
       AccountConfirmedBy, AccountStatus, HeadOfTradeFinance, HeadOfTradeDate, InChargeFinance, InChargeFinanceDate,
       CEO, CEODate, PaidByName
FROM dbo.OfficialUseRecords
WHERE RequestId = @id
ORDER BY Id DESC;", new { id = requestId.Value });
                        if (officialUse != null)
                        {
                            hasOfficialUseRecord = true;
                            vm.OfficialUse.RequestId = requestId;
                            vm.OfficialUse.CheckedBy = officialUse.CheckedBy ?? string.Empty;
                            vm.OfficialUse.CheckedDate = officialUse.CheckedDate ?? string.Empty;
                            vm.OfficialUse.ConfirmedWith = officialUse.ConfirmedWith ?? string.Empty;
                            vm.OfficialUse.Designation = officialUse.Designation ?? string.Empty;
                            vm.OfficialUse.BuildingStreet = officialUse.BuildingStreet ?? string.Empty;
                            vm.OfficialUse.DrawerStatus = officialUse.DrawerStatus ?? string.Empty;
                            vm.OfficialUse.ReasonForPayment = officialUse.ReasonForPayment ?? string.Empty;
                            vm.OfficialUse.AccountConfirmedBy = officialUse.AccountConfirmedBy ?? string.Empty;
                            vm.OfficialUse.AccountStatus = officialUse.AccountStatus ?? string.Empty;
                            vm.OfficialUse.HeadOfTradeFinance = officialUse.HeadOfTradeFinance ?? string.Empty;
                            vm.OfficialUse.HeadOfTradeDate = officialUse.HeadOfTradeDate ?? string.Empty;
                            vm.OfficialUse.InChargeFinance = officialUse.InChargeFinance ?? string.Empty;
                            vm.OfficialUse.InChargeFinanceDate = officialUse.InChargeFinanceDate ?? string.Empty;
                            vm.OfficialUse.CEO = officialUse.CEO ?? string.Empty;
                            vm.OfficialUse.CEODate = officialUse.CEODate ?? string.Empty;
                            vm.OfficialUse.PaidByName = officialUse.PaidByName ?? string.Empty;
                        }
                    }

                    if (request.ClientId != null && (int)request.ClientId > 0)
                    {
                        var client = await _clients.GetByIdAsync((int)request.ClientId);
                        if (client != null)
                        {
                            vm.NewClient.CompanyName = client.CompanyName ?? string.Empty;
                            vm.NewClient.KraPin = client.KraPin ?? string.Empty;
                            vm.NewClient.IdNumber = client.IdNumber;
                            vm.NewClient.ContactPerson = client.ContactPerson ?? string.Empty;
                            vm.NewClient.Email = client.Email;
                            vm.NewClient.Phone = client.Phone ?? string.Empty;
                            vm.NewClient.PhoneAlt = client.PhoneAlt ?? string.Empty;
                            vm.NewClient.PhysicalAddress = client.PhysicalAddress ?? string.Empty;
                            vm.NewClient.PostalAddress = client.PostalAddress ?? string.Empty;
                            vm.NewClient.BusinessRegNumber = client.BusinessRegNumber ?? string.Empty;
                            vm.NewClient.ClientType = (OnwardsSwift.Core.Enums.ClientType)(int.TryParse(client.ClientType, out var ct) ? ct : 1);
                            vm.NewClient.Category = client.Category ?? 1;
                            vm.NewClient.Gender = client.Gender;
                            clientId = client.Id;
                        }
                    }

                    if (step == 1)
                    {
                        step = hasOfficialUseRecord ? 3 : 2;
                    }
                }
            }

            // If clientId exists, pre-fill saved client details so the wizard stays on the correct category when returning
            if (clientId.HasValue)
            {
                var client = await _clients.GetByIdAsync(clientId.Value);
                if (client != null)
                {
                    vm.NewClient.CompanyName = client.CompanyName ?? string.Empty;
                    vm.NewClient.KraPin = client.KraPin ?? string.Empty;
                    vm.NewClient.IdNumber = client.IdNumber;
                    vm.NewClient.ContactPerson = client.ContactPerson ?? string.Empty;
                    vm.NewClient.Email = client.Email;
                    vm.NewClient.Phone = client.Phone ?? string.Empty;
                    vm.NewClient.PhoneAlt = client.PhoneAlt ?? string.Empty;
                    vm.NewClient.PhysicalAddress = client.PhysicalAddress ?? string.Empty;
                    vm.NewClient.PostalAddress = client.PostalAddress ?? string.Empty;
                    vm.NewClient.BusinessRegNumber = client.BusinessRegNumber ?? string.Empty;
                    vm.NewClient.ClientType = (OnwardsSwift.Core.Enums.ClientType)(int.TryParse(client.ClientType, out var ct) ? ct : 1);
                    vm.NewClient.Category = client.Category ?? 1;
                    vm.NewClient.Gender = client.Gender;

                    if (!vm.ChequeEncashment.ClientId.HasValue)
                    {
                        vm.ChequeEncashment.ClientId = client.Id;
                        vm.ChequeEncashment.ApplicantName = client.CompanyName;
                        vm.ChequeEncashment.IdNumber = client.IdNumber ?? string.Empty;
                        vm.ChequeEncashment.PostalAddress = client.PostalAddress ?? string.Empty;
                        vm.ChequeEncashment.Phone = client.Phone ?? string.Empty;
                    }
                }
            }

            // step may have been auto-upgraded to 3 above (an Official Use record already exists
            // for this request) even though the caller asked for step 1 — re-check here using the
            // final effective step, not just the originally requested one.
            if (step == 3)
            {
                var denied = await CheckOfficialUseAccessAsync(Request.Path + Request.QueryString);
                if (denied != null) return denied;
            }

            ViewData["WizardStep"]      = step;
            ViewData["WizardClientId"]  = clientId;
            ViewData["WizardRequestId"] = requestId;
            return View(vm);
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Landing()
        {
            return View();
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult TermsAndConditions(string? name, string? idNumber)
        {
            ViewData["TermsName"] = name;
            ViewData["TermsIdNumber"] = idNumber;
            return View();
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> FullApplication(int step = 1, int? requestId = null)
        {
            var model = new BondApplicationViewModel();

            // If requestId is provided, load the existing bond application so it can be edited in the wizard
            if (requestId.HasValue)
            {
                using var conn = _ctx.Create();
                var bondApp = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
SELECT Id, ApplicantName, ApplicantAddress, ApplicantCode, ApplicantTown, 
       Procuring, ProcAddress, ProcCode, ProcTown,
       GuaranteeFigures, GuaranteeWords, BondTypes, TypeOther, TenderRef, GuaranteeFrom, GuaranteeTo,
       SigName1, SigSignature1, SigName2, SigSignature2,
       IndemnityDateDay, IndemnityDateMonth, IndemnityDateYear, IndemnityName1, IndemnitySignature1, 
       IndemnityName2, IndemnitySignature2, CompanySealStamp, AttachmentSummary
FROM dbo.BondApplications
WHERE Id = @id", new { id = requestId.Value });

                if (bondApp != null)
                {
                    model.Id = bondApp.Id;
                    model.ApplicantName = bondApp.ApplicantName ?? string.Empty;
                    model.ApplicantAddress = bondApp.ApplicantAddress ?? string.Empty;
                    model.ApplicantCode = bondApp.ApplicantCode ?? string.Empty;
                    model.ApplicantTown = bondApp.ApplicantTown ?? string.Empty;
                    model.Procuring = bondApp.Procuring ?? string.Empty;
                    model.ProcAddress = bondApp.ProcAddress ?? string.Empty;
                    model.ProcCode = bondApp.ProcCode ?? string.Empty;
                    model.ProcTown = bondApp.ProcTown ?? string.Empty;
                    model.GuaranteeFigures = bondApp.GuaranteeFigures ?? string.Empty;
                    model.GuaranteeWords = bondApp.GuaranteeWords ?? string.Empty;
                    
                    // Parse bond types
                    var bondTypes = bondApp.BondTypes ?? string.Empty;
                    model.TypeBid = bondTypes.Contains("Bid");
                    model.TypePerformance = bondTypes.Contains("Performance");
                    model.TypeAdvance = bondTypes.Contains("Advance");
                    model.TypeRetention = bondTypes.Contains("Retention");
                    
                    model.TypeOther = bondApp.TypeOther ?? string.Empty;
                    model.TenderRef = bondApp.TenderRef ?? string.Empty;
                    model.GuaranteeFrom = bondApp.GuaranteeFrom;
                    model.GuaranteeTo = bondApp.GuaranteeTo;
                    model.SigName1 = bondApp.SigName1 ?? string.Empty;
                    model.SigSignature1 = bondApp.SigSignature1 ?? string.Empty;
                    model.SigName2 = bondApp.SigName2 ?? string.Empty;
                    model.SigSignature2 = bondApp.SigSignature2 ?? string.Empty;
                    model.IndemnityDateDay = bondApp.IndemnityDateDay ?? string.Empty;
                    model.IndemnityDateMonth = bondApp.IndemnityDateMonth ?? string.Empty;
                    model.IndemnityDateYear = bondApp.IndemnityDateYear ?? string.Empty;
                    model.IndemnityName1 = bondApp.IndemnityName1 ?? string.Empty;
                    model.IndemnitySignature1 = bondApp.IndemnitySignature1 ?? string.Empty;
                    model.IndemnityName2 = bondApp.IndemnityName2 ?? string.Empty;
                    model.IndemnitySignature2 = bondApp.IndemnitySignature2 ?? string.Empty;
                    model.CompanySealStamp = bondApp.CompanySealStamp ?? string.Empty;

                    // Parse existing attachments from AttachmentSummary.
                    // The stored format may use '|' or ';' as delimiters depending on prior save logic.
                    var attachmentSummary = (string)(bondApp.AttachmentSummary ?? string.Empty);
                    if (!string.IsNullOrWhiteSpace(attachmentSummary))
                    {
                        model.ExistingAttachments = attachmentSummary
                            .Split(new[] { ';', '|' }, StringSplitOptions.RemoveEmptyEntries)
                            .Select(p => p.Trim())
                            .Where(p => !string.IsNullOrWhiteSpace(p))
                            .ToList();
                    }
                }
            }

            ViewBag.CurrentStep = step;
            return View(model);
        }

        [AllowAnonymous]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> FullApplication(BondApplicationViewModel model)
        {
            if (!ModelState.IsValid)
            {
                return View(model);
            }

            try
            {
                if (model.SigSignature1File != null && model.SigSignature1File.Length > 0)
                {
                    model.SigSignature1 = await SaveSignatureFile(model.SigSignature1File, "BondSig1");
                }
                if (model.SigSignature2File != null && model.SigSignature2File.Length > 0)
                {
                    model.SigSignature2 = await SaveSignatureFile(model.SigSignature2File, "BondSig2");
                }
                if (model.IndemnitySignature1File != null && model.IndemnitySignature1File.Length > 0)
                {
                    model.IndemnitySignature1 = await SaveSignatureFile(model.IndemnitySignature1File, "IndemnitySig1");
                }
                if (model.IndemnitySignature2File != null && model.IndemnitySignature2File.Length > 0)
                {
                    model.IndemnitySignature2 = await SaveSignatureFile(model.IndemnitySignature2File, "IndemnitySig2");
                }

                var savedFiles = new List<string>();
                var savedFileSummaries = new List<string>();
                
                // If editing, preserve existing attachments
                if (model.Id.HasValue && model.ExistingAttachments.Any())
                {
                    savedFileSummaries.AddRange(model.ExistingAttachments);
                }
                
                if (model.Attachments != null && model.Attachments.Length > 0)
                {
                    foreach (var file in model.Attachments)
                    {
                        if (file == null || file.Length == 0) continue;
                        var path = await SaveBondFile(file, "BA_Attach");
                        if (!string.IsNullOrWhiteSpace(path))
                        {
                            savedFiles.Add(path);
                            savedFileSummaries.Add(path);
                        }
                    }
                }

                var bondTypes = string.Join(", ", new[]
                {
                    model.TypeBid ? "Bid/Tender bond" : null,
                    model.TypePerformance ? "Performance bond" : null,
                    model.TypeAdvance ? "Advance Payment Guarantee" : null,
                    model.TypeRetention ? "Retention bond" : null,
                }.Where(x => !string.IsNullOrWhiteSpace(x)).Cast<string>());

                using var conn = _ctx.Create();
                int bondId;

                // If model has an Id, update existing; otherwise insert new
                if (model.Id.HasValue)
                {
                    var updateSql = @"
UPDATE dbo.BondApplications SET
    ApplicantName = @ApplicantName,
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
WHERE Id = @Id";

                    await conn.ExecuteAsync(updateSql, new
                    {
                        model.Id,
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
                        AttachmentSummary = string.Join(" | ", savedFileSummaries)
                    });
                    
                    bondId = model.Id.Value;
                }
                else
                {
                    var insertSql = @"
INSERT INTO dbo.BondApplications
    (ApplicantName, ApplicantAddress, ApplicantCode, ApplicantTown, Procuring, ProcAddress, ProcCode, ProcTown,
     GuaranteeFigures, GuaranteeWords, BondTypes, TypeOther, TenderRef, GuaranteeFrom, GuaranteeTo,
     SigName1, SigSignature1, SigName2, SigSignature2,
     IndemnityDateDay, IndemnityDateMonth, IndemnityDateYear, IndemnityName1, IndemnitySignature1, IndemnityName2, IndemnitySignature2,
     CompanySealStamp, AttachmentSummary, CreatedAt, CreatedBy)
VALUES
    (@ApplicantName, @ApplicantAddress, @ApplicantCode, @ApplicantTown, @Procuring, @ProcAddress, @ProcCode, @ProcTown,
     @GuaranteeFigures, @GuaranteeWords, @BondTypes, @TypeOther, @TenderRef, @GuaranteeFrom, @GuaranteeTo,
     @SigName1, @SigSignature1, @SigName2, @SigSignature2,
     @IndemnityDateDay, @IndemnityDateMonth, @IndemnityDateYear, @IndemnityName1, @IndemnitySignature1, @IndemnityName2, @IndemnitySignature2,
     @CompanySealStamp, @AttachmentSummary, GETUTCDATE(), @CreatedBy);
SELECT CAST(SCOPE_IDENTITY() as int);";

                    bondId = await conn.QuerySingleAsync<int>(insertSql, new
                    {
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
                        AttachmentSummary = string.Join(" | ", savedFileSummaries),
                        CreatedBy = CurrentUserEmail
                    });
                }

                Success($"Thank you — your bond application has been received. The process is complete and you will be returned to the Onboarding Portal shortly.");
                return RedirectToAction(nameof(Landing));
            }
            catch (Exception ex)
            {
                ModelState.AddModelError(string.Empty, "Failed to submit bond application: " + ex.Message);
                return View(model);
            }
        }

        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> ChequeEncashment(int? clientId)
        {
            var vm = new OnwardsSwift.Core.DTOs.ChequeEncashmentViewModel();
            if (clientId.HasValue)
            {
                var client = await _clients.GetByIdAsync(clientId.Value);
                if (client != null)
                {
                    vm.ClientId = client.Id;
                    vm.ApplicantName = client.CompanyName;
                    vm.IdNumber = client.IdNumber ?? string.Empty;
                    vm.PostalAddress = client.PostalAddress ?? string.Empty;
                    vm.Phone = client.Phone ?? string.Empty;
                }
            }
            return View(vm);
        }

        [AllowAnonymous]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ChequeEncashment(OnwardsSwift.Core.DTOs.ChequeEncashmentViewModel model, string? returnUrl, string? WizardSource)
        {
            // Quick diagnostics helper: writes posted form + files + ModelState to App_Data for troubleshooting
            async Task WriteDiagnosticsAsync(string note)
            {
                try
                {
                    var root = Path.Combine(_webHostEnvironment.ContentRootPath, "App_Data");
                    if (!Directory.Exists(root)) Directory.CreateDirectory(root);
                    var file = Path.Combine(root, "diagnostics-onboarding.log");
                    var sb = new StringBuilder();
                    sb.AppendLine("--- DIAGNOSTIC ENTRY " + DateTime.UtcNow.ToString("o") + " ---");
                    sb.AppendLine(note);
                    try
                    {
                        sb.AppendLine("Form values:");
                        foreach (var k in Request.Form.Keys)
                        {
                            sb.AppendLine($"  {k} = {Request.Form[k]}");
                        }
                    }
                    catch { sb.AppendLine("  (failed to read Request.Form)"); }
                    try
                    {
                        sb.AppendLine("Files:");
                        foreach (var f in Request.Form.Files)
                        {
                            sb.AppendLine($"  {f.Name} -> {f.FileName} ({f.Length} bytes, {f.ContentType})");
                        }
                    }
                    catch { sb.AppendLine("  (failed to read files)"); }
                    try
                    {
                        if (!ModelState.IsValid)
                        {
                            sb.AppendLine("ModelState errors:");
                            foreach (var kv in ModelState)
                            {
                                if (kv.Value.Errors != null && kv.Value.Errors.Count > 0)
                                {
                                    foreach (var e in kv.Value.Errors)
                                    {
                                        sb.AppendLine($"  {kv.Key}: {e.ErrorMessage} | Exception: {e.Exception?.Message}");
                                    }
                                }
                            }
                        }
                        else sb.AppendLine("ModelState: Valid");
                    }
                    catch { sb.AppendLine("  (failed to read ModelState)"); }

                    await System.IO.File.AppendAllTextAsync(file, sb.ToString());
                }
                catch { }
            }

            bool isWizardReturn = string.Equals(WizardSource, "OnboardingWizard", StringComparison.OrdinalIgnoreCase)
                                  || (!string.IsNullOrWhiteSpace(returnUrl)
                                      && returnUrl.Contains("OnboardingWizard", StringComparison.OrdinalIgnoreCase)
                                      && (Url.IsLocalUrl(returnUrl) || Uri.TryCreate(returnUrl, UriKind.Absolute, out var parsedUri) && (parsedUri.IsLoopback || string.Equals(parsedUri.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))));
            if (!isWizardReturn)
            {
                var referer = Request.Headers["Referer"].FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(referer) && referer.Contains("/Forms/OnboardingWizard", StringComparison.OrdinalIgnoreCase))
                {
                    isWizardReturn = true;
                }
            }

            // Category drives the disbursement method: Individual -> M-Pesa, Company -> Bank.
            // Determine server-side rather than trusting any client-posted PaymentMethod.
            model.Category = string.Equals(model.Category, "Company", StringComparison.OrdinalIgnoreCase) ? "Company" : "Individual";
            model.PaymentMethod = model.Category == "Company" ? "BANK" : "MPESA";

            if (!model.TermsAccepted)
            {
                ModelState.AddModelError(nameof(model.TermsAccepted), "Please read and accept the Terms and Conditions before proceeding.");
            }

            if (model.Category == "Company")
            {
                if (string.IsNullOrWhiteSpace(model.DisburseBank))
                    ModelState.AddModelError(nameof(model.DisburseBank), "Bank name is required for company payments.");
                if (string.IsNullOrWhiteSpace(model.DisburseAccount))
                    ModelState.AddModelError(nameof(model.DisburseAccount), "Bank account number is required for company payments.");
            }

            if (string.IsNullOrWhiteSpace(model.ApplicantName))
                ModelState.AddModelError(nameof(model.ApplicantName), "Applicant name is required.");
            if (string.IsNullOrWhiteSpace(model.Phone))
                ModelState.AddModelError(nameof(model.Phone), "Phone number is required.");
            if (string.IsNullOrWhiteSpace(model.Purpose))
                ModelState.AddModelError(nameof(model.Purpose), "Purpose is required.");
            if (string.IsNullOrWhiteSpace(model.DeclarantName))
                ModelState.AddModelError(nameof(model.DeclarantName), "Declarant name is required.");
            if (string.IsNullOrWhiteSpace(model.DeclarantRole))
                ModelState.AddModelError(nameof(model.DeclarantRole), "Declarant role is required.");
            if (string.IsNullOrWhiteSpace(model.DeclarantDate))
                ModelState.AddModelError(nameof(model.DeclarantDate), "Declaration date is required.");

            var hasChequeRows = model.Cheques?.Any(c => !string.IsNullOrWhiteSpace(c.Number)
                                                       || c.Amount.HasValue
                                                       || !string.IsNullOrWhiteSpace(c.Dated)
                                                       || !string.IsNullOrWhiteSpace(c.Drawer)
                                                       || !string.IsNullOrWhiteSpace(c.Bank)
                                                       || !string.IsNullOrWhiteSpace(c.Branch)
                                                       || !string.IsNullOrWhiteSpace(c.Payee)) ?? false;
            if (!hasChequeRows)
            {
                ModelState.AddModelError("Cheques", "Add at least one cheque entry with a valid number, amount, date, drawer, bank, branch and payee.");
            }
            else
            {
                for (var i = 0; i < (model.Cheques?.Count ?? 0); i++)
                {
                    var cheque = model.Cheques![i];
                    if (string.IsNullOrWhiteSpace(cheque.Number) && !cheque.Amount.HasValue && string.IsNullOrWhiteSpace(cheque.Dated) && string.IsNullOrWhiteSpace(cheque.Drawer) && string.IsNullOrWhiteSpace(cheque.Bank) && string.IsNullOrWhiteSpace(cheque.Branch) && string.IsNullOrWhiteSpace(cheque.Payee))
                        continue;

                    if (string.IsNullOrWhiteSpace(cheque.Number))
                        ModelState.AddModelError($"Cheques[{i}].Number", "Cheque number is required.");
                    if (!cheque.Amount.HasValue || cheque.Amount.Value <= 0)
                        ModelState.AddModelError($"Cheques[{i}].Amount", "Cheque amount must be greater than zero.");
                    if (string.IsNullOrWhiteSpace(cheque.Dated))
                        ModelState.AddModelError($"Cheques[{i}].Dated", "Cheque date is required.");
                    else if (!DateTime.TryParse(cheque.Dated, out _))
                        ModelState.AddModelError($"Cheques[{i}].Dated", "Cheque date must be valid.");
                    if (string.IsNullOrWhiteSpace(cheque.Drawer))
                        ModelState.AddModelError($"Cheques[{i}].Drawer", "Drawer name is required.");
                    if (string.IsNullOrWhiteSpace(cheque.Bank))
                        ModelState.AddModelError($"Cheques[{i}].Bank", "Bank name is required.");
                    if (string.IsNullOrWhiteSpace(cheque.Branch))
                        ModelState.AddModelError($"Cheques[{i}].Branch", "Bank branch is required.");
                    if (string.IsNullOrWhiteSpace(cheque.Payee))
                        ModelState.AddModelError($"Cheques[{i}].Payee", "Payee is required.");
                }
            }

            if (!ModelState.IsValid)
            {
                await WriteDiagnosticsAsync("Validation failed before saving cheque encashment.");
                if (isWizardReturn)
                {
                    var vm = new OnboardingWizardViewModel { ChequeEncashment = model };
                    ViewData["WizardStep"] = 2;
                    ViewData["WizardClientId"] = model.ClientId;
                    ViewData["WizardRequestId"] = model.Id;
                    return View("~/Views/Forms/OnboardingWizard.cshtml", vm);
                }
                return View(model);
            }

            using var conn = _ctx.Create();
            try
            {
                int requestId;
                if (model.Id.HasValue && model.Id.Value > 0)
                {
                    var updateReqSql = @"
UPDATE dbo.ChequeEncashmentRequests
SET ClientId = @ClientId,
    ApplicantName = @ApplicantName,
    IdNumber = @IdNumber,
    PostalAddress = @PostalAddress,
    Phone = @Phone,
    Purpose = @Purpose,
    TermsAccepted = @TermsAccepted,
    DeclarantName = @DeclarantName,
    DeclarantRole = @DeclarantRole,
    DeclarantDate = @DeclarantDate,
    Category = @Category,
    PaymentMethod = @PaymentMethod,
    DisburseBank = @DisburseBank,
    DisburseAccount = @DisburseAccount
WHERE Id = @Id;";

                    await conn.ExecuteAsync(updateReqSql, new
                    {
                        Id = model.Id.Value,
                        ClientId = model.ClientId,
                        ApplicantName = model.ApplicantName,
                        IdNumber = model.IdNumber,
                        PostalAddress = model.PostalAddress,
                        Phone = model.Phone,
                        Purpose = model.Purpose,
                        TermsAccepted = model.TermsAccepted ? 1 : 0,
                        DeclarantName = model.DeclarantName,
                        DeclarantRole = model.DeclarantRole,
                        DeclarantDate = model.DeclarantDate,
                        Category = model.Category,
                        PaymentMethod = model.PaymentMethod,
                        DisburseBank = model.Category == "Company" ? model.DisburseBank : null,
                        DisburseAccount = model.Category == "Company" ? model.DisburseAccount : null
                    });
                    requestId = model.Id.Value;
                }
                else
                {
                    var insertReqSql = @"
INSERT INTO dbo.ChequeEncashmentRequests
    (ClientId, ApplicantName, IdNumber, PostalAddress, Phone, Purpose, TermsAccepted, DeclarantName, DeclarantRole, DeclarantDate, Category, PaymentMethod, DisburseBank, DisburseAccount, CreatedAt, CreatedBy)
VALUES
    (@ClientId, @ApplicantName, @IdNumber, @PostalAddress, @Phone, @Purpose, @TermsAccepted, @DeclarantName, @DeclarantRole, @DeclarantDate, @Category, @PaymentMethod, @DisburseBank, @DisburseAccount, GETUTCDATE(), @CreatedBy);
SELECT CAST(SCOPE_IDENTITY() as int);";

                    requestId = await conn.QuerySingleAsync<int>(insertReqSql, new
                    {
                        ClientId = model.ClientId,
                        ApplicantName = model.ApplicantName,
                        IdNumber = model.IdNumber,
                        PostalAddress = model.PostalAddress,
                        Phone = model.Phone,
                        Purpose = model.Purpose,
                        TermsAccepted = model.TermsAccepted ? 1 : 0,
                        DeclarantName = model.DeclarantName,
                        DeclarantRole = model.DeclarantRole,
                        DeclarantDate = model.DeclarantDate,
                        Category = model.Category,
                        PaymentMethod = model.PaymentMethod,
                        DisburseBank = model.Category == "Company" ? model.DisburseBank : null,
                        DisburseAccount = model.Category == "Company" ? model.DisburseAccount : null,
                        CreatedBy = CurrentUserEmail
                    });
                }

                // For edit workflows, replace any existing cheque rows before inserting the current values
                if (model.Id.HasValue && model.Id.Value > 0)
                {
                    await conn.ExecuteAsync("DELETE FROM dbo.ChequeEncashmentCheques WHERE RequestId = @Id;", new { Id = model.Id.Value });
                }

                if (model.Cheques != null && model.Cheques.Count > 0)
                {
                    var insertChequeSql = @"
INSERT INTO dbo.ChequeEncashmentCheques
    (RequestId, ChequeNumber, Amount, Dated, Drawer, Bank, Branch, Payee)
VALUES
    (@RequestId, @ChequeNumber, @Amount, @Dated, @Drawer, @Bank, @Branch, @Payee);";

                    foreach (var c in model.Cheques)
                    {
                        await conn.ExecuteAsync(insertChequeSql, new
                        {
                            RequestId = requestId,
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

                // Save attachments
                if (model.Attachments != null && model.Attachments.Length > 0)
                {
                    var insertAttachSql = @"
INSERT INTO dbo.ChequeEncashmentAttachments
    (RequestId, FilePath, FileName, ContentType, CreatedAt)
VALUES
    (@RequestId, @FilePath, @FileName, @ContentType, GETUTCDATE());";

                    foreach (var f in model.Attachments)
                    {
                        if (f == null || f.Length == 0) continue;
                        var webPath = await SaveFile(f, "CE_Attach");
                        await conn.ExecuteAsync(insertAttachSql, new
                        {
                            RequestId = requestId,
                            FilePath = webPath,
                            FileName = f.FileName,
                            ContentType = f.ContentType
                        });
                    }
                }

                Success("Cheque encashment request submitted. Please complete the Official Use section.");
                return RedirectToAction(nameof(OnboardingWizard), new { step = 3, requestId = requestId });
            }
            catch (Exception ex)
            {
                    var wizardReturnUrl = returnUrl;
                    var isWizard = string.Equals(WizardSource, "OnboardingWizard", StringComparison.OrdinalIgnoreCase)
                                   || (!string.IsNullOrWhiteSpace(wizardReturnUrl)
                                       && wizardReturnUrl.Contains("OnboardingWizard", StringComparison.OrdinalIgnoreCase)
                                       && (Url.IsLocalUrl(wizardReturnUrl) || Uri.TryCreate(wizardReturnUrl, UriKind.Absolute, out var parsedUri2) && (parsedUri2.IsLoopback || string.Equals(parsedUri2.Host, Request.Host.Host, StringComparison.OrdinalIgnoreCase))));

                    ModelState.AddModelError("", "Failed to save cheque encashment: " + ex.Message);
                    await WriteDiagnosticsAsync("Exception while saving cheque encashment: " + ex.Message);
                    if (isWizard)
                    {
                        var vm = new OnboardingWizardViewModel { ChequeEncashment = model };
                        ViewData["WizardStep"] = 2;
                        ViewData["WizardClientId"] = model.ClientId;
                        ViewData["WizardRequestId"] = model.Id;
                        return View("~/Views/Forms/OnboardingWizard.cshtml", vm);
                    }
                    return View(model);
            }
        }

        [HttpGet]
        public async Task<IActionResult> OfficialUse(int? requestId)
        {
            var denied = await CheckOfficialUseAccessAsync(Request.Path + Request.QueryString);
            if (denied != null) return denied;

            var vm = new OnwardsSwift.Core.DTOs.OfficialUseViewModel();
            vm.RequestId = requestId;
            // Optionally prefill some fields from request/clients in future
            return View(vm);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> OfficialUse(OnwardsSwift.Core.DTOs.OfficialUseViewModel model, string? returnUrl)
        {
            var denied = await CheckOfficialUseAccessAsync(Request.Path + Request.QueryString);
            if (denied != null) return denied;

            async Task WriteDiagnosticsAsync(string note)
            {
                try
                {
                    var root = Path.Combine(_webHostEnvironment.ContentRootPath, "App_Data");
                    if (!Directory.Exists(root)) Directory.CreateDirectory(root);
                    var file = Path.Combine(root, "diagnostics-officialuse.log");
                    var sb = new StringBuilder();
                    sb.AppendLine("--- DIAGNOSTIC ENTRY " + DateTime.UtcNow.ToString("o") + " ---");
                    sb.AppendLine(note);
                    sb.AppendLine("RequestId: " + model.RequestId);
                    sb.AppendLine("Form values:");
                    foreach (var k in Request.Form.Keys)
                    {
                        sb.AppendLine($"  {k} = {Request.Form[k]}");
                    }
                    sb.AppendLine("Files:");
                    foreach (var f in Request.Form.Files)
                    {
                        sb.AppendLine($"  {f.Name} -> {f.FileName} ({f.Length} bytes, {f.ContentType})");
                    }
                    if (!ModelState.IsValid)
                    {
                        sb.AppendLine("ModelState errors:");
                        foreach (var kv in ModelState)
                        {
                            if (kv.Value.Errors != null && kv.Value.Errors.Count > 0)
                            {
                                foreach (var e in kv.Value.Errors)
                                {
                                    sb.AppendLine($"  {kv.Key}: {e.ErrorMessage} | Exception: {e.Exception?.Message}");
                                }
                            }
                        }
                    }
                    await System.IO.File.AppendAllTextAsync(file, sb.ToString());
                }
                catch { }
            }

            if (model.RequestId == null || model.RequestId <= 0)
            {
                ModelState.AddModelError(string.Empty, "Unable to save Official Use. Missing onboarding request reference.");
                await WriteDiagnosticsAsync("Missing RequestId in OfficialUse submission.");
                if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
                {
                    Error("Unable to save Official Use details. Please return to Step 2 and continue from there.");
                    return Redirect(returnUrl);
                }
                return View(model);
            }

            if (!ModelState.IsValid)
            {
                await WriteDiagnosticsAsync("OfficialUse model state invalid on save.");
                var errorMessage = GetModelStateErrors();
                var isWizard = !string.IsNullOrWhiteSpace(returnUrl)
                               && Url.IsLocalUrl(returnUrl)
                               && returnUrl.Contains("OnboardingWizard", StringComparison.OrdinalIgnoreCase);
                if (isWizard)
                {
                    ModelState.AddModelError(string.Empty, errorMessage);
                    var vm = new OnboardingWizardViewModel { OfficialUse = model };
                    ViewData["WizardStep"] = 3;
                    ViewData["WizardClientId"] = null;
                    ViewData["WizardRequestId"] = model.RequestId;
                    return View("~/Views/Forms/OnboardingWizard.cshtml", vm);
                }

                ModelState.AddModelError(string.Empty, errorMessage);
                return View(model);
            }

            try
            {
                if (model.CheckedSignatureFile != null && model.CheckedSignatureFile.Length > 0)
                {
                    model.CheckedSignature = await SaveSignatureFile(model.CheckedSignatureFile, "OfficialUse_Checked");
                }
                if (model.HeadOfTradeSignatureFile != null && model.HeadOfTradeSignatureFile.Length > 0)
                {
                    model.HeadOfTradeSignature = await SaveSignatureFile(model.HeadOfTradeSignatureFile, "OfficialUse_HeadOfTrade");
                }
                if (model.InChargeFinanceSignatureFile != null && model.InChargeFinanceSignatureFile.Length > 0)
                {
                    model.InChargeFinanceSignature = await SaveSignatureFile(model.InChargeFinanceSignatureFile, "OfficialUse_InCharge");
                }
                if (model.CEOSignatureFile != null && model.CEOSignatureFile.Length > 0)
                {
                    model.CEOSignature = await SaveSignatureFile(model.CEOSignatureFile, "OfficialUse_CEO");
                }
                if (model.PaidBySignatureFile != null && model.PaidBySignatureFile.Length > 0)
                {
                    model.PaidBySignature = await SaveSignatureFile(model.PaidBySignatureFile, "OfficialUse_PaidBy");
                }

                using var conn = _ctx.Create();
                await conn.OpenAsync();

                // Only insert if the OfficialUseRecords table exists (applied migration guard)
                var tableExists = await conn.ExecuteScalarAsync<int>(
                    "SELECT CASE WHEN OBJECT_ID('dbo.OfficialUseRecords', 'U') IS NULL THEN 0 ELSE 1 END");

                if (tableExists == 0)
                {
                    await WriteDiagnosticsAsync("OfficialUseRecords table not found while saving OfficialUse.");
                    ModelState.AddModelError(string.Empty, "Unable to save Official Use details because the database table is missing.");
                    var isWizard = !string.IsNullOrWhiteSpace(returnUrl)
                                   && Url.IsLocalUrl(returnUrl)
                                   && returnUrl.Contains("OnboardingWizard", StringComparison.OrdinalIgnoreCase);
                    if (isWizard)
                    {
                        var vm = new OnboardingWizardViewModel { OfficialUse = model };
                        ViewData["WizardStep"] = 3;
                        ViewData["WizardClientId"] = null;
                        ViewData["WizardRequestId"] = model.RequestId;
                        return View("~/Views/Forms/OnboardingWizard.cshtml", vm);
                    }
                    return View(model);
                }

                // SERIALIZABLE: the COUNT below and the INSERT/UPDATE it decides between must be
                // treated as one atomic unit per RequestId -- under READ COMMITTED, two concurrent
                // submissions for the same RequestId could both see existingCount == 0 and both
                // INSERT, leaving two rows for one request. The unique index on RequestId
                // (UX_OfficialUseRecords_RequestId) is the schema-level backstop for that.
                using var tx = conn.BeginTransaction(System.Data.IsolationLevel.Serializable);

                var existingCount = await conn.ExecuteScalarAsync<int>(@"
SELECT COUNT(1) FROM dbo.OfficialUseRecords WHERE RequestId = @RequestId;", new { RequestId = model.RequestId > 0 ? model.RequestId : (int?)null }, tx);

                if (existingCount > 0)
                {
                    var sql = @"
UPDATE dbo.OfficialUseRecords
SET CheckedBy = @CheckedBy,
    CheckedSignature = @CheckedSignature,
    CheckedDate = @CheckedDate,
    ConfirmedWith = @ConfirmedWith,
    Designation = @Designation,
    BuildingStreet = @BuildingStreet,
    DrawerStatus = @DrawerStatus,
    ReasonForPayment = @ReasonForPayment,
    AccountConfirmedBy = @AccountConfirmedBy,
    AccountStatus = @AccountStatus,
    HeadOfTradeFinance = @HeadOfTradeFinance,
    HeadOfTradeSignature = @HeadOfTradeSignature,
    HeadOfTradeDate = @HeadOfTradeDate,
    InChargeFinance = @InChargeFinance,
    InChargeFinanceSignature = @InChargeFinanceSignature,
    InChargeFinanceDate = @InChargeFinanceDate,
    CEO = @CEO,
    CEOSignature = @CEOSignature,
    CEODate = @CEODate,
    PaidByName = @PaidByName,
    PaidBySignature = @PaidBySignature
WHERE RequestId = @RequestId;";

                    await conn.ExecuteAsync(sql, new
                    {
                        RequestId                = model.RequestId > 0 ? model.RequestId : (int?)null,
                        model.CheckedBy,
                        model.CheckedSignature,
                        model.CheckedDate,
                        model.ConfirmedWith,
                        model.Designation,
                        model.BuildingStreet,
                        model.DrawerStatus,
                        model.ReasonForPayment,
                        model.AccountConfirmedBy,
                        model.AccountStatus,
                        model.HeadOfTradeFinance,
                        model.HeadOfTradeSignature,
                        model.HeadOfTradeDate,
                        model.InChargeFinance,
                        model.InChargeFinanceSignature,
                        model.InChargeFinanceDate,
                        model.CEO,
                        model.CEOSignature,
                        model.CEODate,
                        model.PaidByName,
                        model.PaidBySignature
                    }, tx);
                }
                else
                {
                    var sql = @"
INSERT INTO dbo.OfficialUseRecords
    (RequestId, CheckedBy, CheckedSignature, CheckedDate,
     ConfirmedWith, Designation, BuildingStreet, DrawerStatus, ReasonForPayment,
     AccountConfirmedBy, AccountStatus,
     HeadOfTradeFinance, HeadOfTradeSignature, HeadOfTradeDate,
     InChargeFinance, InChargeFinanceSignature, InChargeFinanceDate,
     CEO, CEOSignature, CEODate,
     PaidByName, PaidBySignature,
     CreatedAt, CreatedBy)
VALUES
    (@RequestId, @CheckedBy, @CheckedSignature, @CheckedDate,
     @ConfirmedWith, @Designation, @BuildingStreet, @DrawerStatus, @ReasonForPayment,
     @AccountConfirmedBy, @AccountStatus,
     @HeadOfTradeFinance, @HeadOfTradeSignature, @HeadOfTradeDate,
     @InChargeFinance, @InChargeFinanceSignature, @InChargeFinanceDate,
     @CEO, @CEOSignature, @CEODate,
     @PaidByName, @PaidBySignature,
     GETUTCDATE(), @CreatedBy);";

                    await conn.ExecuteAsync(sql, new
                    {
                        RequestId                = model.RequestId > 0 ? model.RequestId : (int?)null,
                        model.CheckedBy,
                        model.CheckedSignature,
                        model.CheckedDate,
                        model.ConfirmedWith,
                        model.Designation,
                        model.BuildingStreet,
                        model.DrawerStatus,
                        model.ReasonForPayment,
                        model.AccountConfirmedBy,
                        model.AccountStatus,
                        model.HeadOfTradeFinance,
                        model.HeadOfTradeSignature,
                        model.HeadOfTradeDate,
                        model.InChargeFinance,
                        model.InChargeFinanceSignature,
                        model.InChargeFinanceDate,
                        model.CEO,
                        model.CEOSignature,
                        model.CEODate,
                        model.PaidByName,
                        model.PaidBySignature,
                        CreatedBy = CurrentUserEmail
                    }, tx);
                }

                tx.Commit();
            }
            catch (Exception ex)
            {
                await WriteDiagnosticsAsync("Exception while saving OfficialUse: " + ex.Message);
                ModelState.AddModelError(string.Empty, "Failed to save Official Use details. Please try again.");
                var isWizard = !string.IsNullOrWhiteSpace(returnUrl)
                               && Url.IsLocalUrl(returnUrl)
                               && returnUrl.Contains("OnboardingWizard", StringComparison.OrdinalIgnoreCase);
                if (isWizard)
                {
                    var vm = new OnboardingWizardViewModel { OfficialUse = model };
                    ViewData["WizardStep"] = 3;
                    ViewData["WizardClientId"] = null;
                    ViewData["WizardRequestId"] = model.RequestId;
                    return View("~/Views/Forms/OnboardingWizard.cshtml", vm);
                }

                return RedirectToAction("Landing", "Forms");
            }

            Success("Your onboarding request has been submitted successfully.");

            if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl))
            {
                return Redirect(returnUrl);
            }

            return RedirectToAction("Landing", "Forms");
        }

        private string GetModelStateErrors()
        {
            var errors = ModelState
                .SelectMany(kvp => kvp.Value.Errors.Select(error => new {
                    Key = kvp.Key,
                    Message = string.IsNullOrWhiteSpace(error.ErrorMessage) ? error.Exception?.Message : error.ErrorMessage
                }))
                .Where(x => !string.IsNullOrWhiteSpace(x.Message))
                .Select(x => string.IsNullOrWhiteSpace(x.Key) ? x.Message : $"{x.Key}: {x.Message}")
                .ToList();

            if (!errors.Any())
            {
                return "Official Use validation failed. Please check all required fields and try again.";
            }

            var summary = string.Join("; ", errors.Take(5));
            if (errors.Count > 5)
            {
                summary += $" (+{errors.Count - 5} more errors)";
            }

            return summary;
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult GenerateOfficialUse(int? requestId)
        {
            // Redirect to the OfficialUse view so user can print/save the official document
            return RedirectToAction(nameof(OfficialUse), new { requestId });
        }

        // Helper method to save uploaded files (similar to ClientsController.SaveFile)
        private async Task<string?> SaveFile(IFormFile? file, string prefix)
        {
            if (file == null || file.Length == 0) return null;

            try
            {
                var uploadsRootSetting = _configuration["FileStorage:UploadsRoot"] ?? Path.Combine("wwwroot", "uploads");
                var uploadsRootPath = Path.IsPathRooted(uploadsRootSetting)
                    ? uploadsRootSetting
                    : Path.Combine(_webHostEnvironment.ContentRootPath, uploadsRootSetting);

                var uploadsFolder = Path.Combine(uploadsRootPath, "cheques");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{prefix}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var webPath = $"/uploads/cheques/{fileName}";
                return webPath;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save file '{prefix}': {ex.Message}", ex);
            }
        }

        private async Task<string?> SaveBondFile(IFormFile? file, string prefix)
        {
            if (file == null || file.Length == 0) return null;

            try
            {
                var uploadsRootSetting = _configuration["FileStorage:UploadsRoot"] ?? Path.Combine("wwwroot", "uploads");
                var uploadsRootPath = Path.IsPathRooted(uploadsRootSetting)
                    ? uploadsRootSetting
                    : Path.Combine(_webHostEnvironment.ContentRootPath, uploadsRootSetting);

                var uploadsFolder = Path.Combine(uploadsRootPath, "bonds");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{prefix}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                return $"/uploads/bonds/{fileName}";
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save file '{prefix}': {ex.Message}", ex);
            }
        }

        private async Task<string?> SaveSignatureFile(IFormFile? file, string prefix)
        {
            if (file == null || file.Length == 0) return null;

            try
            {
                var uploadsRootSetting = _configuration["FileStorage:UploadsRoot"] ?? Path.Combine("wwwroot", "uploads");
                var uploadsRootPath = Path.IsPathRooted(uploadsRootSetting)
                    ? uploadsRootSetting
                    : Path.Combine(_webHostEnvironment.ContentRootPath, uploadsRootSetting);

                var uploadsFolder = Path.Combine(uploadsRootPath, "signatures");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                var extension = Path.GetExtension(file.FileName);
                var fileName = $"{prefix}_{Guid.NewGuid()}{extension}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                return $"/uploads/signatures/{fileName}";
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save signature file '{prefix}': {ex.Message}", ex);
            }
        }
    }
}
