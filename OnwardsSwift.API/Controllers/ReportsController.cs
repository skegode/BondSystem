using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Infrastructure.Data;
using Dapper; // <--- Critical: Needed for .QueryAsync

namespace OnwardsSwift.API.Controllers
{
    public class ReportsController : Controller
    {
        private readonly DapperContext _ctx;

        public ReportsController(DapperContext ctx)
        {
            _ctx = ctx;
        }

        public IActionResult Index()
        {
            return View();
        }

        public async Task<IActionResult> Revenue(DateTime? from, DateTime? to)
        {
            var startDate = from ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var endDate = to ?? DateTime.Now.Date.AddDays(1).AddTicks(-1);

            using var conn = _ctx.Create();

            // JOIN Path: GL -> Invoices -> Clients (for name) & BidBonds (for project context)
            const string sql = @"
        SELECT 
            gl.CreatedAt as Date, 
            i.InvoiceNumber as Reference,
            c.CompanyName as ClientName, 
            gl.Description, 
            gl.Amount,
            b.Id as BondNumber
        FROM GeneralLedger gl
        INNER JOIN Invoices i ON gl.ReferenceId = i.Id
        LEFT JOIN Clients c ON i.ClientId = c.Id
        LEFT JOIN Bonds b ON i.BondId = b.Id
        WHERE gl.TransactionType = 'REVENUE' 
        AND gl.CreatedAt BETWEEN @from AND @to
        ORDER BY gl.CreatedAt DESC";

            var details = await conn.QueryAsync<RevenueLine>(sql, new { from = startDate, to = endDate });

            var model = new RevenueReportViewModel
            {
                Details = details.ToList(),
                TotalRevenue = details.Sum(x => x.Amount)
            };

            ViewBag.From = startDate;
            ViewBag.To = endDate;
            return View(model);
        }

        public async Task<IActionResult> GeneralLedger()
        {
            using var conn = _ctx.Create();
            const string sql = "SELECT * FROM GeneralLedger ORDER BY CreatedAt DESC";
            var entries = await conn.QueryAsync<dynamic>(sql);
            return View(entries);
        }

        public async Task<IActionResult> BankCharges(DateTime? from, DateTime? to)
        {
            var startDate = from ?? new DateTime(DateTime.Now.Year, DateTime.Now.Month, 1);
            var endDate = to ?? DateTime.Now.Date.AddDays(1).AddTicks(-1);

            using var conn = _ctx.Create();

            // JOIN Path: GL -> Invoices -> BidBonds (to see which bond caused the charge)
            const string sql = @"
 SELECT 
    gl.CreatedAt as Date, 
    i.InvoiceNumber as Reference,
    b.ProcuringEntity as RelatedEntity, 
    gl.Description, 
    gl.Amount,
    b.Id as BondNumber
FROM GeneralLedger gl
INNER JOIN Invoices i ON gl.ReferenceId = i.Id
LEFT JOIN Bonds b ON i.BondId = b.Id
        WHERE gl.TransactionType = 'BANK_CHARGE'
        AND gl.CreatedAt BETWEEN @from AND @to
        ORDER BY gl.CreatedAt DESC";

            var details = await conn.QueryAsync<BankChargeLine>(sql, new { from = startDate, to = endDate });

            var model = new BankChargeViewModel
            {
                Details = details.ToList(),
                TotalCharges = details.Sum(x => x.Amount)
            };

            ViewBag.From = startDate;
            ViewBag.To = endDate;
            return View(model);
        }
    }
}