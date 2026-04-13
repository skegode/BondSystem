using Dapper;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data; // Ensure this matches your namespace for DapperContext
using System;
using System.Threading.Tasks;

namespace OnwardsSwift.Infrastructure.Services
{
    public class CalculatorService : ICalculatorService
    {
        private readonly DapperContext _ctx;

        // Ensure this matches the parameter type in your DI container
        public CalculatorService(DapperContext ctx)
        {
            _ctx = ctx;
        }

        public async Task<CalculateResponse> Calculate(CalculateRequest req, string bankId)
        {
            using var conn = _ctx.Create();

            // Fetch dynamic rates from your ProductRates table
            var rateData = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT Rate, ApplicationFee, ApplicationFeeType 
                FROM ProductRates 
                WHERE BankPartnerId = @BankId 
                AND ProductType = @ProdType 
                AND IsActive = 1 
                AND IsDeleted = 0",
                new { BankId = bankId, ProdType = (int)req.ProductType });

            // Default fallback if nothing found in DB
            decimal rate = rateData?.Rate ?? 0.025m;
            decimal appFeeVal = rateData?.ApplicationFee ?? 0m;
            int appFeeType = rateData?.ApplicationFeeType ?? 1;

            // Compute fees
            var financeFee = req.Amount * rate * (req.TenorDays / 365m);
            var appFee = (appFeeType == 2) ? (req.Amount * (appFeeVal / 100)) : appFeeVal;
            var advance = req.Amount * ((req.AdvancePercentage ?? 100m) / 100m);

            return new CalculateResponse
            {
                PrincipalAmount = req.Amount,
                Rate = rate * 100,
                TenorDays = req.TenorDays,
                FinanceFee = Math.Round(financeFee, 2),
                ApplicationFee = Math.Round(appFee, 2),
                AdvanceAmount = Math.Round(advance, 2),
                NetToClient = Math.Round(advance - financeFee - appFee, 2),
                ProductType = req.ProductType.ToString(),
                RateDescription = $"{rate * 100:F1}% p.a. · {req.TenorDays} days"
            };
        }
    }
}