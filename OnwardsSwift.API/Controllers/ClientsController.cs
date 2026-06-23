using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.API.Controllers
{
    public class ClientsController : AppController
    {
        private readonly IClientService _clients;
        private readonly DapperContext _ctx;
        private readonly IWebHostEnvironment _webHostEnvironment;
        private readonly IConfiguration _configuration;

        public ClientsController(IClientService clients, DapperContext ctx, IWebHostEnvironment webHostEnvironment, IConfiguration configuration)
        {
            _clients = clients;
            _ctx = ctx;
            _webHostEnvironment = webHostEnvironment;
            _configuration = configuration;
        }

        public async Task<IActionResult> Index(string? search, int page = 1)
        {
            var result = await _clients.GetAllAsync(page, 20, search);
            ViewBag.Search = search;
            return View(result);
        }

        [AllowAnonymous]
        [HttpGet]
        public IActionResult Create() => View(new CreateClientRequest());
        [AllowAnonymous]
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateClientRequest model)
        {
            try
            {
                NormalizeClientModel(model);
                var returnUrl = (Request.Query["returnUrl"].FirstOrDefault() ?? Request.Form["returnUrl"].FirstOrDefault());
                var isWizardCreate = !string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) && returnUrl.Contains("OnboardingWizard", StringComparison.OrdinalIgnoreCase);
                if (isWizardCreate && model.Category == 1 && !model.IprsVerified)
                {
                    ModelState.AddModelError(nameof(model.IprsVerified), "Please verify the applicant with IPRS before continuing.");
                }

                if (!ModelState.IsValid)
                {
                    if (isWizardCreate)
                    {
                        var vm = new OnboardingWizardViewModel { NewClient = model };
                        ViewData["WizardStep"] = 1;
                        ViewData["WizardClientId"] = null;
                        ViewData["WizardRequestId"] = null;
                        return View("~/Views/Forms/OnboardingWizard.cshtml", vm);
                    }
                    return View(model);
                }

                string? pathFront = null, pathBack = null, pathPhoto = null, pathReg = null;

                // 1. Conditional File Processing
                if (model.Category == 1) // Individual
                {
                    pathFront = await SaveFile(model.IdFrontFile, "ID_Front");
                    pathBack = await SaveFile(model.IdBackFile, "ID_Back");
                    pathPhoto = await SaveFile(model.PassportPhotoFile, "Passport");
                }
                else // Corporate
                {
                    pathReg = await SaveFile(model.RegCertFile, "Reg_Cert");
                }

                // 2. Database Insertion
                using var conn = _ctx.Create();
                var sql = @"
            INSERT INTO Clients
                (CompanyName, ClientType, IdNumber, KraPin, Category, Gender, 
                 ContactPerson, Email, Phone, PhoneAlt, PhysicalAddress, 
                 PostalAddress, BusinessRegNumber, UtilisedLimit,
                 KycIdFrontPath, KycIdBackPath, KycPassportPhotoPath, KycRegCertPath,
                 CreatedAt, CreatedBy, Status, IsDeleted)
            VALUES
                (@Co, @CType, @IdN, @Kra, @Cat, @Gen, 
                 @Con, @Em, @Ph, @PhA, @Addr, 
                 @Post, @Reg, @Util,
                 @P1, @P2, @P3, @P4,
                 GETUTCDATE(), @By, 1, 0);
            
            SELECT CAST(SCOPE_IDENTITY() as int);";

                var newId = await conn.QuerySingleAsync<int>(sql, new
                {
                    Co = model.CompanyName,
                    CType = (int)model.ClientType,
                    IdN = model.Category == 1 ? model.IdNumber : null,
                    Kra = model.KraPin,
                    Cat = model.Category,
                    Gen = model.Category == 1 ? model.Gender : null,
                    Con = model.ContactPerson,
                    Em = model.Email ?? string.Empty,
                    Ph = model.Phone,
                    PhA = model.PhoneAlt,
                    Addr = model.PhysicalAddress,
                    Post = model.PostalAddress,
                    Reg = model.BusinessRegNumber,
                    Util = 0, // Initial utilized limit is zero
                    P1 = pathFront,
                    P2 = pathBack,
                    P3 = pathPhoto,
                    P4 = pathReg,
                    By = CurrentUserEmail
                });

                Success($"{model.CompanyName} registered successfully.");
                // If a returnUrl is provided (e.g. opened from the onboarding wizard), redirect back to it with the new clientId
                var wizardReturnUrl = (Request.Query["returnUrl"].FirstOrDefault() ?? Request.Form["returnUrl"].FirstOrDefault());
                if (!string.IsNullOrWhiteSpace(wizardReturnUrl) && Url.IsLocalUrl(wizardReturnUrl))
                {
                    var sep = wizardReturnUrl.Contains("?") ? "&" : "?";
                    return Redirect(wizardReturnUrl + sep + "clientId=" + newId);
                }

                return RedirectToAction(nameof(Details), new { id = newId });
            }
            catch (Exception ex)
            {
                if (ex is UnauthorizedAccessException)
                {
                    ModelState.AddModelError("", "System Error: Upload folder is not writable. Contact admin to grant modify permissions to the app identity.");
                }
                else
                {
                    ModelState.AddModelError("", "System Error: " + ex.Message);
                }
                return View(model);
            }
        
        }

        // Helper method inside the Controller
        private async Task<string?> SaveFile(IFormFile? file, string prefix)
        {
            if (file == null || file.Length == 0) return null;

            try
            {
                var uploadsRootSetting = _configuration["FileStorage:UploadsRoot"] ?? Path.Combine("wwwroot", "uploads");
                var uploadsRootPath = Path.IsPathRooted(uploadsRootSetting)
                    ? uploadsRootSetting
                    : Path.Combine(_webHostEnvironment.ContentRootPath, uploadsRootSetting);

                var uploadsFolder = Path.Combine(uploadsRootPath, "kyc");
                if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

                var fileName = $"{prefix}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
                var filePath = Path.Combine(uploadsFolder, fileName);

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await file.CopyToAsync(stream);
                }

                var webPath = $"/uploads/kyc/{fileName}";
                return webPath;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Failed to save file '{prefix}': {ex.Message}", ex);
            }
        }

        public async Task<IActionResult> Details(int id)
        {
            var client = await _clients.GetByIdAsync(id);
            if (client == null) return NotFound();
            var facilities = await _clients.GetClientFacilitiesAsync(id);
            ViewBag.Facilities = facilities;
            return View(client);
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            var client = await _clients.GetByIdAsync(id);
            if (client == null) return NotFound();
            var model = new CreateClientRequest
            {
                Category        = client.Category ?? 1,
                CompanyName     = client.CompanyName,
                KraPin          = client.KraPin,
                IdNumber        = client.IdNumber,
                Gender          = client.Gender,
                ContactPerson   = client.ContactPerson,
                Email           = client.Email,
                Phone           = client.Phone,
                PhoneAlt        = client.PhoneAlt ?? string.Empty,
                ClientType      = (OnwardsSwift.Core.Enums.ClientType)(int.TryParse(client.ClientType, out var ct) ? ct : 1),
                PhysicalAddress = client.PhysicalAddress,
                PostalAddress   = client.PostalAddress ?? string.Empty,
                BusinessRegNumber = client.BusinessRegNumber ?? string.Empty
            };
            ViewBag.ClientId = id;
            ViewBag.ClientName = client.CompanyName;
            ViewBag.KycFiles = new
            {
                IdFrontPath = client.KycIdFrontPath,
                IdBackPath = client.KycIdBackPath,
                PassportPhotoPath = client.KycPassportPhotoPath,
                RegCertPath = client.KycRegCertPath
            };
            return View(model);
        }

        [HttpGet]
        [Route("/api/clients/search")]
        public async Task<IActionResult> ApiSearch(string q)
        {
            if (string.IsNullOrWhiteSpace(q)) return Ok(new object[0]);
            var result = await _clients.GetAllAsync(1, 50, q);
            var list = result.Items.Select(c => new {
                id = c.Id,
                name = c.CompanyName,
                idNumber = c.IdNumber,
                businessRegNumber = c.BusinessRegNumber,
                phone = c.Phone
            });
            return Ok(list);
        }

        [AllowAnonymous]
        [HttpGet]
        [Route("/api/clients/find")]
        public async Task<IActionResult> ApiFind(string? idNumber, string? businessRegNumber)
        {
            if (string.IsNullOrWhiteSpace(idNumber) && string.IsNullOrWhiteSpace(businessRegNumber))
            {
                return BadRequest("Enter ID/passport number or business registration number.");
            }

            using var conn = _ctx.Create();
            var client = await conn.QueryFirstOrDefaultAsync<ClientResponse>(@"
                SELECT TOP 1
                    c.Id,
                    c.CompanyName,
                    CAST(ISNULL(c.ClientType, 1) AS VARCHAR(10)) AS ClientType,
                    ISNULL(c.Category, 1) AS Category,
                    c.Gender,
                    ISNULL(c.KraPin, '') AS KraPin,
                    ISNULL(c.IdNumber, '') AS IdNumber,
                    ISNULL(c.BusinessRegNumber, '') AS BusinessRegNumber,
                    ISNULL(c.ContactPerson, '') AS ContactPerson,
                    ISNULL(c.Email, '') AS Email,
                    ISNULL(c.Phone, '') AS Phone,
                    ISNULL(c.PhoneAlt, '') AS PhoneAlt,
                    ISNULL(c.PhysicalAddress, '') AS PhysicalAddress,
                    ISNULL(c.PostalAddress, '') AS PostalAddress
                FROM Clients c
                WHERE c.IsDeleted = 0
                  AND (
                        (@IdNumber IS NOT NULL AND c.IdNumber = @IdNumber)
                     OR (@BusinessRegNumber IS NOT NULL AND c.BusinessRegNumber = @BusinessRegNumber)
                  )",
                new { IdNumber = string.IsNullOrWhiteSpace(idNumber) ? null : idNumber, BusinessRegNumber = string.IsNullOrWhiteSpace(businessRegNumber) ? null : businessRegNumber });

            if (client == null) return NotFound();
            return Ok(new
            {
                client.Id,
                client.CompanyName,
                client.ClientType,
                client.Category,
                client.Gender,
                client.KraPin,
                client.IdNumber,
                client.BusinessRegNumber,
                client.ContactPerson,
                client.Email,
                client.Phone,
                client.PhoneAlt,
                client.PhysicalAddress,
                client.PostalAddress
            });
        }

        [HttpPost]
        [Route("/api/clients/create")]
        public async Task<IActionResult> ApiCreate([FromBody] OnwardsSwift.Core.DTOs.CreateClientRequest model)
        {
            try
            {
                NormalizeClientModel(model);

                using var conn = _ctx.Create();
                var sql = @"
            INSERT INTO Clients
                (CompanyName, ClientType, IdNumber, KraPin, Category, Gender, 
                 ContactPerson, Email, Phone, PhoneAlt, PhysicalAddress, 
                 PostalAddress, BusinessRegNumber, UtilisedLimit,
                 KycIdFrontPath, KycIdBackPath, KycPassportPhotoPath, KycRegCertPath,
                 CreatedAt, CreatedBy, Status, IsDeleted)
            VALUES
                (@Co, @CType, @IdN, @Kra, @Cat, @Gen, 
                 @Con, @Em, @Ph, @PhA, @Addr, 
                 @Post, @Reg, @Util,
                 @P1, @P2, @P3, @P4,
                 GETUTCDATE(), @By, 1, 0);
            
            SELECT CAST(SCOPE_IDENTITY() as int);";

                var newId = await conn.QuerySingleAsync<int>(sql, new
                {
                    Co = model.CompanyName,
                    CType = (int)model.ClientType,
                    IdN = model.Category == 1 ? model.IdNumber : null,
                    Kra = model.KraPin,
                    Cat = model.Category,
                    Gen = model.Category == 1 ? model.Gender : null,
                    Con = model.ContactPerson,
                    Em = model.Email ?? string.Empty,
                    Ph = model.Phone,
                    PhA = model.PhoneAlt,
                    Addr = model.PhysicalAddress,
                    Post = model.PostalAddress,
                    Reg = model.BusinessRegNumber,
                    Util = 0,
                    P1 = (string?)null,
                    P2 = (string?)null,
                    P3 = (string?)null,
                    P4 = (string?)null,
                    By = CurrentUserEmail
                });

                return Ok(new { id = newId });
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CreateClientRequest model)
        {
            try
            {
                var existingClient = await _clients.GetByIdAsync(id);
                if (existingClient == null) return NotFound();

                // On edit, only validate critical fields. Files are optional.
                // Clear non-critical validation errors to allow file uploads without re-entering everything.
                var keysToRemove = new[]
                {
                    nameof(CreateClientRequest.IdFrontFile),
                    nameof(CreateClientRequest.IdBackFile),
                    nameof(CreateClientRequest.PassportPhotoFile),
                    nameof(CreateClientRequest.RegCertFile),
                    nameof(CreateClientRequest.PhoneAlt),
                    nameof(CreateClientRequest.PostalAddress)
                };
                foreach (var key in keysToRemove)
                    ModelState.Remove(key);

                NormalizeClientModel(model);
                var returnUrl = (Request.Query["returnUrl"].FirstOrDefault() ?? Request.Form["returnUrl"].FirstOrDefault());
                if (!ModelState.IsValid)
                {
                    if (!string.IsNullOrWhiteSpace(returnUrl) && Url.IsLocalUrl(returnUrl) && returnUrl.Contains("OnboardingWizard", StringComparison.OrdinalIgnoreCase))
                    {
                        var vm = new OnboardingWizardViewModel { NewClient = model };
                        ViewData["WizardStep"] = 1;
                        ViewData["WizardClientId"] = id;
                        ViewData["WizardRequestId"] = null;
                        return View("~/Views/Forms/OnboardingWizard.cshtml", vm);
                    }

                    await PopulateEditViewBags(id, model);
                    return View(model);
                }

                string? pathFront = null, pathBack = null, pathPhoto = null, pathReg = null;
                var filesToDelete = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // 1. Conditional File Processing (only if new files are provided)
                try
                {
                    if (model.Category == 1) // Individual
                    {
                        if (model.IdFrontFile != null && model.IdFrontFile.Length > 0)
                            pathFront = await SaveFile(model.IdFrontFile, "ID_Front");
                        if (model.IdBackFile != null && model.IdBackFile.Length > 0)
                            pathBack = await SaveFile(model.IdBackFile, "ID_Back");
                        if (model.PassportPhotoFile != null && model.PassportPhotoFile.Length > 0)
                            pathPhoto = await SaveFile(model.PassportPhotoFile, "Passport");
                    }
                    else // Corporate
                    {
                        if (model.RegCertFile != null && model.RegCertFile.Length > 0)
                            pathReg = await SaveFile(model.RegCertFile, "Reg_Cert");
                    }
                }
                catch (Exception fileEx)
                {
                    ModelState.AddModelError("", $"File upload error: {fileEx.Message}");
                    await PopulateEditViewBags(id, model);
                    return View(model);
                }

                // Keep existing file, replace with new one, or clear when remove is requested.
                var finalPathFront = ResolveUpdatedKycPath(existingClient.KycIdFrontPath, pathFront, model.RemoveIdFrontFile, filesToDelete);
                var finalPathBack = ResolveUpdatedKycPath(existingClient.KycIdBackPath, pathBack, model.RemoveIdBackFile, filesToDelete);
                var finalPathPhoto = ResolveUpdatedKycPath(existingClient.KycPassportPhotoPath, pathPhoto, model.RemovePassportPhotoFile, filesToDelete);
                var finalPathReg = ResolveUpdatedKycPath(existingClient.KycRegCertPath, pathReg, model.RemoveRegCertFile, filesToDelete);

                // 2. Database Update
                using var conn = _ctx.Create();
                var sql = @"
            UPDATE Clients SET
                CompanyName = @Co,
                ClientType = @CType,
                IdNumber = @IdN,
                Category = @Cat,
                Gender = @Gen,
                ContactPerson = @Con,
                Email = @Em,
                Phone = @Ph,
                PhoneAlt = @PhA,
                PhysicalAddress = @Addr,
                PostalAddress = @Post,
                BusinessRegNumber = @Reg,
                KycIdFrontPath = @P1,
                KycIdBackPath = @P2,
                KycPassportPhotoPath = @P3,
                KycRegCertPath = @P4,
                UpdatedAt = GETUTCDATE(),
                UpdatedBy = @By
            WHERE Id = @Id";

                await conn.ExecuteAsync(sql, new
                {
                    Id = id,
                    Co = model.CompanyName,
                    CType = (int)model.ClientType,
                    IdN = model.Category == 1 ? model.IdNumber : null,
                    Cat = model.Category,
                    Gen = model.Category == 1 ? model.Gender : null,
                    Con = model.ContactPerson,
                    Em = model.Email ?? string.Empty,
                    Ph = model.Phone,
                    PhA = model.PhoneAlt,
                    Addr = model.PhysicalAddress,
                    Post = model.PostalAddress,
                    Reg = model.BusinessRegNumber,
                    P1 = finalPathFront,
                    P2 = finalPathBack,
                    P3 = finalPathPhoto,
                    P4 = finalPathReg,
                    By = CurrentUserEmail
                });

                DeleteStoredFiles(filesToDelete);

                Success("Client updated successfully.");
                var wizardReturnUrl = (Request.Query["returnUrl"].FirstOrDefault() ?? Request.Form["returnUrl"].FirstOrDefault());
                if (!string.IsNullOrWhiteSpace(wizardReturnUrl) && Url.IsLocalUrl(wizardReturnUrl))
                {
                    var sep = wizardReturnUrl.Contains("?") ? "&" : "?";
                    return Redirect(wizardReturnUrl + sep + "clientId=" + id);
                }

                return RedirectToAction(nameof(Details), new { id });
            }
            catch (Exception ex)
            {
                if (ex is UnauthorizedAccessException)
                {
                    ModelState.AddModelError("", "System Error: Upload folder is not writable. Contact admin to grant modify permissions to the app identity.");
                }
                else if (ex.Message.Contains("Failed to save file"))
                {
                    ModelState.AddModelError("", $"File upload failed: {ex.InnerException?.Message ?? ex.Message}");
                }
                else
                {
                    ModelState.AddModelError("", "System Error: " + ex.Message);
                }
                await PopulateEditViewBags(id, model);
                return View(model);
            }
        }

        private void NormalizeClientModel(CreateClientRequest model)
        {
            if (model.Category == 1)
            {
                // Individual clients do not use corporate contact details.
                if (string.IsNullOrWhiteSpace(model.ContactPerson))
                    model.ContactPerson = model.CompanyName;

                model.BusinessRegNumber = string.Empty;
                ModelState.Remove(nameof(CreateClientRequest.ContactPerson));
                ModelState.Remove(nameof(CreateClientRequest.BusinessRegNumber));
                ModelState.Remove(nameof(CreateClientRequest.RegCertFile));
            }
            else
            {
                // Corporate clients do not use individual identity fields.
                model.IdNumber = null;
                model.Gender = null;
                ModelState.Remove(nameof(CreateClientRequest.IdNumber));
                ModelState.Remove(nameof(CreateClientRequest.Gender));
                ModelState.Remove(nameof(CreateClientRequest.IdFrontFile));
                ModelState.Remove(nameof(CreateClientRequest.IdBackFile));
                ModelState.Remove(nameof(CreateClientRequest.PassportPhotoFile));
            }
        }

        private async Task PopulateEditViewBags(int id, CreateClientRequest model)
        {
            ViewBag.ClientId = id;
            ViewBag.ClientName = model.CompanyName;

            var client = await _clients.GetByIdAsync(id);
            ViewBag.KycFiles = new
            {
                IdFrontPath = client?.KycIdFrontPath,
                IdBackPath = client?.KycIdBackPath,
                PassportPhotoPath = client?.KycPassportPhotoPath,
                RegCertPath = client?.KycRegCertPath
            };
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStatus(int id, int status, string? reason)
        {
            await _clients.UpdateStatusAsync(id, status, reason, CurrentUserEmail);
            Success("Client status updated.");
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyCrb(int id)
        {
            await _clients.VerifyCrbAsync(id, CurrentUserEmail);
            Success("CRB verification recorded.");
            return RedirectToAction(nameof(Details), new { id });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateCreditLimit(int id, decimal limit)
        {
            await _clients.UpdateCreditLimitAsync(id, limit, CurrentUserEmail);
            Success("Credit limit updated.");
            return RedirectToAction(nameof(Details), new { id });
        }

        private static string? ResolveUpdatedKycPath(string? existingPath, string? uploadedPath, bool removeRequested, ISet<string> filesToDelete)
        {
            if (!string.IsNullOrWhiteSpace(uploadedPath))
            {
                if (!string.IsNullOrWhiteSpace(existingPath) && !string.Equals(existingPath, uploadedPath, StringComparison.OrdinalIgnoreCase))
                {
                    filesToDelete.Add(existingPath);
                }

                return uploadedPath;
            }

            if (removeRequested)
            {
                if (!string.IsNullOrWhiteSpace(existingPath))
                {
                    filesToDelete.Add(existingPath);
                }

                return null;
            }

            return existingPath;
        }

        private void DeleteStoredFiles(IEnumerable<string> webPaths)
        {
            foreach (var webPath in webPaths)
            {
                try
                {
                    TryDeleteStoredFile(webPath);
                }
                catch
                {
                    // File cleanup failure should not fail the edit operation.
                }
            }
        }

        private void TryDeleteStoredFile(string? webPath)
        {
            if (string.IsNullOrWhiteSpace(webPath)) return;

            var normalizedWebPath = webPath.Replace('\\', '/');
            const string uploadsPrefix = "/uploads/";
            if (!normalizedWebPath.StartsWith(uploadsPrefix, StringComparison.OrdinalIgnoreCase)) return;

            var relativePath = normalizedWebPath.Substring(uploadsPrefix.Length).TrimStart('/');
            if (string.IsNullOrWhiteSpace(relativePath)) return;

            var uploadsRootSetting = _configuration["FileStorage:UploadsRoot"] ?? Path.Combine("wwwroot", "uploads");
            var uploadsRootPath = Path.IsPathRooted(uploadsRootSetting)
                ? uploadsRootSetting
                : Path.Combine(_webHostEnvironment.ContentRootPath, uploadsRootSetting);

            var rootFullPath = Path.GetFullPath(uploadsRootPath);
            var fileFullPath = Path.GetFullPath(Path.Combine(rootFullPath, relativePath.Replace('/', Path.DirectorySeparatorChar)));

            if (!fileFullPath.StartsWith(rootFullPath, StringComparison.OrdinalIgnoreCase)) return;
            if (System.IO.File.Exists(fileFullPath)) System.IO.File.Delete(fileFullPath);
        }
    }
}
