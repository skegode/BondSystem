using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Enums;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.Infrastructure.Services
{
    public class ChequeDiscountService : IChequeDiscountService
    {
        private readonly DapperContext _ctx;

        public ChequeDiscountService(DapperContext ctx)
        {
            _ctx = ctx;
        }

        public async Task<ChequeDiscountResponse> CreateAsync(CreateChequeDiscountRequest req, string by)
        {
            var rateInfo = await GetLiveRateAsync(req.DraweeBank, 4);
            decimal currentRate = (decimal)rateInfo.Rate;

            // 2. Calculations
            var td = Math.Max(1, (int)(req.MaturityDate - DateTime.UtcNow).TotalDays);
            var adv = req.ChequeFaceValue * 0.85m; // 85% Margin

        

            using var conn = _ctx.Create();
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
 

                return await GetByIdAsync(1) ?? throw new Exception("Cheque not found after insert.");
            }               

            catch
            {
                trans.Rollback();
                throw;
            }
        }

        public async Task<ChequeDiscountResponse?> GetByIdAsync(int id)
        {
            using var conn = _ctx.Create();
            var r = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT f.Id AS FacilityId, f.ReferenceNo, f.TenorDays, f.Status, f.CreatedAt,
                       ISNULL(c.CompanyName,'') AS ClientName,
                       ch.Id, ch.ChequeNumber, ch.DrawerName, ch.DraweeBank, ch.DraweeBranch,
                       ch.ChequeDate, ch.MaturityDate, ch.ChequeFaceValue, ch.AdvanceAmount,
                       ch.DiscountFee, ch.NetAdvance, ch.PresentedToBank, ch.Honoured,
                       ch.Bounced, ch.BounceReason
                FROM   Facilities f
                LEFT JOIN Clients c ON c.Id = f.ClientId
                LEFT JOIN ChequeDiscounts ch ON ch.FacilityId = f.Id
                WHERE  f.Id = @Id AND f.IsDeleted = 0", new { Id = id });
            return r == null ? null : MapCheq(r);
        }

        public async Task<PagedResult<ChequeDiscountResponse>> GetAllAsync(FacilityFilter filter)
        {
            var w = "f.IsDeleted=0 AND f.Type=3" +
                (filter.Status.HasValue ? $" AND f.Status={(int)filter.Status.Value}" : "") +
                (!string.IsNullOrWhiteSpace(filter.Search) ? " AND (f.ReferenceNo LIKE @S OR c.CompanyName LIKE @S OR ch.ChequeNumber LIKE @S)" : "");

            var p = new { S = $"%{filter.Search}%", Skip = (filter.Page - 1) * filter.PageSize, Take = filter.PageSize };

            using var conn = _ctx.Create();
            var total = await conn.ExecuteScalarAsync<int>($@"
                SELECT COUNT(*) FROM Facilities f 
                LEFT JOIN Clients c ON c.Id = f.ClientId 
                LEFT JOIN ChequeDiscounts ch ON ch.FacilityId = f.Id 
                WHERE {w}", p);

            var rows = await conn.QueryAsync<dynamic>($@"
                SELECT f.Id AS FacilityId, f.ReferenceNo, f.TenorDays, f.Status, f.CreatedAt,
                       ISNULL(c.CompanyName,'') AS ClientName,
                       ch.Id, ch.ChequeNumber, ch.DrawerName, ch.DraweeBank, ch.DraweeBranch,
                       ch.ChequeDate, ch.MaturityDate, ch.ChequeFaceValue, ch.AdvanceAmount,
                       ch.DiscountFee, ch.NetAdvance, ch.PresentedToBank, ch.Honoured,
                       ch.Bounced, ch.BounceReason
                FROM   Facilities f
                LEFT JOIN Clients c ON c.Id = f.ClientId
                LEFT JOIN ChequeDiscounts ch ON ch.FacilityId = f.Id
                WHERE  {w} ORDER BY f.CreatedAt DESC OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", p);

            return new PagedResult<ChequeDiscountResponse>
            {
                Items = rows.Select<dynamic, ChequeDiscountResponse>(MapCheq).ToList(),
                TotalCount = total,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }

        public async Task<bool> PresentToBankAsync(int id, string by)
        {
            using var conn = _ctx.Create();
            return await conn.ExecuteAsync(
                "UPDATE ChequeDiscounts SET PresentedToBank=1, PresentedAt=GETUTCDATE(), UpdatedAt=GETUTCDATE() WHERE FacilityId=@Id AND IsDeleted=0",
                new { Id = id }) > 0;
        }

        public async Task<bool> RecordOutcomeAsync(RecordChequeOutcomeRequest req, string by)
        {
            using var conn = _ctx.Create();
            return await conn.ExecuteAsync(@"
                UPDATE ChequeDiscounts
                SET Honoured=@H, Bounced=@B, BounceReason=@R,
                    HonouredAt=CASE WHEN @H=1 THEN GETUTCDATE() ELSE NULL END,
                    UpdatedAt=GETUTCDATE(), UpdatedBy=@By
                WHERE FacilityId=@Id AND IsDeleted=0;
                UPDATE Facilities SET Status=CASE WHEN @H=1 THEN 7 ELSE 2 END,
                    UpdatedAt=GETUTCDATE(), UpdatedBy=@By WHERE Id=@Id",
                new { Id = req.FacilityId, H = req.Honoured ? 1 : 0, B = req.Honoured ? 0 : 1, R = req.BounceReason, By = by }) > 0;
        }

        private async Task<dynamic> GetLiveRateAsync(int bankId, int productType)
        {
            using var conn = _ctx.Create();
            var rateData = await conn.QueryFirstOrDefaultAsync(@"
                SELECT TOP 1 Rate, CommissionRate as Comm, CommissionType 
                FROM ProductRates 
                WHERE BankPartnerId = @BankId AND ProductType = @ProductType AND IsActive = 1",
                new { BankId = (int)bankId, ProductType = (int)productType });

            return rateData ?? new { Rate = 0.035m, Comm = 0m, CommissionType = 0 };
        }
        public async Task<CalculateResponse> CalculateCost(CalculateRequest req, string bankId)
        {
            // Convert string bankId to Enum
            if (!Enum.TryParse(bankId, out int bankEnum)) ;

            // Fetch dynamic rate for Cheque Discounting (Type 3)
            var rateInfo = await GetLiveRateAsync(bankEnum,4);
            decimal currentRate = (decimal)rateInfo.Rate;

            // Standard Cheque Discounting Calculation
            // Using 85% as the standard Advance Margin for Kenya
            decimal advanceAmount = req.Amount * 0.85m;

            return new CalculateResponse
            {
                PrincipalAmount = req.Amount,
                Rate = currentRate * 100,
                TenorDays = req.TenorDays,
                ProductType = "ChequeDiscount",
                RateDescription = $"{currentRate * 100:F1}% p.a. Discount Rate (85% Advance)"
            };
        }

        private static ChequeDiscountResponse MapCheq(dynamic r) => new()
        {
            Id = r.Id == null ? Guid.Empty : (Guid)r.Id,
            FacilityId = r.FacilityId == null ? Guid.Empty : (Guid)r.FacilityId,
            ReferenceNo = r.ReferenceNo ?? "",
            ClientName = r.ClientName ?? "",
            ChequeNumber = r.ChequeNumber ?? "",
            DrawerName = r.DrawerName ?? "",
            DraweeBank = r.DraweeBan,
            ChequeDate = r.ChequeDate == null ? DateTime.MinValue : (DateTime)r.ChequeDate,
            MaturityDate = r.MaturityDate == null ? DateTime.MinValue : (DateTime)r.MaturityDate,
            ChequeFaceValue = r.ChequeFaceValue == null ? 0m : (decimal)r.ChequeFaceValue,
            AdvanceAmount = r.AdvanceAmount == null ? 0m : (decimal)r.AdvanceAmount,
            DiscountFee = r.DiscountFee == null ? 0m : (decimal)r.DiscountFee,
            NetAdvance = r.NetAdvance == null ? 0m : (decimal)r.NetAdvance,
            TenorDays = r.TenorDays == null ? 0 : (int)r.TenorDays,
            Status = r.Status,
            PresentedToBank = r.PresentedToBank != null && (bool)r.PresentedToBank,
            Honoured = r.Honoured != null && (bool)r.Honoured,
            Bounced = r.Bounced != null && (bool)r.Bounced,
            BounceReason = r.BounceReason,
            CreatedAt = r.CreatedAt == null ? DateTime.MinValue : (DateTime)r.CreatedAt
        };
    }
}