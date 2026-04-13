using Dapper;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Enums;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace OnwardsSwift.Infrastructure.Services
{
    public class InvoiceDiscountService : IInvoiceDiscountService
    {
        private readonly DapperContext _ctx;

        public InvoiceDiscountService(DapperContext ctx)
        {
            _ctx = ctx;
        }

        // Implementation of CalculateCost using Dapper
        public async Task<CalculateResponse> CalculateCost(CalculateRequest req, string bankId)
        {
            using var conn = _ctx.Create();
            var rateData = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT Rate, ApplicationFee, ApplicationFeeType 
                FROM ProductRates 
                WHERE BankPartnerId = @BankId 
                AND ProductType = @ProdType 
                AND IsActive = 1 AND IsDeleted = 0",
                new { BankId = bankId, ProdType = (int)req.ProductType });

            decimal rate = rateData?.Rate ?? 0.040m;
            decimal appFeeVal = rateData?.ApplicationFee ?? 0m;
            int appFeeType = rateData?.ApplicationFeeType ?? 1;

            var financeFee = req.Amount * rate * (req.TenorDays / 365m);
            var appFee = (appFeeType == 2) ? (req.Amount * (appFeeVal / 100)) : appFeeVal;

            return new CalculateResponse
            {
                PrincipalAmount = req.Amount,
                Rate = rate * 100,
                TenorDays = req.TenorDays,
                FinanceFee = Math.Round(financeFee, 2),
                ApplicationFee = Math.Round(appFee, 2),
                NetToClient = Math.Round(req.Amount - financeFee - appFee, 2),
                ProductType = req.ProductType.ToString(),
                RateDescription = $"{rate * 100:F1}% p.a. · {req.TenorDays} days"
            };
        }

        public async Task<InvoiceDiscountResponse> CreateAsync(CreateInvoiceDiscountRequest req, string by, int productId)
        {
            using var conn = _ctx.Create();

            var rateData = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT Rate FROM ProductRates 
                WHERE ProductType = @ProdType 
                AND IsActive = 1 AND IsDeleted = 0",
                new { ProdType = productId });

            decimal activeRate = rateData?.Rate ?? 0.040m;

            var td = Math.Max(1, (int)(req.InvoiceDueDate - DateTime.UtcNow).TotalDays);
            var adv = req.InvoiceFaceValue * (req.AdvancePercentage / 100m);

         

            await conn.ExecuteAsync(@"
                INSERT INTO Facilities
                    (Id, ReferenceNo, Type, ClientId, Amount, Rate, TenorDays, FinanceFee, NetAmount,
                     Status, DisbursementAccount, DisbursementBank, Notes, CreatedAt, CreatedBy, IsDeleted)
                VALUES (@Id, @Ref, 2, @Cid, @Amt, @Rate, @Td, @Fee, @Net, 1, @Acct, @Bank, @Notes, GETUTCDATE(), @By, 0)",
                new {  Ref = "INV-" + DateTime.Now.Ticks.ToString().Substring(10), Cid = req.ClientId, Amt = req.InvoiceFaceValue, Rate = activeRate, Td = td, Acct = req.DisbursementAccount, Bank = req.DisbursementBank, Notes = req.Notes, By = by });

            await conn.ExecuteAsync(@"
                INSERT INTO InvoiceDiscounts
                    (Id, FacilityId, InvoiceNumber, DebtorName, DebtorKraPin, DebtorContact,
                     InvoiceDate, InvoiceDueDate, InvoiceFaceValue, AdvancePercentage,
                     AdvanceAmount, DiscountFee, NetAdvance, DebtorPaid, CreatedAt, CreatedBy, IsDeleted)
                VALUES (@Id, @Fid, @Inv, @Deb, @Kra, @Con, @IDate, @Due, @Fv, @Ap, @Adv, @Fee, @Net, 0, GETUTCDATE(), @By, 0)",
                new {  Inv = req.InvoiceNumber, Deb = req.DebtorName, Kra = req.DebtorKraPin, Con = req.DebtorContact, IDate = req.InvoiceDate, Due = req.InvoiceDueDate, Fv = req.InvoiceFaceValue, Ap = req.AdvancePercentage, Adv = adv, By = by });

            return await GetByIdAsync(1) ?? throw new Exception("Invoice not found after insert.");
        }

        public async Task<InvoiceDiscountResponse?> GetByIdAsync(int id)
        {
            using var conn = _ctx.Create();
            // ... Mapping logic as per your previous implementation
            return null; // Replace with your mapping
        }

        public async Task<PagedResult<InvoiceDiscountResponse>> GetAllAsync(FacilityFilter filter)
        {
            return new PagedResult<InvoiceDiscountResponse>(); // Replace with your logic
        }

        public async Task<bool> RecordDebtorPaymentAsync(RecordDebtorPaymentRequest req, string by)
        {
            using var conn = _ctx.Create();
            return await conn.ExecuteAsync("UPDATE Facilities SET Status=7 WHERE Id=@Id", new { Id = req.FacilityId }) > 0;
        }
    }
}