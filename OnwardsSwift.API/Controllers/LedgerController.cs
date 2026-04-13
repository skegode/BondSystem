using Dapper;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.API.Controllers
{
    public class LedgerController : AppController
    {
        private readonly DapperContext _ctx;
        public LedgerController(DapperContext ctx) => _ctx = ctx;

        public async Task<IActionResult> Index(string? date)
        {
            var day = date != null ? DateTime.Parse(date).Date : DateTime.UtcNow.Date;
            using var conn = _ctx.Create();

            try
            {
                var entries = await conn.QueryAsync<dynamic>(
                    "SELECT Id,EntryType,Reference,ClientName,Description,Amount,BankCharges,CommissionAmount,VatAmount,IsPaid,PaidAt,PaymentMethod FROM LedgerEntries WHERE LedgerDate=@Day AND IsDeleted=0 ORDER BY CreatedAt",
                    new { Day=day });
                var summary = await conn.QueryFirstOrDefaultAsync<dynamic>(
                    "SELECT OpeningBalance,IsReconciled FROM DailyLedgers WHERE LedgerDate=@Day AND IsDeleted=0",
                    new { Day=day });

                var list    = entries.ToList();
                var openBal = summary == null ? 0m : (decimal)summary.OpeningBalance;
                var recs    = list.Where(e => (string?)e.EntryType == "Receipt").ToList();
                var pays    = list.Where(e => (string?)e.EntryType == "Payment").ToList();

                ViewBag.Date           = day;
                ViewBag.OpeningBalance = openBal;
                ViewBag.TotalReceipts  = recs.Sum(e => (decimal)e.Amount);
                ViewBag.TotalPayments  = pays.Sum(e => (decimal)e.Amount);
                ViewBag.TotalProfit    = recs.Sum(e => (decimal)e.CommissionAmount);
                ViewBag.ClosingBalance = openBal + (decimal)ViewBag.TotalReceipts - (decimal)ViewBag.TotalPayments;
                ViewBag.IsReconciled   = summary != null && (bool)summary.IsReconciled;
                ViewBag.Receipts       = recs;
                ViewBag.Payments       = pays;
            }
            catch
            {
                ViewBag.Date = day;
                ViewBag.Error = "Ledger tables not yet created. Run AddFeatureTables.sql.";
            }

            return View();
        }
    }
}
