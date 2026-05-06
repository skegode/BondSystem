using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;
using Dapper;

namespace OnwardsSwift.API.Controllers
{
    [Authorize]
    public class HomeController : Controller
    {
        private readonly DapperContext _ctx;
        public HomeController(IClientService clients, DapperContext ctx, IWebHostEnvironment webHostEnvironment)
        {
            _ctx = ctx;
        }


        public async Task<IActionResult> Index()
        {
            using var conn = _ctx.Create();
            var vm = new DashboardViewModel();

            // ── KPI stats ──────────────────────────────────────────
            var stats = await conn.QuerySingleAsync(@"
                SELECT
                    (SELECT COUNT(*) FROM Bonds WHERE isApproved = 1)                                    AS ActiveBonds,
                    (SELECT COUNT(*) FROM Bonds WHERE isApproved = 0)                                    AS PendingReview,
                    (SELECT COUNT(*) FROM CashCovers WHERE Status = 'Active')                            AS CashCovers,
                    (SELECT COUNT(DISTINCT IssuingBank) FROM Bonds)                                      AS IssuingBanks,
                    ISNULL((SELECT SUM(Amount) FROM Bonds WHERE isApproved = 1), 0)                     AS TotalExposure,
                    ISNULL((SELECT SUM(ApplicationFee) FROM Bonds WHERE isApproved = 1), 0)              AS CommissionEarned,
                    (SELECT COUNT(*) FROM Clients WHERE IsDeleted = 0)                                   AS TotalClients,
                    (SELECT COUNT(*) FROM FacilityApprovalInstances WHERE Status = 'PENDING')            AS PendingApprovals,
                    ISNULL((SELECT SUM(ChequeAmount) FROM ChequeDiscounting WHERE Status = 'Active'), 0) AS ChequePortfolio,
                    (SELECT COUNT(*) FROM ChequeDiscounting WHERE Status = 'Active')                     AS ChequeCount");

            vm.ActiveBonds      = (int)stats.ActiveBonds;
            vm.PendingReview    = (int)stats.PendingReview;
            vm.CashCovers       = (int)stats.CashCovers;
            vm.IssuingBanks     = (int)stats.IssuingBanks;
            vm.TotalExposure    = (decimal)stats.TotalExposure;
            vm.CommissionEarned = (decimal)stats.CommissionEarned;
            vm.TotalClients     = (int)stats.TotalClients;
            vm.PendingApprovals = (int)stats.PendingApprovals;
            vm.ChequePortfolio  = (decimal)stats.ChequePortfolio;
            vm.ChequeCount      = (int)stats.ChequeCount;

            // ── Recent bonds table (last 10) ───────────────────────
            vm.RecentBonds = (await conn.QueryAsync<RecentBondDto>(@"
                SELECT TOP 10
                    b.Id,
                    ISNULL(b.TenderNumber, b.PaymentReference) AS Reference,
                    c.CompanyName AS Applicant,
                    bk.BankName   AS Bank,
                    b.Amount      AS Value,
                    CASE WHEN b.isApproved = 1 THEN 'Approved'
                         WHEN b.isApproved = 2 THEN 'Rejected'
                         ELSE 'Pending' END AS Status,
                    b.TenderClosingDate AS Expiry
                FROM Bonds b
                JOIN Clients c  ON b.ClientId    = c.Id
                JOIN Banks   bk ON b.IssuingBank = bk.Id
                ORDER BY b.CreatedAt DESC")).ToList();

            // ── Monthly submissions — last 6 months ────────────────
            vm.MonthlySubs = (await conn.QueryAsync<MonthlyStatDto>(@"
                SELECT
                    FORMAT(CreatedAt, 'MMM yy') AS MonthLabel,
                    COUNT(*)                    AS Count,
                    ISNULL(SUM(Amount), 0)      AS Amount
                FROM Bonds
                WHERE CreatedAt >= DATEADD(MONTH, -5, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))
                GROUP BY FORMAT(CreatedAt, 'MMM yy'), YEAR(CreatedAt), MONTH(CreatedAt)
                ORDER BY YEAR(CreatedAt), MONTH(CreatedAt)")).ToList();

            // ── Monthly revenue — last 6 months ────────────────────
            vm.MonthlyRevenue = (await conn.QueryAsync<MonthlyStatDto>(@"
                SELECT
                    FORMAT(CreatedAt, 'MMM yy')         AS MonthLabel,
                    COUNT(*)                             AS Count,
                    ISNULL(SUM(ApplicationFee), 0)       AS Amount
                FROM Bonds
                WHERE isApproved = 1
                  AND CreatedAt >= DATEADD(MONTH, -5, DATEFROMPARTS(YEAR(GETDATE()), MONTH(GETDATE()), 1))
                GROUP BY FORMAT(CreatedAt, 'MMM yy'), YEAR(CreatedAt), MONTH(CreatedAt)
                ORDER BY YEAR(CreatedAt), MONTH(CreatedAt)")).ToList();

            // ── Status breakdown ───────────────────────────────────
            vm.StatusBreakdown = (await conn.QueryAsync<StatusStatDto>(@"
                SELECT
                    CASE WHEN isApproved = 1 THEN 'Approved'
                         WHEN isApproved = 2 THEN 'Rejected'
                         ELSE 'Pending' END AS Label,
                    COUNT(*)               AS Count,
                    ISNULL(SUM(Amount), 0) AS Amount
                FROM Bonds
                GROUP BY isApproved")).ToList();

            // ── Top 5 clients by exposure ──────────────────────────
            vm.TopClients = (await conn.QueryAsync<ClientExposureDto>(@"
                SELECT TOP 5
                    c.CompanyName          AS ClientName,
                    COUNT(b.Id)            AS BondCount,
                    ISNULL(SUM(b.Amount), 0) AS Exposure
                FROM Bonds b
                JOIN Clients c ON b.ClientId = c.Id
                WHERE b.isApproved = 1
                GROUP BY c.CompanyName
                ORDER BY Exposure DESC")).ToList();

            // ── Bond type distribution ─────────────────────────────
            vm.Distribution = (await conn.QueryAsync<BondTypeStat>(@"
                SELECT
                    pt.ProductName              AS TypeName,
                    COUNT(b.Id)                 AS Count,
                    ISNULL(SUM(b.Amount), 0)    AS Exposure,
                    CAST(COUNT(b.Id) * 100.0 / NULLIF((SELECT COUNT(*) FROM Bonds), 0) AS FLOAT) AS Percentage
                FROM Bonds b
                JOIN ProductTypes pt ON b.BondTypeId = pt.Id
                GROUP BY pt.ProductName")).ToList();

            return View(vm);
        }

        public IActionResult Error() => View();
    }
}
