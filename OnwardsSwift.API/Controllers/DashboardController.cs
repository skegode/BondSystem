using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.API.Controllers
{
    [Authorize]
    [ApiController]
    [Route("api/[controller]")]
    public class DashboardController : ControllerBase
    {
        private readonly DapperContext _ctx;

        public DashboardController(DapperContext ctx)
        {
            _ctx = ctx;
        }

        [HttpGet("metrics")]
        public async Task<IActionResult> GetMetrics()
        {
            using var conn = _ctx.Create();

            try
            {
                const string sql = @"
                    SELECT
                        (SELECT COUNT(*) FROM Bonds WHERE isApproved = 1) AS ActiveBonds,
                        (SELECT COUNT(*) FROM Bonds WHERE isApproved IS NULL OR isApproved = 0) AS PendingReview,
                        (SELECT COUNT(*) FROM CashCovers) AS CashCovers,
                        (SELECT COUNT(DISTINCT IssuingBank) FROM Bonds) AS IssuingBanks,
                        (SELECT ISNULL(SUM(Amount), 0) FROM Bonds WHERE isApproved = 1) AS TotalExposure,
                        (SELECT ISNULL(SUM(CommissionAmount), 0) FROM Bonds) AS CommissionEarned,
                        (SELECT COUNT(*) FROM Bonds WHERE BondTypeId = 1) AS BidBonds,
                        (SELECT COUNT(*) FROM Bonds WHERE BondTypeId = 2) AS PerformanceBonds,
                        (SELECT COUNT(*) FROM Bonds WHERE BondTypeId = 3) AS AdvancePaymentBonds,
                        (SELECT COUNT(DISTINCT ClientId) FROM Bonds) AS TotalClients,
                        (SELECT ISNULL(SUM(CashCoverAmount), 0) FROM CashCovers) AS CashCoverPortfolio,
                        (SELECT ISNULL(SUM(Amount), 0) FROM Bonds WHERE BondTypeId = 1) AS BidBondsExposure,
                        (SELECT ISNULL(SUM(Amount), 0) FROM Bonds WHERE BondTypeId = 2) AS PerformanceBondsExposure,
                        (SELECT ISNULL(SUM(Amount), 0) FROM Bonds WHERE BondTypeId = 3) AS AdvancePaymentBondsExposure";

                var result = await conn.QueryFirstOrDefaultAsync<dynamic>(sql);

                // Fetch recent bonds
                const string recentBondsSql = @"
                    SELECT TOP 5
                        b.Id,
                        b.TenderNumber as BondRef,
                        c.CompanyName as Applicant,
                        bn.BankName as IssuingBank,
                        b.Amount as Value,
                        CASE 
                            WHEN b.isApproved = 1 THEN 'Approved'
                            WHEN b.isApproved = 2 THEN 'Rejected'
                            WHEN b.isApproved = 0 THEN 'Under Review'
                            ELSE 'Awaiting Docs'
                        END as Status,
                        b.TenderClosingDate as Closing
                    FROM Bonds b
                    LEFT JOIN Clients c ON c.Id = b.ClientId
                    LEFT JOIN Banks bn ON bn.Id = b.IssuingBank
                    ORDER BY b.CreatedAt DESC";

                var recentBonds = await conn.QueryAsync<dynamic>(recentBondsSql);

                if (result == null)
                    return NotFound("No data found");

                var totalBonds = (int)result.BidBonds + (int)result.PerformanceBonds + (int)result.AdvancePaymentBonds;

                return Ok(new
                {
                    activeBonds = (int)result.ActiveBonds,
                    pendingReview = (int)result.PendingReview,
                    cashCovers = (int)result.CashCovers,
                    issuingBanks = (int)result.IssuingBanks,
                    totalExposure = (decimal)result.TotalExposure,
                    commissionEarned = (decimal)result.CommissionEarned,
                    bidBonds = (int)result.BidBonds,
                    performanceBonds = (int)result.PerformanceBonds,
                    advancePaymentBonds = (int)result.AdvancePaymentBonds,
                    totalClients = (int)result.TotalClients,
                    cashCoverPortfolio = (decimal)result.CashCoverPortfolio,
                    bidBondsExposure = (decimal)result.BidBondsExposure,
                    performanceBondsExposure = (decimal)result.PerformanceBondsExposure,
                    advancePaymentBondsExposure = (decimal)result.AdvancePaymentBondsExposure,
                    totalBonds = totalBonds,
                    recentBonds = recentBonds.ToList()
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
