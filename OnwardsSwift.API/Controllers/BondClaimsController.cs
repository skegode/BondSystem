using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.API.Controllers
{
    public class BondClaimsController : AppController
    {
        private readonly DapperContext _ctx;
        public BondClaimsController(DapperContext ctx) => _ctx = ctx;

        public async Task<IActionResult> Index()
        {
            using var conn = _ctx.Create();
            try
            {
                var rows = await conn.QueryAsync<dynamic>(
                    "SELECT Id,FacilityId,BondRef,Obligee,PenalSum,ClaimAmount,ClaimDate,ClaimReference,ClaimStatus,Notes FROM BondClaims WHERE IsDeleted=0 ORDER BY ClaimDate DESC");
                return View(rows.ToList());
            }
            catch { return View(new List<dynamic>()); }
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await PopulateFacilities();
            return View(new BondClaimRequest { ClaimDate = DateTime.Today });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(BondClaimRequest model)
        {
            if (!ModelState.IsValid) { await PopulateFacilities(); return View(model); }

            using var conn = _ctx.Create();
            var fac = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT Id,ReferenceNo,ClientId,Amount FROM Facilities WHERE Id=@Id AND IsDeleted=0",
                new { Id = model.FacilityId });
            if (fac == null) { Error("Facility not found."); return View(model); }

            var diff     = model.NextLowestBid.HasValue && model.DefaultingBid.HasValue
                           ? Math.Max(0m, model.NextLowestBid.Value - model.DefaultingBid.Value) : 0m;
            var claimAmt = model.ClaimAmount > 0 ? model.ClaimAmount : Math.Min(diff, (decimal)fac.Amount);

            await conn.ExecuteAsync(@"INSERT INTO BondClaims(Id,FacilityId,BondRef,ClientId,Obligee,PenalSum,DefaultingBid,NextLowestBid,ClaimAmount,ClaimDate,ClaimReference,ClaimStatus,Notes,CreatedAt,CreatedBy,IsDeleted)
                VALUES(@Id,@Fid,@Ref,@Cid,@Obl,@Pen,@Def,@Next,@Clm,@Date,@Ref2,'Recorded',@Notes,GETUTCDATE(),@By,0)",
                new { Id=Guid.NewGuid(), Fid=model.FacilityId, Ref=(string?)fac.ReferenceNo,
                      Cid=(Guid)fac.ClientId, Obl=model.Obligee, Pen=(decimal)fac.Amount,
                      Def=model.DefaultingBid, Next=model.NextLowestBid, Clm=claimAmt,
                      Date=model.ClaimDate??DateTime.UtcNow, Ref2=model.ClaimReference,
                      Notes=model.Notes, By=CurrentUserEmail });
            await conn.ExecuteAsync("UPDATE Facilities SET Status=5,RejectionReason=@R,UpdatedAt=GETUTCDATE() WHERE Id=@Id",
                new { Id=model.FacilityId, R=$"Bond Called. Claim: {claimAmt:N2}" });

            Success($"Bond claim recorded. Claim amount: KSh {claimAmt:N2}");
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateFacilities()
        {
            using var conn = _ctx.Create();
            var rows = await conn.QueryAsync<dynamic>(
                "SELECT f.Id,f.ReferenceNo,f.Amount,ISNULL(c.CompanyName,'') AS ClientName FROM Facilities f LEFT JOIN Clients c ON c.Id=f.ClientId WHERE f.IsDeleted=0 AND f.Status IN(1,2,3,4) AND f.Type IN(1,4,6) ORDER BY f.CreatedAt DESC");
            ViewBag.Facilities = rows.Select(r => new SelectListItem(
                $"{r.ReferenceNo} — {r.ClientName} (KSh {(decimal)r.Amount:N0})", ((Guid)r.Id).ToString())).ToList();
        }
    }
}
