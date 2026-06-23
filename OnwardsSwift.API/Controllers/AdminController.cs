using System.Security.Cryptography;
using System.Text;
using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.Data.SqlClient;
using Microsoft.DotNet.Scaffolding.Shared.Messaging;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Enums;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.API.Controllers
{
    public class AdminController : AppController
    {
        private readonly DapperContext _ctx;
        private readonly IConfiguration _configuration;
        private readonly OnwardsSwift.Infrastructure.Services.PermissionService _permissions;
        private readonly OnwardsSwift.Infrastructure.Services.ApplicationErrorLogger _errorLogger;
        public AdminController(DapperContext ctx, IConfiguration configuration, OnwardsSwift.Infrastructure.Services.PermissionService permissions, OnwardsSwift.Infrastructure.Services.ApplicationErrorLogger errorLogger)
        {
            _ctx = ctx;
            _configuration = configuration;
            _permissions = permissions;
            _errorLogger = errorLogger;

        }

        public async Task<IActionResult> Errors()
        {
            var rows = await _errorLogger.GetRecentAsync(200);
            return View(rows);
        }
        private SqlConnection GetConnection() => new SqlConnection(_configuration.GetConnectionString("DefaultConnection"));

        public async Task<IActionResult> Users(string? search, int page = 1)
        {
            using var conn = _ctx.Create();
            var w     = string.IsNullOrWhiteSpace(search) ? "IsDeleted=0" : "IsDeleted=0 AND (FullName LIKE @S OR Email LIKE @S)";
            var total = await conn.ExecuteScalarAsync<int>($"SELECT COUNT(*) FROM SystemUsers WHERE {w}", new { S=$"%{search}%" });
            var rows  = await conn.QueryAsync<dynamic>($@"
SELECT u.Id, u.FullName, u.Email, u.Phone, u.Role, u.Department, ISNULL(u.CommissionPercent,0) AS CommissionPercent,
       u.IsActive, u.LastLoginAt, u.CreatedAt,
       CASE WHEN up.Id IS NULL THEN 0 ELSE 1 END AS HasMobileLogin
FROM SystemUsers u
LEFT JOIN UserPermissions up ON up.UserId = u.Id
    AND up.PermissionId = (SELECT Id FROM Permissions WHERE Code = @MobileLoginCode)
WHERE {w}
ORDER BY u.FullName OFFSET @Skip ROWS FETCH NEXT 20 ROWS ONLY",
                new { S=$"%{search}%", Skip=(page-1)*20, MobileLoginCode = OnwardsSwift.Infrastructure.Services.PermissionService.MobileLogin });
            ViewBag.Search = search; ViewBag.Page = page; ViewBag.Total = total;
            return View(rows.ToList());
        }

        // Mobile app sign-in is gated per-client (Role = Client) via dbo.UserPermissions /
        // PermissionService.MobileLogin -- a new signup cannot use their PIN until granted here.
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> GrantMobileAccess(int id)
        {
            await _permissions.GrantUserPermissionAsync(id, OnwardsSwift.Infrastructure.Services.PermissionService.MobileLogin, CurrentUserEmail);
            Success("Mobile app access granted.");
            return RedirectToAction(nameof(Users));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RevokeMobileAccess(int id)
        {
            await _permissions.RevokeUserPermissionAsync(id, OnwardsSwift.Infrastructure.Services.PermissionService.MobileLogin);
            Success("Mobile app access revoked.");
            return RedirectToAction(nameof(Users));
        }

        [HttpGet]
        public IActionResult CreateUser()
        {
            ViewBag.Roles = RoleList();
            return View();
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateUser(string fullName, string email, string password, string role, string? phone, string? department, decimal commissionPercent)
        {
            using var conn = _ctx.Create();
            var exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SystemUsers WHERE Email=@E AND IsDeleted=0", new { E=email });
            if (exists > 0) { Error("Email already exists."); ViewBag.Roles = RoleList(); return View(); }
            if (!Enum.TryParse<UserRole>(role, out var roleEnum)) { Error("Invalid role."); ViewBag.Roles = RoleList(); return View(); }
            await conn.ExecuteAsync("INSERT INTO SystemUsers(FullName,Email,Phone,Role,Department,CommissionPercent,IsActive,PasswordHash,CreatedAt,CreatedBy,IsDeleted) VALUES(@Fn,@Em,@Ph,@Role,@Dept,@CommissionPercent,1,@Pw,GETUTCDATE(),@By,0)",
                new { Fn=fullName, Em=email, Ph=phone, Role=(int)roleEnum, Dept=department, CommissionPercent = commissionPercent, Pw=Hash(password), By=CurrentUserEmail });
            Success($"User '{fullName}' created.");
            return RedirectToAction(nameof(Users));
        }

        [HttpGet]
        public async Task<IActionResult> EditUser(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return NotFound();
            using var conn = _ctx.Create();
            var r = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT Id,FullName,Email,Phone,Role,Department,ISNULL(CommissionPercent,0) AS CommissionPercent,IsActive FROM SystemUsers WHERE CAST(Id AS NVARCHAR(50))=@Id AND IsDeleted=0", new { Id=id });
            if (r == null) return NotFound();
            ViewBag.Roles = RoleList(); return View(r);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditUser(string id, string fullName, string? phone, string role, string? department, decimal commissionPercent, bool isActive)
        {
            if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Users));
            if (!Enum.TryParse<UserRole>(role, out var roleEnum)) { Error("Invalid role."); return RedirectToAction(nameof(Users)); }
            using var conn = _ctx.Create();
            await conn.ExecuteAsync("UPDATE SystemUsers SET FullName=@Fn,Phone=@Ph,Role=@Role,Department=@Dept,CommissionPercent=@CommissionPercent,IsActive=@Active,UpdatedAt=GETUTCDATE(),UpdatedBy=@By WHERE CAST(Id AS NVARCHAR(50))=@Id",
                new { Id=id, Fn=fullName, Ph=phone, Role=(int)roleEnum, Dept=department, CommissionPercent = commissionPercent, Active=isActive, By=CurrentUserEmail });
            Success("User updated."); return RedirectToAction(nameof(Users));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ResetPassword(string id, string newPassword)
        {
            if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Users));
            using var conn = _ctx.Create();
            await conn.ExecuteAsync("UPDATE SystemUsers SET PasswordHash=@Pw,UpdatedAt=GETUTCDATE() WHERE CAST(Id AS NVARCHAR(50))=@Id",
                new { Id=id, Pw=Hash(newPassword) });
            Success("Password reset."); return RedirectToAction(nameof(Users));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> ToggleUser(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return RedirectToAction(nameof(Users));
            using var conn = _ctx.Create();
            var cur = await conn.ExecuteScalarAsync<bool?>("SELECT IsActive FROM SystemUsers WHERE CAST(Id AS NVARCHAR(50))=@Id", new { Id=id });
            if (cur == null) return RedirectToAction(nameof(Users));
            await conn.ExecuteAsync("UPDATE SystemUsers SET IsActive=@V,UpdatedAt=GETUTCDATE() WHERE CAST(Id AS NVARCHAR(50))=@Id", new { Id=id, V=!cur });
            Success(cur.Value ? "User deactivated." : "User activated.");
            return RedirectToAction(nameof(Users));
        }


        public async Task<IActionResult> Banks()
        {
            using var conn = _ctx.Create();
            var banks = await conn.QueryAsync<dynamic>("SELECT Id,BankName,ISNULL(BankType,1) AS InstitutionType,ContactEmail,ContactPhone,IsActive FROM Banks WHERE IsDeleted=0 ORDER BY BankName  ");
            return View(banks.ToList());
        }

        [HttpGet]
        public IActionResult CreateBank() { ViewBag.InstTypes = InstTypeList(); return View(); }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateBank(string name, string shortCode, string institutionType, string? contactEmail, string? contactPhone)
        {
            var itype = ParseInstType(institutionType);
            using var conn = _ctx.Create();

            try
            {
                // Added @Ph to the SQL string and ensured the mapping matches your object
                const string sql = @"
            INSERT INTO Banks (BankName, BankCode, BankType, ContactEmail, ContactPhone, IsActive, CreatedBy) 
            VALUES (@N, @Sc, @IT, @Em, @Ph, 1, @By)";

                await conn.ExecuteAsync(sql, new
                {
                    N = name,
                    Sc = shortCode.ToUpperInvariant().Trim(),
                    IT = itype,
                    Em = contactEmail ?? "",
                    Ph = contactPhone ?? "",
                    By = CurrentUserEmail
                });

                Success($"Bank '{name}' added successfully.");
                return RedirectToAction(nameof(Banks));
            }
            catch (Exception ex)
            {
                // Use a generic Exception unless 'Message' is a custom type in your project
                Success($"Error: {ex.Message}");

                // Return to the view so the user doesn't lose their typed data
                return View();
            }
        }

        [HttpGet]
        public async Task<IActionResult> EditBank(Guid id)
        {
            using var conn = _ctx.Create();
            var b = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT Id,Name,ShortCode,ISNULL(InstitutionType,1) AS InstitutionType,LicenceNumber,ContactEmail,ContactPhone FROM BankPartners WHERE Id=@Id AND IsDeleted=0", new { Id=id });
            if (b == null) return NotFound();
            ViewBag.InstTypes = InstTypeList(); return View(b);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditBank(Guid id, string name, string shortCode, string institutionType, string? licenceNumber, string? contactEmail, string? contactPhone)
        {
            var itype = ParseInstType(institutionType);
            using var conn = _ctx.Create();
            try
            {
                await conn.ExecuteAsync("UPDATE BankPartners SET Name=@N,ShortCode=@Sc,InstitutionType=@IT,LicenceNumber=@Ln,ContactEmail=@Em,ContactPhone=@Ph,UpdatedAt=GETUTCDATE(),UpdatedBy=@By WHERE Id=@Id",
                    new { Id=id, N=name, Sc=shortCode.ToUpperInvariant(), IT=itype, Ln=licenceNumber, Em=contactEmail??"", Ph=contactPhone??"", By=CurrentUserEmail });
            }
            catch
            {
                await conn.ExecuteAsync("UPDATE BankPartners SET Name=@N,ShortCode=@Sc,ContactEmail=@Em,ContactPhone=@Ph,UpdatedAt=GETUTCDATE(),UpdatedBy=@By WHERE Id=@Id",
                    new { Id=id, N=name, Sc=shortCode.ToUpperInvariant(), Em=contactEmail??"", Ph=contactPhone??"", By=CurrentUserEmail });
            }
            Success("Bank updated."); return RedirectToAction(nameof(Banks));
        }



        private async Task<IEnumerable<SelectListItem>> GetProductListAsync(int? categoryId = null)
        {
            using var conn = _ctx.Create();

            // Removed "WHERE IsDeleted = 0" because that column doesn't exist in ProductTypes
            string sql = "SELECT Id, ProductName FROM ProductTypes WHERE 1=1";

            if (categoryId.HasValue)
            {
                sql += " AND ProductType = @Pt";
            }

            sql += " ORDER BY ProductName";

            var products = await conn.QueryAsync<dynamic>(sql, new { Pt = categoryId });

            return products.Select(p => new SelectListItem
            {
                Text = p.ProductName,
                Value = p.Id.ToString()
            });
        }

        // 2. READ: List all rates
        public async Task<IActionResult> Rates()
        {
            using var conn = _ctx.Create();
            var rows = await conn.QueryAsync<dynamic>(@"
        SELECT r.*, ISNULL(b.BankName,'System Default') AS BankName, p.ProductName 
        FROM ProductRates r 
        LEFT JOIN Banks b ON b.Id = r.BankPartnerId 
        LEFT JOIN ProductTypes p ON p.Id = r.ProductType
        WHERE r.IsDeleted = 0 
        ORDER BY p.ProductName");
            return View(rows.ToList());
        }

        // 3. CREATE: Get
        [HttpGet]
        public async Task<IActionResult> CreateRate()
        {
            await PopulateBanks(); // Your existing helper
            ViewBag.ProductTypes = await GetProductListAsync();
            ViewBag.CommTypes = CommTypeList();
            return View();
        }

        // 4. CREATE: Post
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> CreateRate(
         int bankPartnerId,
         int productType,
         decimal rate,
         decimal commission,
         int commissionType,
         decimal applicationFee,
         int applicationFeeType)
        {
            using var conn = _ctx.Create();

            await conn.ExecuteAsync(@"
        INSERT INTO ProductRates (
            BankPartnerId, ProductType, Rate, Commission, 
            CommissionType, ApplicationFee, ApplicationFeeType, 
            IsActive, CreatedAt, CreatedBy, IsDeleted
        )
        VALUES (
            @bankPartnerId, @productType, @rate, @commission, 
            @commissionType, @applicationFee, @applicationFeeType, 
            1, GETUTCDATE(), @By, 0
        )",
                new
                {
                    bankPartnerId,
                    productType,
                    rate = rate / 100m, // Converts e.g. 2.5 to 0.025
                    commission,
                    commissionType,
                    applicationFee,
                    applicationFeeType,
                    By = CurrentUserEmail
                });

            Success("Rate configuration created.");
            return RedirectToAction(nameof(Rates));
        }
        // 5. EDIT: Get
        [HttpGet]
        public async Task<IActionResult> EditRate(int id)
        {
            using var conn = _ctx.Create();
            var rate = await conn.QuerySingleOrDefaultAsync<dynamic>("SELECT * FROM ProductRates WHERE Id = @Id", new { Id = id });
            if (rate == null) return NotFound();

            await PopulateBanks();
            ViewBag.ProductTypes = await GetProductListAsync();
            ViewBag.CommTypes = CommTypeList();
            return View(rate);
        }

        // 6. EDIT: Post
        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> EditRate(
    int id,
    int bankPartnerId,
    int productType,
    decimal rate,
    decimal commission,
    int commissionType,
    decimal applicationFee,
    int applicationFeeType,
    int isActive)
        {
            using var conn = _ctx.Create();
            await conn.ExecuteAsync(@"
        UPDATE ProductRates SET 
            BankPartnerId = @bankPartnerId, 
            ProductType = @productType, 
            Rate = @rate, 
            Commission = @commission, 
            CommissionType = @commissionType, 
            ApplicationFee = @applicationFee, 
            ApplicationFeeType = @applicationFeeType, 
            IsActive = @isActive, 
            UpdatedAt = GETUTCDATE() 
        WHERE Id = @id",
                new
                {
                    id,
                    bankPartnerId,
                    productType,
                    rate = rate / 100m,
                    commission,
                    commissionType,
                    applicationFee,
                    applicationFeeType,
                    isActive
                });

            Success("Rate updated successfully.");
            return RedirectToAction(nameof(Rates));
        }





        public async Task<IActionResult> Settings()
        {
            using var conn = _ctx.Create();
            try
            {
                var rows = await conn.QueryAsync<dynamic>("SELECT SettingKey,SettingValue FROM SystemSettings ORDER BY SettingKey");
                var dict = rows.ToDictionary(r => (string)r.SettingKey, r => (string)r.SettingValue);
                return View(dict);
            }
            catch { return View(new Dictionary<string, string>()); }
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Settings(IFormCollection form)
        {
            using var conn = _ctx.Create();
            foreach (var key in form.Keys.Where(k => k != "__RequestVerificationToken"))
            {
                var val    = form[key].ToString();
                var exists = await conn.ExecuteScalarAsync<int>("SELECT COUNT(*) FROM SystemSettings WHERE SettingKey=@K", new { K=key });
                if (exists > 0)
                    await conn.ExecuteAsync("UPDATE SystemSettings SET SettingValue=@V,UpdatedAt=GETUTCDATE(),UpdatedBy=@By WHERE SettingKey=@K", new { K=key, V=val, By=CurrentUserEmail });
                else
                    await conn.ExecuteAsync("INSERT INTO SystemSettings(Id,SettingKey,SettingValue,CreatedAt,CreatedBy) VALUES(NEWID(),@K,@V,GETUTCDATE(),@By)", new { K=key, V=val, By=CurrentUserEmail });
            }
            Success("Settings saved."); return RedirectToAction(nameof(Settings));
        }

   
        public async Task<IActionResult> InternalBanks()
        {
            using var db = GetConnection();
            var banks = await db.QueryAsync("SELECT * FROM InternalBanks ORDER BY BankName");

            // Explicitly tell ASP.NET to look for "InternalBanksList.cshtml"
            return View("InternalBanksList", banks);
        }

        [HttpGet]
        public IActionResult CreateInternalBank()
        {
            // If you need to check session here as well:
            if (HttpContext.Session.GetInt32("UserId") == null)
            {
                return RedirectToAction("Login", "Accounts");
            }

            return View();
        }



        [HttpPost]
        public async Task<IActionResult> CreateInternalBank(string BankName, string AccountNumber, string PaybillNo)
        {
            using var db = GetConnection();
            var sql = @"INSERT INTO InternalBanks (BankName, AccountNumber, PaybillNo, CreatedBy) 
                    VALUES (@BankName, @AccountNumber, @PaybillNo, @CreatedBy)";

            await db.ExecuteAsync(sql, new
            {
                BankName,
                AccountNumber,
                PaybillNo,
                CreatedBy = HttpContext.Session.GetInt32("UserId") ?? 0
            });

            return RedirectToAction(nameof(InternalBanks));
        }

        // --- 3. EDIT BANK (GET & POST) ---
        public async Task<IActionResult> EditInternalBank(int id)
        {
            using var db = GetConnection();
            var bank = await db.QuerySingleOrDefaultAsync("SELECT * FROM InternalBanks WHERE Id = @id", new { id });
            if (bank == null) return NotFound();
            return View(InternalBanks);
        }

        [HttpPost]
        public async Task<IActionResult> EditInternalBank(int Id, string BankName, string AccountNumber, string PaybillNo)
        {
            using var db = GetConnection();
            var sql = @"UPDATE InternalBanks SET BankName=@BankName, AccountNumber=@AccountNumber, PaybillNo=@PaybillNo 
                    WHERE Id=@Id";

            await db.ExecuteAsync(sql, new { Id, BankName, AccountNumber, PaybillNo });
            return RedirectToAction(nameof(InternalBanks));
        }

        // --- 4. DELETE BANK ---
        [HttpPost]
        public async Task<IActionResult> DeleteInternalBank(int id)
        {
            using var db = GetConnection();
            await db.ExecuteAsync("DELETE FROM InternalBanks WHERE Id = @id", new { id });
            return RedirectToAction(nameof(InternalBanks));
        }
        // GET: Admin/CreateMenu
        public async Task<IActionResult> CreateMenu()
        {
            using var conn = _ctx.Create();
            // Fetch only Top-Level menus to populate the "Parent Menu" dropdown
            var parents = await conn.QueryAsync<NavMenu>("SELECT Id, Title FROM SystemMenus WHERE ParentId IS NULL");
            ViewBag.Parents = parents;
            return View();
        }

        // POST: Admin/CreateMenu
        [HttpPost]
        public async Task<IActionResult> CreateMenu(NavMenu model)
        {
            using var conn = _ctx.Create();
            const string sql = @"
        INSERT INTO SystemMenus (ParentId, Title, Icon, Controller, Action, SortOrder, IsActive, AllowedUserIds)
        VALUES (@ParentId, @Title, @Icon, @Controller, @Action, @SortOrder, @IsActive, @AllowedUserIds)";

            await conn.ExecuteAsync(sql, model);
            TempData["Success"] = "New menu item added successfully!";
            return RedirectToAction("CreateMenu");
        }
        // GET: Admin/ManageMenus
        public async Task<IActionResult> ManageMenus()
        {
            using var conn = _ctx.Create();
            // Fetch all menus ordered by parent and sort order
            var menus = await conn.QueryAsync<NavMenu>("SELECT * FROM SystemMenus ORDER BY ISNULL(ParentId, Id), ParentId, SortOrder");
            return View(menus.ToList());
        }

        // GET: Admin/EditMenu/5
        public async Task<IActionResult> EditMenu(int id)
        {
            using var conn = _ctx.Create();
            var menu = await conn.QueryFirstOrDefaultAsync<NavMenu>("SELECT * FROM SystemMenus WHERE Id = @id", new { id });

            if (menu == null) return NotFound();

            var parents = await conn.QueryAsync<NavMenu>("SELECT Id, Title FROM SystemMenus WHERE ParentId IS NULL AND Id <> @id", new { id });
            ViewBag.ParentMenus = parents;

            return View(menu);
        }

        // POST: Admin/EditMenu
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditMenu(NavMenu model)
        {
            using var conn = _ctx.Create();
            const string sql = @"
        UPDATE SystemMenus 
        SET ParentId = @ParentId, 
            Title = @Title, 
            Icon = @Icon, 
            Controller = @Controller, 
            Action = @Action, 
            SortOrder = @SortOrder, 
            IsActive = @IsActive, 
            AllowedUserIds = @AllowedUserIds
        WHERE Id = @Id";

            await conn.ExecuteAsync(sql, model);
            TempData["Success"] = "Menu updated successfully!";
            return RedirectToAction("ManageMenus");
        }

        // Product Types -- required documents per type, consumed by the mobile app's Bond
        // Application wizard (GET /api/bonds/types) so the checkbox list and the documents a
        // client must upload for each bond type are configured here instead of hardcoded.
        public async Task<IActionResult> ProductTypes()
        {
            using var conn = _ctx.Create();
            var rows = await conn.QueryAsync<dynamic>(@"
SELECT pt.Id, pt.ProductName, pt.ProductType,
       (SELECT COUNT(1) FROM dbo.ProductTypeDocuments d WHERE d.ProductTypeId = pt.Id) AS DocumentCount
FROM dbo.ProductTypes pt
ORDER BY pt.ProductName;");
            return View(rows.ToList());
        }

        public async Task<IActionResult> ProductTypeDocuments(int id)
        {
            using var conn = _ctx.Create();
            var productType = await conn.QuerySingleOrDefaultAsync<dynamic>(
                "SELECT Id, ProductName FROM dbo.ProductTypes WHERE Id = @id", new { id });
            if (productType == null) return RedirectToAction(nameof(ProductTypes));

            var documents = await conn.QueryAsync<dynamic>(@"
SELECT Id, ProductTypeId, DocumentKey, Label, Description, Required, SortOrder
FROM dbo.ProductTypeDocuments
WHERE ProductTypeId = @id
ORDER BY SortOrder, Id;", new { id });

            ViewBag.ProductType = productType;
            return View(documents.ToList());
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddProductTypeDocument(int productTypeId, string documentKey, string label, string? description, bool required, int sortOrder)
        {
            if (string.IsNullOrWhiteSpace(documentKey) || string.IsNullOrWhiteSpace(label))
            {
                Error("Document key and label are required.");
                return RedirectToAction(nameof(ProductTypeDocuments), new { id = productTypeId });
            }

            using var conn = _ctx.Create();
            try
            {
                await conn.ExecuteAsync(@"
INSERT INTO dbo.ProductTypeDocuments (ProductTypeId, DocumentKey, Label, Description, Required, SortOrder)
VALUES (@productTypeId, @documentKey, @label, @description, @required, @sortOrder);",
                    new { productTypeId, documentKey = documentKey.Trim(), label = label.Trim(), description, required, sortOrder });
                Success("Document requirement added.");
            }
            catch (Exception ex)
            {
                Error("Could not add document: " + ex.Message);
            }

            return RedirectToAction(nameof(ProductTypeDocuments), new { id = productTypeId });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteProductTypeDocument(int id, int productTypeId)
        {
            using var conn = _ctx.Create();
            await conn.ExecuteAsync("DELETE FROM dbo.ProductTypeDocuments WHERE Id = @id", new { id });
            Success("Document requirement removed.");
            return RedirectToAction(nameof(ProductTypeDocuments), new { id = productTypeId });
        }

        private async Task PopulateBanks()
        {
            using var conn = _ctx.Create();
            var banks = await conn.QueryAsync<dynamic>("SELECT Id,BankName as Name FROM Banks WHERE IsDeleted=0 AND IsActive=1 ORDER BY BankName");
            ViewBag.Banks = new[] { new SelectListItem("System Default (all partners)", "") }
                .Concat(banks.Select(b => new SelectListItem((string)b.Name, ((int)b.Id).ToString()))).ToList();
        }

        private static int ParseInstType(string s) =>
            Enum.TryParse<InstitutionType>(s, true, out var v) ? (int)v : 1;

        private static IEnumerable<SelectListItem> RoleList() =>
            Enum.GetValues<UserRole>().Select(r => new SelectListItem(r.ToString(), r.ToString()));

        private static IEnumerable<SelectListItem> InstTypeList() =>
            Enum.GetValues<InstitutionType>().Select(v => new SelectListItem(v.ToString(), v.ToString()));

        private async Task<IEnumerable<SelectListItem>> GetProductListAsync(int typeId)
        {
            using var conn = _ctx.Create();
            // Querying the ProductTypes table based on the ProductType category
            var products = await conn.QueryAsync<dynamic>(
                "SELECT Id, ProductName FROM ProductTypes WHERE ProductType = @Pt ORDER BY ProductName",
                new { Pt = typeId }
            );

            return products.Select(p => new SelectListItem
            {
                Text = p.ProductName,
                Value = p.Id.ToString()
            });
        }

        private static IEnumerable<SelectListItem> CommTypeList() =>
            Enum.GetValues<CommissionType>().Select(v => new SelectListItem(v.ToString(), ((int)v).ToString()));

        private static string Hash(string pw)
        {
            using var sha = SHA256.Create();
            return Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(pw + "OnwardsSwiftSalt2026")));
        }
    }
}
