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
                var allowedMethods = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "MPESA", "BANK", "CHEQUE" };
                var normalizedPaymentMethod = string.IsNullOrWhiteSpace(req.PaymentMethod)
                    ? null
                    : req.PaymentMethod.Trim().ToUpperInvariant();

                if (normalizedPaymentMethod != null && !allowedMethods.Contains(normalizedPaymentMethod))
                    throw new InvalidOperationException("Invalid payment method selected.");

                var hasBondPayments = await conn.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1)
                    FROM INFORMATION_SCHEMA.TABLES
                    WHERE TABLE_NAME = 'BondPayments'", transaction: trans) > 0;

                var hasPaymentMethodColumn = await conn.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1)
                    FROM sys.columns c
                    INNER JOIN sys.objects o ON c.object_id = o.object_id
                    WHERE o.name = 'Bonds' AND o.type = 'U' AND c.name = 'PaymentMethod'", transaction: trans) > 0;

                var hasApplicationStatusColumn = await conn.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1)
                    FROM sys.columns c
                    INNER JOIN sys.objects o ON c.object_id = o.object_id
                    WHERE o.name = 'Bonds' AND o.type = 'U' AND c.name = 'ApplicationStatus'", transaction: trans) > 0;

                var hasAmendmentFeeColumn = await conn.ExecuteScalarAsync<int>(@"
                    SELECT COUNT(1)
                    FROM sys.columns c
                    INNER JOIN sys.objects o ON c.object_id = o.object_id
                    WHERE o.name = 'Bonds' AND o.type = 'U' AND c.name = 'AmendmentFee'", transaction: trans) > 0;

                // 1. We use the values already present in 'req'.
                // These were calculated by the create form pricing section.
                // Pricing summary computations are enforced server-side for consistency.
                var normalizedAppStatus = string.IsNullOrWhiteSpace(req.ApplicationStatus)
                    ? "New Application"
                    : req.ApplicationStatus.Trim();
                var amendmentFee = Math.Max(req.AmendmentFee, 0);
                var isAmendment = string.Equals(normalizedAppStatus, "Amendment", StringComparison.OrdinalIgnoreCase);
                var clientCharges = isAmendment ? amendmentFee : req.ClientCharges;
                var bankCharge = req.BankCharges;
                const decimal taxPercentage = 20m;
                var taxCalculation = Math.Round(bankCharge * (taxPercentage / 100m), 2);
                var totalBankCharge = bankCharge + taxCalculation;
                var netProfit = clientCharges - totalBankCharge;

                var insertColumns = new List<string>
                {
                    "BondTypeId", "TenderNumber", "ProcuringEntity", "IssuingBank", "TenderClosingDate",
                    "Amount", "BankRate", "TenderName", "TenderDocPath", "CR12Path",
                    "ApplicationFee", "CommissionAmount", "BankCharge",
                    "TaxPercentage", "TaxCalculation", "TotalBankCharge",
                    "isApproved", "CreatedAt", "CreatedBy",
                    "ClientId", "AgentId", "TenorDays", "IsDeferredPayment",
                    "PaymentBankId", "PaymentReference", "PaymentReceiptPath",
                    "CompanyProfilePath", "IndemnityDocPath", "DisbursementAccount", "DisbursementBank", "Notes",
                    "ProcessingDate"
                };

                var insertValues = new List<string>
                {
                    "@BTid", "@TenNo", "@Entity", "@Bank", "@Close",
                    "@Amt", "@BRate", "@TName", "@TDoc", "@CR12",
                    "@AFee", "@CAmt", "@BCharge",
                    "@TaxPct", "@TaxCalc", "@TotalBank",
                    "0", "GETDATE()", "@CreatedBy",
                    "@Cid", "@AgentId", "@Tenor", "@IsDef",
                    "@PayBank", "@PayRef", "@PayPath",
                    "@Profile", "@Indemnity", "@DAccount", "@DBank", "@Notes",
                    "@ProcDate"
                };

                if (hasPaymentMethodColumn)
                {
                    insertColumns.Add("PaymentMethod");
                    insertValues.Add("@PayMethod");
                }

                if (hasApplicationStatusColumn)
                {
                    insertColumns.Add("ApplicationStatus");
                    insertValues.Add("@AppStatus");
                }

                if (hasAmendmentFeeColumn)
                {
                    insertColumns.Add("AmendmentFee");
                    insertValues.Add("@AmendFee");
                }

                var bondSql = $@"
            INSERT INTO Bonds (
                {string.Join(", ", insertColumns)}
            )
            VALUES (
                {string.Join(", ", insertValues)}
            );
            SELECT CAST(SCOPE_IDENTITY() as int);";

                var totalCharged = Math.Max(clientCharges, 0);
                var requestedAmountPaid = Math.Max(req.AmountPaid ?? 0, 0);
                if (req.IsPaid && requestedAmountPaid == 0 && totalCharged > 0)
                    requestedAmountPaid = totalCharged;

                var captureAmount = Math.Min(requestedAmountPaid, totalCharged);
                var isDeferredPayment = totalCharged > 0 && captureAmount < totalCharged;

                if (captureAmount > 0 && !hasBondPayments)
                    throw new InvalidOperationException("Partial-payment table is missing. Run the BondPayments migration script.");

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
                    AFee = netProfit,
                    CAmt = clientCharges,
                    BCharge = bankCharge,
                    TaxPct = taxPercentage,
                    TaxCalc = taxCalculation,
                    TotalBank = totalBankCharge,
                    CreatedBy = currentUserId.ToString(),
                    Cid = req.ClientId,
                    AgentId = req.AgentId == 0 ? (int?)null : req.AgentId,
                    Tenor = req.TenorDays,
                    IsDef = isDeferredPayment,
                    PayBank = req.PaymentBankId,
                    PayRef = string.IsNullOrWhiteSpace(req.PaymentReference) ? null : req.PaymentReference.Trim(),
                    PayMethod = normalizedPaymentMethod,
                    PayPath = req.PaymentReceiptPath,
                    AppStatus = normalizedAppStatus,
                    AmendFee = amendmentFee,
                    Profile = req.CompanyProfilePath,
                    Indemnity = req.IndemnityDocPath,
                    DAccount = req.DisbursementAccount,
                    DBank = req.DisbursementBank,
                    Notes = req.Notes,
                    ProcDate = req.ProcessingDate
                }, trans);

                if (captureAmount > 0)
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO BondPayments
                            (BondId, ClientId, AmountPaid, PaymentMethod, PaymentReference, Notes, PaymentDate, CreatedAt, CreatedBy)
                        VALUES
                            (@BondId, @ClientId, @AmountPaid, @PaymentMethod, @PaymentReference, @Notes, GETDATE(), GETDATE(), @CreatedBy)",
                        new
                        {
                            BondId = newBondId,
                            ClientId = req.ClientId,
                            AmountPaid = captureAmount,
                            PaymentMethod = normalizedPaymentMethod,
                            PaymentReference = string.IsNullOrWhiteSpace(req.PaymentReference) ? null : req.PaymentReference.Trim(),
                            Notes = string.IsNullOrWhiteSpace(req.PaymentNotes) ? null : req.PaymentNotes.Trim(),
                            CreatedBy = currentUserId.ToString()
                        }, trans);
                }

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

            var hasBondPayments = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM INFORMATION_SCHEMA.TABLES
                WHERE TABLE_NAME = 'BondPayments'") > 0;

            var hasPaymentMethodColumn = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                WHERE o.name = 'Bonds' AND o.type = 'U' AND c.name = 'PaymentMethod'") > 0;

            var paidAmountExpr = hasBondPayments
                ? "ISNULL((SELECT SUM(bp.AmountPaid) FROM BondPayments bp WHERE bp.BondId = b.Id), 0)"
                : "0";
            var paymentMethodSelect = hasPaymentMethodColumn
                ? "b.PaymentMethod,"
                : string.Empty;

            var hasApplicationStatusColumn = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                WHERE o.name = 'Bonds' AND o.type = 'U' AND c.name = 'ApplicationStatus'") > 0;

            var hasAmendmentFeeColumn = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM sys.columns c
                INNER JOIN sys.objects o ON c.object_id = o.object_id
                WHERE o.name = 'Bonds' AND o.type = 'U' AND c.name = 'AmendmentFee'") > 0;

            var applicationStatusSelect = hasApplicationStatusColumn
                ? "ISNULL(b.ApplicationStatus, 'New Application') AS ApplicationStatus,"
                : "'New Application' AS ApplicationStatus,";

            var amendmentFeeSelect = hasAmendmentFeeColumn
                ? "ISNULL(b.AmendmentFee, 0) AS AmendmentFee,"
                : "0 AS AmendmentFee,";

            var sql = @"
                SELECT
                    b.Id,
                    b.TenderNumber,
                    b.TenderName,
                    b.Amount,
                    b.BankRate        AS Rate,
                    b.TenorDays,
                    b.TenderClosingDate,
                    b.ProcessingDate,
                    b.IsDeferredPayment,
                    b.PaymentReference,
                    " + paidAmountExpr + @" AS PaidAmount,
                    b.PaymentReceiptPath,
                    b.TenderDocPath,
                    b.CR12Path,
                    b.CompanyProfilePath,
                    b.IndemnityDocPath,
                    b.Notes,
                    b.StatusNotes,
                    b.CreatedAt,
                    b.isApproved,
                    " + paymentMethodSelect + @"
                    " + applicationStatusSelect + @"
                    " + amendmentFeeSelect + @"
                    ISNULL(b.ApplicationFee, 0)    AS ApplicationFee,
                    ISNULL(b.CommissionAmount, 0)   AS CommissionAmount,
                    ISNULL(b.BankCharge, 0)         AS BankCharge,
                    ISNULL(b.TaxPercentage, 0)      AS TaxPercentage,
                    ISNULL(b.TaxCalculation, 0)     AS TaxCalculation,
                    ISNULL(b.TotalBankCharge, ISNULL(b.BankCharge, 0)) AS TotalBankCharge,
                    ISNULL(b.ApplicationFee, ISNULL(b.CommissionAmount, 0) - ISNULL(b.TotalBankCharge, ISNULL(b.BankCharge, 0))) AS NetProfit,
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
                ClientCharge          = (decimal)r.CommissionAmount,
                ApplicationFee        = (decimal)r.NetProfit,
                NetProfit             = (decimal)r.NetProfit,
                BankCharge            = (decimal)r.BankCharge,
                TaxPercentage         = (decimal)r.TaxPercentage,
                TaxCalculation        = (decimal)r.TaxCalculation,
                TotalBankCharge       = (decimal)r.TotalBankCharge,
                PaidAmount            = (decimal)(r.PaidAmount ?? 0),
                OutstandingAmount     = Math.Max((decimal)r.CommissionAmount - (decimal)(r.PaidAmount ?? 0), 0),
                PayableCharge         = (decimal)r.CommissionAmount,
                TenorDays             = (int)r.TenorDays,
                TenderClosingDate     = (DateTime)r.TenderClosingDate,
                ProcessingDate        = r.ProcessingDate != null ? (DateTime?)r.ProcessingDate : null,
                IsDeferredPayment     = r.IsDeferredPayment != null && (bool)r.IsDeferredPayment,
                PaymentReference      = r.PaymentReference?.ToString(),
                PaymentMethod         = hasPaymentMethodColumn ? r.PaymentMethod?.ToString() : null,
                PaymentReceiptPath    = r.PaymentReceiptPath?.ToString(),
                TenderDocPath         = r.TenderDocPath?.ToString(),
                CR12Path              = r.CR12Path?.ToString(),
                CompanyProfilePath    = r.CompanyProfilePath?.ToString(),
                IndemnityDocPath      = r.IndemnityDocPath?.ToString(),
                Notes                 = r.Notes?.ToString(),
                StatusNotes           = r.StatusNotes?.ToString(),
                Status                = status,
                ApplicationStatus     = r.ApplicationStatus?.ToString() ?? "New Application",
                AmendmentFee          = (decimal)(r.AmendmentFee ?? 0),
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