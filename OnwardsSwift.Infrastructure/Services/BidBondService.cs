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
    public class BidBondService : IBidBondService
    {
        private readonly DapperContext _ctx;

        public BidBondService(DapperContext ctx)
        {
            _ctx = ctx;
        }

        public async Task<CalculateResponse> CalculateCost(CalculateRequest req, int bankId)
        {
            // 1. Convert string bankId to Enum for query
            if (!Enum.TryParse(bankId.ToString(), out int bankEnum)) ;

            // 2. Fetch Live Rates
            var rateInfo = await GetLiveRateAsync(bankEnum, req.ProductType);

            decimal annualRate = (decimal)rateInfo.Rate;
            decimal commRate = (decimal)rateInfo.Comm;
            int commType = (int)rateInfo.CommissionType;

            // 3. Perform Calculations
            decimal bankFee = req.Amount * annualRate * (req.TenorDays / 365.0m);
            decimal commission = (commType == 1) ? (req.Amount * commRate) : commRate;
            decimal totalFee = bankFee + commission;

            return new CalculateResponse
            {
                PrincipalAmount = req.Amount,
                Rate = annualRate * 100,
                TenorDays = req.TenorDays,
                FinanceFee = Math.Round(totalFee, 2),
                NetToClient = Math.Round(req.Amount - totalFee, 2),
                ProductType = "BidBond",
                RateDescription = $"{annualRate * 100:F1}% p.a. + {(commType == 1 ? (commRate * 100).ToString("F1") + "%" : "KES " + commRate.ToString("N0"))} commission"
            };
        }

        public async Task<int> CreateAsync(CreateBidBondRequest req, int currentUserId)
        {
            using var conn = _ctx.Create();
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
                // 1. We use the values already present in 'req' 
                // These were calculated by the JS Wizard/Professional View
                var bondSql = @"
            INSERT INTO Bonds (
                BondTypeId, TenderNumber, ProcuringEntity, IssuingBank, TenderClosingDate, 
                Amount, BankRate, TenderName, TenderDocPath, CR12Path, 
                ApplicationFee, CommissionAmount, BankCharge, -- Value passed from UI
                isApproved, CreatedAt, CreatedBy, 
                ClientId, AgentId, TenorDays, IsDeferredPayment, 
                PaymentBankId, PaymentReference, PaymentReceiptPath,
                CompanyProfilePath, IndemnityDocPath, DisbursementAccount, DisbursementBank, Notes
            )
            VALUES (
                @BTid, @TenNo, @Entity, @Bank, @Close, 
                @Amt, @BRate, @TName, @TDoc, @CR12, 
                @AFee, @CAmt, @BCharge,
                0, GETDATE(), @CreatedBy, 
                @Cid, @AgentId, @Tenor, @IsDef, 
                @PayBank, @PayRef, @PayPath,
                @Profile, @Indemnity, @DAccount, @DBank, @Notes
            );
            SELECT CAST(SCOPE_IDENTITY() as int);";

                int newBondId = await conn.QuerySingleAsync<int>(bondSql, new
                {
                    BTid = req.BondTypeId,
                    TenNo = req.TenderNumber,
                    Entity = req.ProcuringEntity,
                    Bank = req.IssuingBank,
                    Close = req.TenderClosingDate,
                    Amt = req.Amount,
                    BRate = req.AppliedRate,       // From UI Hidden Field
                    TName = req.TenderName,
                    TDoc = req.TenderDocPath,
                    CR12 = req.CR12Path,
                    AFee = req.ApplicationFee,     // From UI Hidden Field
                    CAmt = req.CommissionAmount,   // From UI Hidden Field
                    BCharge = req.CommissionAmount, 
                    CreatedBy = currentUserId.ToString(),
                    Cid = req.ClientId,
                    AgentId = req.AgentId == 0 ? (int?)null : req.AgentId,
                    Tenor = req.TenorDays,
                    IsDef = req.IsDeferredPayment,
                    PayBank = req.PaymentBankId,
                    PayRef = req.PaymentReference,
                    PayPath = req.PaymentReceiptPath,
                    Profile = req.CompanyProfilePath,
                    Indemnity = req.IndemnityDocPath,
                    DAccount = req.DisbursementAccount,
                    DBank = req.DisbursementBank,
                    Notes = req.Notes
                }, trans);

                // 2. Insert Cash Cover (unchanged)
                if (req.HasCashCover && req.CashCoverAmount > 0)
                {
                    var cashCoverSql = @"
                INSERT INTO CashCovers (BondId, CashCoverAmount, MaturityDate, Status, CreatedAt, CreatedBy, BankId, Reference, ReceiptPath)
                VALUES (@Bid, @CAmt, @MatDate, 'Active', GETDATE(), @By, @CCBank, @CCRef, @CCPath)";

                    await conn.ExecuteAsync(cashCoverSql, new
                    {
                        Bid = newBondId,
                        CAmt = req.CashCoverAmount,
                        MatDate = req.CashCoverDueDate ?? DateTime.Now.AddDays(req.TenorDays),
                        By = currentUserId.ToString(),
                        CCBank = req.CashCoverBankId,
                        CCRef = req.CashCoverReference,
                        CCPath = req.CashCoverReceiptPath
                    }, trans);
                }

                trans.Commit();
                return newBondId;
            }
            catch
            {
                if (trans.Connection != null) trans.Rollback();
                throw;
            }
        }
        public async Task<IEnumerable<PendingBondVM>> GetPendingApprovalsAsync()
        {
            using var conn = _ctx.Create();
            // Joins ensure we see "Bid Bond" instead of '1' and "ABC Ltd" instead of '45'
            var sql = @"
        SELECT 
    b.Id, 
    b.TenderNumber, 
    b.TenderName, 
    b.Amount, 
    b.TenderClosingDate, 
    b.IsDeferredPayment, 
    b.CreatedAt,
    c.companyname ClientName, 
    bt.ProductName as BondTypeName
FROM Bonds b
INNER JOIN Clients c ON b.ClientId = c.Id
INNER JOIN producttypes bt ON b.BondTypeId = bt.Id
WHERE b.isApproved IS NULL OR b.isApproved = 0
ORDER BY b.CreatedAt DESC";

            return await conn.QueryAsync<PendingBondVM>(sql);
        }
        public async Task<bool> ApproveAsync(ApproveBondRequest req, int approvedByUserId)
        {
            using var conn = _ctx.Create();
            // Maps 1 for Approved, 2 for Rejected based on the boolean in the DTO
            var sql = @"
        UPDATE Bonds 
        SET 
            isApproved = @Status,
            ApprovedBy = @By,
            ApprovedAt = GETDATE(),
            StatusNotes = @Notes,
            UpdatedAt = GETDATE(),
            UpdatedBy = @By
        WHERE Id = @Bid";

            int rows = await conn.ExecuteAsync(sql, new
            {
                Status = req.IsApproved ? 1 : 2,
                By = approvedByUserId.ToString(),
                Notes = req.Remarks,
                Bid = req.BondId
            });

            return rows > 0;
        }

        public async Task<BidBondResponse?> GetByIdAsync(int id)
        {
            using var conn = _ctx.Create();

            const string sql = @"
                SELECT
                    b.Id,
                    b.TenderNumber,
                    b.TenderName,
                    b.Amount,
                    b.BankRate        AS Rate,
                    b.TenorDays,
                    b.TenderClosingDate,
                    b.IsDeferredPayment,
                    b.PaymentReference,
                    b.PaymentReceiptPath,
                    b.TenderDocPath,
                    b.CR12Path,
                    b.CompanyProfilePath,
                    b.IndemnityDocPath,
                    b.Notes,
                    b.StatusNotes,
                    b.CreatedAt,
                    b.isApproved,
                    ISNULL(b.ApplicationFee, 0)    AS ApplicationFee,
                    ISNULL(b.CommissionAmount, 0)   AS CommissionAmount,
                    ISNULL(b.BankCharge, 0)         AS BankCharge,
                    c.CompanyName                   AS ClientName,
                    bt.ProductName                  AS BondTypeName,
                    b.ProcuringEntity               AS ProcuringEntityName,
                    bk.BankName                     AS IssuingBankName,
                    ISNULL(su.FullName, b.ApprovedBy) AS ApprovedByName,
                    b.ApprovedAt,
                    cc.CashCoverAmount              AS CCAmount,
                    cc.MaturityDate                 AS CCMaturityDate,
                    cc.Reference                    AS CCReference,
                    cc.ReceiptPath                  AS CCReceiptPath,
                    cc.CreatedAt                    AS CCDate
                FROM Bonds b
                INNER JOIN Clients      c  ON c.Id  = b.ClientId
                INNER JOIN ProductTypes bt ON bt.Id = b.BondTypeId
                INNER JOIN Banks        bk ON bk.Id = b.IssuingBank
                LEFT  JOIN SystemUsers  su ON CAST(su.Id AS NVARCHAR) = b.ApprovedBy
                LEFT  JOIN CashCovers   cc ON cc.BondId = b.Id
                WHERE b.Id = @Id";

            var r = await conn.QueryFirstOrDefaultAsync<dynamic>(sql, new { Id = id });
            if (r == null) return null;

            int approvalFlag = r.isApproved != null ? (int)r.isApproved : 0;
            string status = approvalFlag == 1 ? "Approved"
                          : approvalFlag == 2 ? "Rejected"
                          : "Pending";

            return new BidBondResponse
            {
                Id                    = (int)r.Id,
                ReferenceNo           = r.TenderNumber?.ToString() ?? $"BOND-{r.Id}",
                ClientName            = r.ClientName?.ToString() ?? "",
                TenderNumber          = r.TenderNumber?.ToString() ?? "",
                TenderName            = r.TenderName?.ToString() ?? "",
                ProcuringEntity       = r.ProcuringEntityName?.ToString() ?? "",
                ProcuringEntityName   = r.ProcuringEntityName?.ToString() ?? "",
                IssuingBank           = r.IssuingBankName?.ToString() ?? "",
                BondTypeName          = r.BondTypeName?.ToString() ?? "",
                Amount                = (decimal)r.Amount,
                Rate                  = r.Rate != null ? (decimal)r.Rate : 0m,
                CommissionFee         = (decimal)r.CommissionAmount,
                ApplicationFee        = (decimal)r.ApplicationFee,
                BankCharge            = (decimal)r.BankCharge,
                TenorDays             = (int)r.TenorDays,
                TenderClosingDate     = (DateTime)r.TenderClosingDate,
                IsDeferredPayment     = r.IsDeferredPayment != null && (bool)r.IsDeferredPayment,
                PaymentReference      = r.PaymentReference?.ToString(),
                PaymentReceiptPath    = r.PaymentReceiptPath?.ToString(),
                TenderDocPath         = r.TenderDocPath?.ToString(),
                CR12Path              = r.CR12Path?.ToString(),
                CompanyProfilePath    = r.CompanyProfilePath?.ToString(),
                IndemnityDocPath      = r.IndemnityDocPath?.ToString(),
                Notes                 = r.Notes?.ToString(),
                StatusNotes           = r.StatusNotes?.ToString(),
                Status                = status,
                ApprovedBy            = r.ApprovedByName?.ToString(),
                ApprovedAt            = r.ApprovedAt,
                CreatedAt             = (DateTime)r.CreatedAt,
                HasCashCover          = r.CCAmount != null,
                CashCoverAmount       = r.CCAmount != null ? (decimal?)r.CCAmount : null,
                CashCoverReceiptRef   = r.CCReference?.ToString(),
                CashCoverReceiptPath  = r.CCReceiptPath?.ToString(),
                CashCoverMaturityDate = r.CCMaturityDate,
                CashCoverDate         = r.CCDate,
            };
        }

        public async Task<PagedResult<BidBondResponse>> GetAllAsync(FacilityFilter filter)
        {
            

       

            using var conn = _ctx.Create();

            // 2. Count Total Records
            var countSql = $@"
        SELECT COUNT(*) 
        FROM Bonds b 
        LEFT JOIN Clients c ON c.Id = b.ClientId ";

            var total = await conn.ExecuteScalarAsync<int>(countSql);

            var rowsSql = $@"
        SELECT 
            b.Id AS BondId, 
            b.Id AS FacilityId, -- Keeping alias for compatibility with your MapBond method
            b.TenderNumber, 
            b.TenderName,
            b.Amount, 
            b.BankRate, 
            b.ApplicationFee, 
            b.CommissionAmount,
            b.isApproved AS Status, -- Mapping isApproved to Status for the DTO
            b.CreatedAt, 
            b.TenderClosingDate,
            b.IssuingBank,
            b.ProcuringEntity, -- This is now an INT ID in your schema
            ISNULL(c.CompanyName, 'N/A') AS ClientName
        FROM Bonds b
        INNER JOIN Clients c ON c.Id = b.ClientId
        ORDER BY b.CreatedAt DESC 
        OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY";

            var rows = await conn.QueryAsync<dynamic>(rowsSql);

            // 4. Return Paged Result
            return new PagedResult<BidBondResponse>
            {
                Items = rows.Select<dynamic, BidBondResponse>(MapBond).ToList(),
                TotalCount = total,
                Page = filter.Page,
                PageSize = filter.PageSize
            };
        }

        private async Task<dynamic> GetLiveRateAsync(int bankId, int productType)
        {
            using var conn = _ctx.Create();
            // Added ApplicationFee and ApplicationFeeType to the query
            var sql = @"
        SELECT TOP 1 
            Rate, 
            Commission as Comm, 
            CommissionType,
            ApplicationFee as AppFee, 
            ApplicationFeeType
        FROM ProductRates 
        WHERE BankPartnerId = @BankId AND ProductType = @ProductType AND IsActive = 1";

            var rateData = await conn.QueryFirstOrDefaultAsync(sql, new { BankId = (int)bankId, ProductType = (int)productType });

            // Kenyan Financial System Defaults (Updated to include AppFee)
            return rateData ?? new
            {
                Rate = 0.025m,
                Comm = 0.01m,
                CommissionType = 1,
                AppFee = 2500m,       // Default KES 2,500
                ApplicationFeeType = 2 // Default to Flat Fee
            };
        }

        private static BidBondResponse MapBond(dynamic r) => new()
        {
            Id = r.Id,
            ClientName = r.ClientName ?? "",
            TenderNumber = r.TenderNumber ?? "",
            ProcuringEntity = r.ProcuringEntity ?? "",
            Amount = r.Amount == null ? 0m : (decimal)r.Amount,
            Rate = r.Rate == null ? 0m : (decimal)r.Rate * 100,
            CommissionFee = r.FinanceFee == null ? 0m : (decimal)r.FinanceFee,
            TenorDays = r.TenorDays == null ? 0 : (int)r.TenorDays,
            IssuingBank = r.IssuingBank ,
            TenderClosingDate = r.TenderClosingDate == null ? DateTime.MinValue : (DateTime)r.TenderClosingDate,
            Status = r.Status,
            BondNumber = r.BondNumber,
            CreatedAt = r.CreatedAt == null ? DateTime.MinValue : (DateTime)r.CreatedAt,

            TenderDocPath = r.TenderDocPath,
            CR12Path = r.CR12Path,
            PaymentReceiptPath = r.PaymentReceiptPath,
            IsDeferredPayment = r.IsDeferredPayment ?? false,
            Notes = r.Notes
        };

        public Task<bool> ResellAsync(ResaleBidBondRequest req, string by) => throw new NotImplementedException();
        public Task<bool> ConvertToPerformanceBondAsync(int id, string by) => throw new NotImplementedException();
    }
}