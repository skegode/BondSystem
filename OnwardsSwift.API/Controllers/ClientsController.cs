using Dapper;
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

        public ClientsController(IClientService clients, DapperContext ctx, IWebHostEnvironment webHostEnvironment)
        {
            _clients = clients;
            _ctx = ctx;
            _webHostEnvironment = webHostEnvironment;
        }

        public async Task<IActionResult> Index(string? search, int page = 1)
        {
            var result = await _clients.GetAllAsync(page, 20, search);
            ViewBag.Search = search;
            return View(result);
        }

        [HttpGet]
        public IActionResult Create() => View(new CreateClientRequest());
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateClientRequest model)
        {
            try
            {
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
                 PostalAddress, BusinessRegNumber, CreditLimit, UtilisedLimit,
                 KycIdFrontPath, KycIdBackPath, KycPassportPhotoPath, KycRegCertPath,
                 CreatedAt, CreatedBy, Status, IsDeleted)
            VALUES
                (@Co, @CType, @IdN, @Kra, @Cat, @Gen, 
                 @Con, @Em, @Ph, @PhA, @Addr, 
                 @Post, @Reg, @Lim, @Util,
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
                    Em = model.Email,
                    Ph = model.Phone,
                    PhA = model.PhoneAlt,
                    Addr = model.PhysicalAddress,
                    Post = model.PostalAddress,
                    Reg = model.BusinessRegNumber,
                    Lim = model.CreditLimit,
                    Util = 0, // Initial utilized limit is zero
                    P1 = pathFront,
                    P2 = pathBack,
                    P3 = pathPhoto,
                    P4 = pathReg,
                    By = CurrentUserEmail
                });

                Success($"{model.CompanyName} registered successfully.");
                return RedirectToAction(nameof(Details), new { id = newId });
            }
            catch (Exception ex)
            {
                ModelState.AddModelError("", "System Error: " + ex.Message);
                return View(model);
            }
        
        }

        // Helper method inside the Controller
        private async Task<string?> SaveFile(IFormFile? file, string prefix)
        {
            if (file == null || file.Length == 0) return null;

            // Define the path: wwwroot/uploads/kyc
            var uploadsFolder = Path.Combine(_webHostEnvironment.WebRootPath, "uploads", "kyc");
            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);

            var fileName = $"{prefix}_{Guid.NewGuid()}{Path.GetExtension(file.FileName)}";
            var filePath = Path.Combine(uploadsFolder, fileName);

            using (var stream = new FileStream(filePath, FileMode.Create))
            {
                await file.CopyToAsync(stream);
            }

            return $"/uploads/kyc/{fileName}";
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
                CompanyName     = client.CompanyName,
                KraPin          = client.KraPin,
                ContactPerson   = client.ContactPerson,
                Email           = client.Email,
                Phone           = client.Phone,
                PhysicalAddress = client.PhysicalAddress,
                CreditLimit     = client.CreditLimit
            };
            ViewBag.ClientId = id;
            ViewBag.ClientName = client.CompanyName;
            return View(model);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, CreateClientRequest model)
        {
            if (!ModelState.IsValid) { ViewBag.ClientId = id; return View(model); }
            await _clients.UpdateAsync(id, model, CurrentUserEmail);
            Success("Client updated.");
            return RedirectToAction(nameof(Details), new { id });
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
    }
}
