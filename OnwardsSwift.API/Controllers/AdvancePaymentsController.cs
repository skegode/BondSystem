using Dapper;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.API.Controllers
{
    public class AdvancePaymentsController : AppController
    {
        private readonly DapperContext  _ctx;
        private readonly IClientService _clients;

        public AdvancePaymentsController(DapperContext ctx, IClientService clients)
        { _ctx = ctx; _clients = clients; }

        public async Task<IActionResult> Index(int page = 1)
        {
            using var conn = _ctx.Create();
            var rows = await conn.QueryAsync<dynamic>(@"
                SELECT ap.Id,ap.FacilityId,ap.ContractRef,ap.Beneficiary,ap.BondAmount,ap.TotalFee,
                       ap.Status,ap.CreatedAt, ISNULL(c.CompanyName,'') AS ClientName
                FROM AdvancePayments ap
                LEFT JOIN Clients c ON c.Id=ap.ClientId
                WHERE ap.IsDeleted=0
                ORDER BY ap.CreatedAt DESC
                OFFSET @Skip ROWS FETCH NEXT 20 ROWS ONLY",
                new { Skip = (page - 1) * 20 });
            ViewBag.Page  = page;
            ViewBag.Items = rows.ToList();
            return View();
        }

        [HttpGet]
        public async Task<IActionResult> Create()
        {
            await PopulateClients();
            return View(new CreateAdvancePaymentRequest
            {
                ContractStartDate = DateTime.Today,
                ContractEndDate   = DateTime.Today.AddYears(1),
                TenorDays         = 365
            });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(CreateAdvancePaymentRequest model)
        {
            if (!ModelState.IsValid) { await PopulateClients(); return View(model); }

            // Calculate fees
            var rate        = model.BankRate / 100.0;
            var bankCharges = model.BondAmount * (decimal)rate * (model.TenorDays / 365m);
            var commission  = bankCharges * 0.10m;
            var vat         = commission * 0.16m;
            model.BankCharges = bankCharges;
            model.Commission  = commission;
            model.Vat         = vat;
            model.TotalFee    = bankCharges + commission + vat;

            using var conn = _ctx.Create();
            var refNo = $"AP-{DateTime.UtcNow:yyyy}-{Random.Shared.Next(1000,9999)}";
            var fid   = Guid.NewGuid();
            var apId  = Guid.NewGuid();

            await conn.ExecuteAsync(@"INSERT INTO Facilities(Id,ReferenceNo,Type,ClientId,Amount,Rate,TenorDays,FinanceFee,NetAmount,Status,Notes,CreatedAt,CreatedBy,IsDeleted)
                VALUES(@Id,@Ref,5,@Cid,@Amt,@Rate,@Tenor,@Fee,@Amt,1,@Notes,GETUTCDATE(),@By,0)",
                new { Id=fid, Ref=refNo, Cid=model.ClientId, Amt=model.BondAmount, Rate=(decimal)model.BankRate,
                      Tenor=model.TenorDays, Fee=model.TotalFee, Notes=$"AP Bond — {model.Beneficiary}", By=CurrentUserEmail });

            await conn.ExecuteAsync(@"INSERT INTO AdvancePayments(Id,FacilityId,ClientId,ContractRef,Beneficiary,BondAmount,BankRate,TenorDays,BankCharges,CommissionAmount,VatAmount,TotalFee,ContractStartDate,ContractEndDate,PaymentMode,Notes,Status,CreatedAt,CreatedBy,IsDeleted)
                VALUES(@Id,@Fid,@Cid,@Ref,@Ben,@Bamt,@Rate,@Tenor,@Bchg,@Comm,@Vat,@Tot,@Sd,@Ed,@Pm,@Notes,'Active',GETUTCDATE(),@By,0)",
                new { Id=apId, Fid=fid, Cid=model.ClientId, Ref=model.ContractRef, Ben=model.Beneficiary,
                      Bamt=model.BondAmount, Rate=(decimal)model.BankRate, Tenor=model.TenorDays,
                      Bchg=model.BankCharges, Comm=model.Commission, Vat=model.Vat, Tot=model.TotalFee,
                      Sd=model.ContractStartDate, Ed=model.ContractEndDate, Pm=model.PaymentMode,
                      Notes=model.Notes, By=CurrentUserEmail });

            Success($"Advance payment bond {refNo} recorded.");
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateClients()
        {
            var clients = await _clients.GetAllAsync(1, 200, null);
            ViewBag.Clients = new SelectList(clients.Items, "Id", "CompanyName");
        }
    }
}
