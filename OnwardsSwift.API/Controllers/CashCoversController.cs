using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.API.Controllers
{
    public class CashCoversController : AppController
    {
        private readonly DapperContext  _ctx;
        private readonly IClientService _clients;
        public CashCoversController(DapperContext ctx, IClientService clients)
        { _ctx = ctx; _clients = clients; }

        public async Task<IActionResult> Index()
        {
            using var conn = _ctx.Create();
            try
            {
                // Aligning SQL Aliases with the View's property names
                const string sql = @"
            SELECT 
    c.Id,
    c.Reference as BondRef, 
    n.CompanyName as ClientName,
    b.Amount as BondAmount,
    c.CashCoverAmount,
    I.BankName as HoldingBank,
    ((c.CashCoverAmount / NULLIF(b.Amount, 0)) * 100) as CashCoverPct,
    c.MaturityDate,
    DATEDIFF(DAY, GETDATE(), c.MaturityDate) as DaysRemaining,
    c.Status,
    b.TenderName
FROM CashCovers c
INNER JOIN Bonds b ON b.Id = c.BondId
INNER JOIN Clients n ON n.Id = b.ClientId
INNER JOIN Obligees o ON o.Id = b.ProcuringEntity
inner join InternalBanks I on i.id=b.PaymentBankId";

                var rows = await conn.QueryAsync<CashCoverViewModel>(sql);

                ViewBag.Today = DateTime.UtcNow.Date;
                return View(rows.ToList());
            }
            catch (Exception ex)
            {
                // Log error here if needed
                return View(new List<CashCoverViewModel>());
            }
        }
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Release(
           int id,
           string notes,
           string paymentMethod,
           string phone,
           string bankName,
           string accountNo,
           string branchName)
        {
            using var conn = _ctx.Create();
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
                // 1. Update the CashCover status to lock it from further actions
                const string updateStatusSql = @"
            UPDATE CashCovers 
            SET Status = 'Awaiting Approval Release' 
            WHERE Id = @id AND Status = 'Active'";

                // 2. Insert the detailed request into the PaymentQueue
                const string queueSql = @"
            INSERT INTO PaymentQueue (
                ReferenceId, ReferenceType, Amount, PaymentMethod, 
                BankName, BankAccount, BankBranch, 
                CreatedAt, Status, Narration, CreatedBy
            )
            SELECT 
                Id, 'CASH_COVER', CashCoverAmount, @paymentMethod, 
                @bankName, @account, @branch, 
                GETDATE(), 'Awaiting Authorization', @notes, 'Sam' -- Replace with User.Identity.Name if available
            FROM CashCovers WHERE Id = @id";

                var destinationAccount = paymentMethod == "MPESA" ? phone : accountNo;

                // Execute both within the same transaction
                var affected = await conn.ExecuteAsync(updateStatusSql, new { id }, trans);

                if (affected > 0)
                {
                    await conn.ExecuteAsync(queueSql, new
                    {
                        id,
                        paymentMethod,
                        bankName,
                        account = destinationAccount,
                        branch = branchName,
                        notes
                    }, trans);

                    trans.Commit();
                    TempData["Success"] = "Release request submitted for authorization.";
                }
                else
                {
                    // If affected is 0, the record wasn't 'Active' or didn't exist
                    trans.Rollback();
                    TempData["Error"] = "Record is no longer eligible for release.";
                }
            }
            catch (Exception ex)
            {
                trans.Rollback();
                // Log the error (ex) here
                TempData["Error"] = "Critical error: Could not process release request.";
            }

            return RedirectToAction(nameof(Index));
        }
        public async Task<IActionResult> PendingReleases()
        {
            using var conn = _ctx.Create();
            const string sql = @"
        SELECT 
            pq.Id, 
            pq.ReferenceId, 
            pq.Amount, 
            pq.PaymentMethod, 
            pq.BankAccount, 
            pq.BankName, 
            pq.Narration, 
            pq.CreatedAt,
            c.Reference as BondRef
        FROM PaymentQueue pq
        INNER JOIN CashCovers c ON pq.ReferenceId = c.Id
        WHERE pq.Status = 'Awaiting Authorization'";

            var pending = await conn.QueryAsync<dynamic>(sql);
            return View(pending.ToList());
        }

        [HttpPost]
        public async Task<IActionResult> AuthorizeRelease(int queueId, int cashCoverId, string decision)
        {
            using var conn = _ctx.Create();
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
                if (decision == "Approve")
                {
                    // 1. Finalize Cash Cover Status
                    await conn.ExecuteAsync("UPDATE CashCovers SET Status = 'Released' WHERE Id = @cashCoverId", new { cashCoverId }, trans);
                    // 2. Update Queue Status
                    await conn.ExecuteAsync("UPDATE PaymentQueue SET Status = 'Completed', AuthorizedAt = GETDATE() WHERE Id = @queueId", new { queueId }, trans);
                }
                else
                {
                    // Reject: Return Cash Cover to 'Active' so it can be initiated again
                    await conn.ExecuteAsync("UPDATE CashCovers SET Status = 'Active' WHERE Id = @cashCoverId", new { cashCoverId }, trans);
                    await conn.ExecuteAsync("UPDATE PaymentQueue SET Status = 'Rejected' WHERE Id = @queueId", new { queueId }, trans);
                }

                trans.Commit();
                TempData["Success"] = $"Payment {decision}ed successfully.";
            }
            catch
            {
                trans.Rollback();
                TempData["Error"] = "Authorization failed.";
            }
            return RedirectToAction(nameof(PendingReleases));
        }
        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await PopulateFacilities();
            return View(new CashCoverRequest { MaturityDate = DateTime.Today.AddMonths(6) });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CashCoverRequest model)
        {
            if (!ModelState.IsValid) { await PopulateFacilities(); return View(model); }

            using var conn = _ctx.Create();
            var fac = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT f.Id,f.ReferenceNo,f.ClientId,f.Amount,ISNULL(c.CompanyName,'') AS CompanyName FROM Facilities f LEFT JOIN Clients c ON c.Id=f.ClientId WHERE f.Id=@Id AND f.IsDeleted=0",
                new { Id = model.FacilityId });
            if (fac == null) { Error("Facility not found."); return View(model); }

            await conn.ExecuteAsync(@"INSERT INTO CashCovers(Id,FacilityId,BondRef,ClientId,ClientName,BondAmount,CashCoverAmount,CashCoverPct,MaturityDate,HoldingAccount,HoldingBank,Status,Notes,CreatedAt,CreatedBy,IsDeleted)
                VALUES(@Id,@Fid,@Ref,@Cid,@Cname,@Bamt,@Ccamt,@Cpct,@Mat,@Acct,@Bank,'Active',@Notes,GETUTCDATE(),@By,0)",
                new { Id=Guid.NewGuid(), Fid=model.FacilityId, Ref=(string?)fac.ReferenceNo, Cid=(Guid)fac.ClientId,
                      Cname=(string)fac.CompanyName, Bamt=(decimal)fac.Amount, Ccamt=model.CashCoverAmount,
                      Cpct=model.CashCoverPct, Mat=model.MaturityDate, Acct=model.HoldingAccount,
                      Bank=model.HoldingBank, Notes=model.Notes, By=CurrentUserEmail });

            Success("Cash cover recorded.");
            return RedirectToAction(nameof(Index));
        }

   

        private async Task PopulateFacilities()
        {
            using var conn = _ctx.Create();
            var rows = await conn.QueryAsync<dynamic>(
                "SELECT f.Id,f.ReferenceNo,ISNULL(c.CompanyName,'') AS ClientName FROM Facilities f LEFT JOIN Clients c ON c.Id=f.ClientId WHERE f.IsDeleted=0 AND f.Type IN(1,4,6) ORDER BY f.CreatedAt DESC");
            ViewBag.Facilities = rows.Select(r => new SelectListItem(
                $"{r.ReferenceNo} — {r.ClientName}", ((Guid)r.Id).ToString())).ToList();
        }
    }
}
