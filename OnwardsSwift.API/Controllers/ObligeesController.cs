using Dapper;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.API.Controllers
{
    public class ObligeesController : AppController
    {
        private readonly DapperContext _ctx;
        public ObligeesController(DapperContext ctx) => _ctx = ctx;

        public async Task<IActionResult> Index(string? search)
        {
            using var conn = _ctx.Create();
            var w    = string.IsNullOrWhiteSpace(search) ? "IsDeleted=0" : "IsDeleted=0 AND (Name LIKE @S OR ShortName LIKE @S)";
            var rows = await conn.QueryAsync<dynamic>($"SELECT Id,Name,ShortName,Category,ContactPerson,Email,Phone FROM Obligees WHERE {w} ORDER BY Name", new { S=$"%{search}%" });
            ViewBag.Search = search;
            return View(rows.ToList());
        }

        [HttpGet]
        public IActionResult Create() => View();

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(string name, string? shortName, string? category, string? contactPerson, string? email, string? phone, string? address)
        {
            if (string.IsNullOrWhiteSpace(name)) { ModelState.AddModelError("", "Name is required."); return View(); }
            using var conn = _ctx.Create();
            await conn.ExecuteAsync("INSERT INTO Obligees(Name,ShortName,Category,ContactPerson,Email,Phone,Address,IsActive,CreatedAt,CreatedBy,IsDeleted) VALUES(@N,@Sn,@Cat,@Con,@Em,@Ph,@Addr,1,GETUTCDATE(),@By,0)",
                new { N=name.Trim(), Sn=shortName?.Trim(), Cat=category??"Government", Con=contactPerson, Em=email, Ph=phone, Addr=address, By=CurrentUserEmail });
            Success($"Obligee '{name}' added.");
            return RedirectToAction(nameof(Index));
        }

        [HttpGet]
        public async Task<IActionResult> Edit(int id)
        {
            using var conn = _ctx.Create();
            var r = await conn.QueryFirstOrDefaultAsync<dynamic>("SELECT Id,Name,ShortName,Category,ContactPerson,Email,Phone,Address FROM Obligees WHERE Id=@Id AND IsDeleted=0", new { Id=id });
            if (r == null) return NotFound();
            return View(r);
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, string name, string? shortName, string? category, string? contactPerson, string? email, string? phone, string? address)
        {
            using var conn = _ctx.Create();
            await conn.ExecuteAsync("UPDATE Obligees SET Name=@N,ShortName=@Sn,Category=@Cat,ContactPerson=@Con,Email=@Em,Phone=@Ph,Address=@Addr,UpdatedAt=GETUTCDATE(),UpdatedBy=@By WHERE Id=@Id",
                new { Id=id, N=name.Trim(), Sn=shortName?.Trim(), Cat=category, Con=contactPerson, Em=email, Ph=phone, Addr=address, By=CurrentUserEmail });
            Success("Obligee updated.");
            return RedirectToAction(nameof(Index));
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            using var conn = _ctx.Create();
            await conn.ExecuteAsync("UPDATE Obligees SET IsDeleted=1,UpdatedAt=GETUTCDATE() WHERE Id=@Id", new { Id=id });
            Success("Obligee deleted.");
            return RedirectToAction(nameof(Index));
        }
    }
}
