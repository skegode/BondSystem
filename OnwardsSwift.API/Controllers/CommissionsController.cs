using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Infrastructure.Data;
using Dapper;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace OnwardsSwift.API.Controllers
{
    [Authorize]
    public class CommissionsController : AppController
    {
        private readonly DapperContext _ctx;

        public CommissionsController(DapperContext ctx)
        {
            _ctx = ctx;
        }

        // GET: Commissions/Payments - List all pending/settled commission payments
        public async Task<IActionResult> Payments(string? userId, string? paymentStatus, int? year, int? month)
        {
            using var conn = _ctx.Create();

            var selectedMonth = month ?? DateTime.Now.Month;
            var selectedYear = year ?? DateTime.Now.Year;

            var userFilter = !string.IsNullOrEmpty(userId) ? "AND CAST(cp.UserId AS NVARCHAR(50)) = @userId" : string.Empty;
            var yearFilter = "AND cp.PaymentYear = @year";
            var monthFilter = "AND cp.PaymentMonth = @month";

            var paymentsSql = @"
                SELECT 
                    cp.Id,
                    CAST(cp.UserId AS NVARCHAR(50)) AS UserId,
                    u.FullName AS UserName,
                    cp.PaymentMonth,
                    cp.PaymentYear,
                    cp.CommissionBase,
                    cp.CommissionPercent,
                    cp.CommissionAmount,
                    cp.PaidAmount,
                    cp.PaymentStatus,
                    cp.PaymentDate,
                    cp.PaymentReference,
                    cp.Notes,
                    cp.CreatedAt
                FROM CommissionPayments cp
                LEFT JOIN SystemUsers u ON cp.UserId = u.Id
                WHERE ISNULL(cp.IsDeleted, 0) = 0
                  " + userFilter + @"
                  " + yearFilter + @"
                  " + monthFilter + @"
                ORDER BY cp.PaymentYear DESC, cp.PaymentMonth DESC, u.FullName ASC";

            var allPeriodPayments = (await conn.QueryAsync<CommissionPaymentViewModel>(
                paymentsSql,
                new { userId, year = selectedYear, month = selectedMonth }
            )).ToList();

            var payments = string.IsNullOrWhiteSpace(paymentStatus)
                ? allPeriodPayments
                : allPeriodPayments
                    .Where(p => string.Equals(p.PaymentStatus, paymentStatus, StringComparison.OrdinalIgnoreCase))
                    .ToList();

            var users = (await conn.QueryAsync<UserDropdownItem>(@"
                SELECT Id, FullName 
                FROM SystemUsers 
                WHERE ISNULL(IsDeleted, 0) = 0 
                ORDER BY FullName")).ToList();

            var commissionColumns = (await conn.QueryAsync<string>(@"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Bonds'")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hasIsDeleted = commissionColumns.Contains("IsDeleted");
            var hasAgentId = commissionColumns.Contains("AgentId");
            var hasCreatedAt = commissionColumns.Contains("CreatedAt");
            var hasCommissionAmount = commissionColumns.Contains("CommissionAmount");
            var hasBankCharge = commissionColumns.Contains("BankCharge");
            var hasIsApproved = commissionColumns.Contains("isApproved");

            var commissionDateExpr = hasCreatedAt ? "b.CreatedAt" : "GETDATE()";
            var commissionAmountExpr = hasCommissionAmount ? "ISNULL(b.CommissionAmount, 0)" : "0";
            var bankChargeExpr = hasBankCharge ? "ISNULL(b.BankCharge, 0)" : "0";
            var userJoin = hasAgentId
                ? "LEFT JOIN SystemUsers u ON CAST(u.Id AS NVARCHAR(50)) = CAST(b.AgentId AS NVARCHAR(50))"
                : "LEFT JOIN SystemUsers u ON 1 = 0";
            var commissionWhere = new List<string>();
            if (hasCreatedAt)
                commissionWhere.Add("b.CreatedAt BETWEEN @from AND @to");
            if (hasIsDeleted)
                commissionWhere.Add("ISNULL(b.IsDeleted, 0) = 0");
            if (hasIsApproved)
                commissionWhere.Add("ISNULL(b.isApproved, 0) = 1");
            if (hasAgentId && !string.IsNullOrWhiteSpace(userId))
                commissionWhere.Add("CAST(b.AgentId AS NVARCHAR(50)) = @userId");

            var commissionSql = $@"
                SELECT
                    ISNULL(CAST(u.Id AS NVARCHAR(50)), '') AS UserId,
                    ISNULL(u.FullName, 'Unassigned') AS UserName,
                    @month AS PaymentMonth,
                    @year AS PaymentYear,
                    ISNULL(u.CommissionPercent, 0) AS CommissionPercent,
                    SUM(CASE WHEN ({commissionAmountExpr} - {bankChargeExpr}) > 0 THEN ({commissionAmountExpr} - {bankChargeExpr}) ELSE 0 END) AS CommissionBase,
                    SUM(CASE WHEN ({commissionAmountExpr} - {bankChargeExpr}) > 0 THEN ({commissionAmountExpr} - {bankChargeExpr}) * ISNULL(u.CommissionPercent, 0) / 100.0 ELSE 0 END) AS CommissionAmount
                FROM Bonds b
                {userJoin}
                WHERE {string.Join(" AND ", commissionWhere)}
                GROUP BY u.Id, u.FullName, u.CommissionPercent
                ORDER BY u.FullName";

            var commissionSummaries = (await conn.QueryAsync<UserCommissionSummaryForPayment>(
                commissionSql,
                new { month = selectedMonth, year = selectedYear, from = new DateTime(selectedYear, selectedMonth, 1), to = new DateTime(selectedYear, selectedMonth, DateTime.DaysInMonth(selectedYear, selectedMonth), 23, 59, 59), userId }
            )).ToList();

            foreach (var row in commissionSummaries)
            {
                var paidAmount = allPeriodPayments
                    .Where(p => string.Equals(p.UserId, row.UserId, StringComparison.OrdinalIgnoreCase))
                    .Sum(p => p.PaidAmount);

                row.PaidAmount = paidAmount;
                row.OutstandingAmount = Math.Max(0, row.CommissionAmount - paidAmount);
                row.PaymentStatus = row.OutstandingAmount <= 0 && row.CommissionAmount > 0
                    ? "Settled"
                    : paidAmount > 0
                        ? "Partial"
                        : "Pending";
            }

            if (!string.IsNullOrWhiteSpace(paymentStatus))
            {
                commissionSummaries = commissionSummaries
                    .Where(x => string.Equals(x.PaymentStatus, paymentStatus, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            var model = new CommissionPaymentListViewModel
            {
                Payments = payments,
                CalculatedSummaries = commissionSummaries,
                Users = users,
                SelectedMonth = selectedMonth,
                SelectedYear = selectedYear,
                TotalPending = commissionSummaries.Where(p => p.PaymentStatus == "Pending").Sum(p => p.CommissionAmount),
                TotalSettled = commissionSummaries.Where(p => p.PaymentStatus == "Settled").Sum(p => p.CommissionAmount),
                TotalCommissionDue = commissionSummaries.Sum(x => x.CommissionAmount),
                TotalCommissionBase = commissionSummaries.Sum(x => x.CommissionBase),
                TotalPayments = payments.Count,
                TotalUsers = commissionSummaries.Count
            };

            ViewBag.SelectedUserId = userId ?? string.Empty;
            ViewBag.SelectedStatus = paymentStatus;
            ViewBag.SelectedYear = selectedYear;
            ViewBag.SelectedMonth = selectedMonth;

            return View(model);
        }

        // POST: Commissions/ProcessPayment - Mark a commission as paid
        [HttpPost]
        public async Task<IActionResult> ProcessPayment(Guid commissionPaymentId, decimal paidAmount, string paymentReference, string paymentStatus, string notes = "")
        {
            using var conn = _ctx.Create();

            try
            {
                var commissionAmount = await conn.ExecuteScalarAsync<decimal?>(
                    "SELECT CommissionAmount FROM CommissionPayments WHERE Id = @id AND ISNULL(IsDeleted,0)=0",
                    new { id = commissionPaymentId }) ?? 0m;

                var normalizedStatus = paidAmount <= 0
                    ? "Pending"
                    : paidAmount >= commissionAmount
                        ? "Settled"
                        : "Partial";

                var updateSql = @"
                    UPDATE CommissionPayments
                    SET PaidAmount = @paidAmount,
                        PaymentStatus = @paymentStatus,
                        PaymentReference = @paymentReference,
                        PaymentDate = GETDATE(),
                        Notes = @notes,
                        ModifiedAt = GETDATE(),
                        ModifiedBy = @modifiedBy
                    WHERE Id = @id";

                await conn.ExecuteAsync(updateSql, new
                {
                    id = commissionPaymentId,
                    paidAmount,
                    paymentStatus = normalizedStatus,
                    paymentReference,
                    notes,
                    modifiedBy = CurrentUserEmail
                });

                Success($"Commission payment successfully marked as {normalizedStatus.ToLower()}.");
                return RedirectToAction(nameof(Payments));
            }
            catch (Exception ex)
            {
                Error($"Error processing payment: {ex.Message}");
                return RedirectToAction(nameof(Payments));
            }
        }

        [HttpPost]
        public async Task<IActionResult> UpsertPeriodStatus(
            string userId,
            int month,
            int year,
            decimal commissionBase,
            decimal commissionPercent,
            decimal commissionAmount,
            decimal paidAmount,
            string paymentStatus,
            string? paymentReference,
            string? notes,
            string? returnUserId,
            string? returnPaymentStatus)
        {
            using var conn = _ctx.Create();

            if (string.IsNullOrWhiteSpace(userId))
            {
                Error("User is required.");
                return RedirectToAction(nameof(Payments), new { userId = returnUserId, paymentStatus = returnPaymentStatus, month, year });
            }

            try
            {
                paidAmount = Math.Max(0, paidAmount);
                if (paidAmount > commissionAmount)
                    paidAmount = commissionAmount;

                if (string.Equals(paymentStatus, "Pending", StringComparison.OrdinalIgnoreCase))
                    paidAmount = 0;
                else if (string.Equals(paymentStatus, "Settled", StringComparison.OrdinalIgnoreCase))
                    paidAmount = commissionAmount;

                var existing = await conn.QueryFirstOrDefaultAsync<Guid?>(@"
                    SELECT TOP 1 Id
                    FROM CommissionPayments
                    WHERE PaymentMonth = @month
                      AND PaymentYear = @year
                      AND CAST(UserId AS NVARCHAR(50)) = @userId
                      AND ISNULL(IsDeleted, 0) = 0", new { userId, month, year });

                if (existing.HasValue)
                {
                    await conn.ExecuteAsync(@"
                        UPDATE CommissionPayments
                        SET CommissionBase = @commissionBase,
                            CommissionPercent = @commissionPercent,
                            CommissionAmount = @commissionAmount,
                            PaidAmount = @paidAmount,
                            PaymentStatus = @paymentStatus,
                            PaymentReference = @paymentReference,
                            Notes = @notes,
                            PaymentDate = GETDATE(),
                            ModifiedAt = GETDATE()
                        WHERE Id = @id", new
                    {
                        id = existing.Value,
                        commissionBase,
                        commissionPercent,
                        commissionAmount,
                        paidAmount,
                        paymentStatus,
                        paymentReference = paymentReference ?? string.Empty,
                        notes = notes ?? string.Empty
                    });
                }
                else
                {
                    await conn.ExecuteAsync(@"
                        INSERT INTO CommissionPayments
                        (Id, UserId, PaymentMonth, PaymentYear, CommissionBase, CommissionPercent, CommissionAmount, PaidAmount, PaymentStatus, PaymentReference, Notes, PaymentDate)
                        VALUES
                        (@id, @userId, @month, @year, @commissionBase, @commissionPercent, @commissionAmount, @paidAmount, @paymentStatus, @paymentReference, @notes, GETDATE())", new
                    {
                        id = Guid.NewGuid(),
                        userId,
                        month,
                        year,
                        commissionBase,
                        commissionPercent,
                        commissionAmount,
                        paidAmount,
                        paymentStatus,
                        paymentReference = paymentReference ?? string.Empty,
                        notes = notes ?? string.Empty
                    });
                }

                Success("Commission status updated for the selected period.");
            }
            catch (Exception ex)
            {
                Error($"Could not update commission status: {ex.Message}");
            }

            return RedirectToAction(nameof(Payments), new { userId = returnUserId, paymentStatus = returnPaymentStatus, month, year });
        }

        // GET: Commissions/Calculate - Calculate commissions for a specific month/year
        public async Task<IActionResult> Calculate(int? month, int? year)
        {
            var selectedMonth = month ?? DateTime.Now.Month;
            var selectedYear = year ?? DateTime.Now.Year;

            using var conn = _ctx.Create();

            var bondColumns = (await conn.QueryAsync<string>(@"
                SELECT COLUMN_NAME
                FROM INFORMATION_SCHEMA.COLUMNS
                WHERE TABLE_NAME = 'Bonds'")).ToHashSet(StringComparer.OrdinalIgnoreCase);

            var hasIsDeleted = bondColumns.Contains("IsDeleted");
            var hasAgentId = bondColumns.Contains("AgentId");
            var hasCreatedAt = bondColumns.Contains("CreatedAt");
            var hasCommissionAmount = bondColumns.Contains("CommissionAmount");
            var hasBankCharge = bondColumns.Contains("BankCharge");

            var commissionExpr = hasCommissionAmount ? "ISNULL(b.CommissionAmount, 0)" : "0";
            var bankChargeExpr = hasBankCharge ? "ISNULL(b.BankCharge, 0)" : "0";
            var userJoin = hasAgentId
                ? "LEFT JOIN SystemUsers u ON CAST(u.Id AS NVARCHAR(50)) = CAST(b.AgentId AS NVARCHAR(50))"
                : "LEFT JOIN SystemUsers u ON 1 = 0";

            var whereConditions = new List<string>();
            if (hasCreatedAt)
            {
                whereConditions.Add("MONTH(b.CreatedAt) = @month");
                whereConditions.Add("YEAR(b.CreatedAt) = @year");
            }
            if (hasIsDeleted)
                whereConditions.Add("ISNULL(b.IsDeleted, 0) = 0");

            // Query to get commission data for the specified month
            var sql = $@"
                SELECT 
                    ISNULL(CAST(u.Id AS NVARCHAR(50)), '') AS UserId,
                    ISNULL(u.FullName, 'Unassigned') AS UserName,
                    ISNULL(u.CommissionPercent, 0) AS CommissionPercent,
                    SUM(CASE WHEN ({commissionExpr} - {bankChargeExpr}) > 0 
                        THEN ({commissionExpr} - {bankChargeExpr}) 
                        ELSE 0 END) AS CommissionBase,
                    SUM(CASE WHEN ({commissionExpr} - {bankChargeExpr}) > 0 
                        THEN ({commissionExpr} - {bankChargeExpr}) * ISNULL(u.CommissionPercent, 0) / 100.0
                        ELSE 0 END) AS CommissionAmount
                FROM Bonds b
                {userJoin}
                {(whereConditions.Count > 0 ? "WHERE " + string.Join(" AND ", whereConditions) : string.Empty)}
                GROUP BY u.Id, u.FullName, u.CommissionPercent";

            var commissions = (await conn.QueryAsync<UserCommissionSummaryForPayment>(
                sql,
                new { month = selectedMonth, year = selectedYear }
            )).ToList();

            // Check if records already exist for this period
            var existingSql = @"
                SELECT COUNT(*) 
                FROM CommissionPayments 
                WHERE PaymentMonth = @month 
                  AND PaymentYear = @year";

            var existingCount = await conn.ExecuteScalarAsync<int>(existingSql, new { month = selectedMonth, year = selectedYear });

            if (existingCount > 0)
            {
                TempData["Warning"] = $"Commission payments for {selectedMonth:D2}/{selectedYear} already exist. Please review and update manually.";
            }

            return View(commissions);
        }

        // POST: Commissions/CreatePayments - Bulk create commission payment records for a month
        [HttpPost]
        public async Task<IActionResult> CreatePayments(int month, int year)
        {
            using var conn = _ctx.Create();

            try
            {
                var bondColumns = (await conn.QueryAsync<string>(@"
                    SELECT COLUMN_NAME
                    FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'Bonds'")).ToHashSet(StringComparer.OrdinalIgnoreCase);

                var hasIsDeleted = bondColumns.Contains("IsDeleted");
                var hasAgentId = bondColumns.Contains("AgentId");
                var hasCreatedAt = bondColumns.Contains("CreatedAt");
                var hasCommissionAmount = bondColumns.Contains("CommissionAmount");
                var hasBankCharge = bondColumns.Contains("BankCharge");

                var commissionExpr = hasCommissionAmount ? "ISNULL(b.CommissionAmount, 0)" : "0";
                var bankChargeExpr = hasBankCharge ? "ISNULL(b.BankCharge, 0)" : "0";
                var userJoin = hasAgentId
                    ? "LEFT JOIN SystemUsers u ON CAST(u.Id AS NVARCHAR(50)) = CAST(b.AgentId AS NVARCHAR(50))"
                    : "LEFT JOIN SystemUsers u ON 1 = 0";

                var whereConditions = new List<string>();
                if (hasCreatedAt)
                {
                    whereConditions.Add("MONTH(b.CreatedAt) = @month");
                    whereConditions.Add("YEAR(b.CreatedAt) = @year");
                }
                if (hasIsDeleted)
                    whereConditions.Add("ISNULL(b.IsDeleted, 0) = 0");
                whereConditions.Add("u.Id IS NOT NULL");

                // Query to get commission data for the specified month
                var sql = $@"
                    SELECT 
                        CAST(u.Id AS NVARCHAR(50)) AS UserId,
                        u.CommissionPercent,
                        SUM(CASE WHEN ({commissionExpr} - {bankChargeExpr}) > 0 
                            THEN ({commissionExpr} - {bankChargeExpr}) 
                            ELSE 0 END) AS CommissionBase,
                        SUM(CASE WHEN ({commissionExpr} - {bankChargeExpr}) > 0 
                            THEN ({commissionExpr} - {bankChargeExpr}) * u.CommissionPercent / 100.0
                            ELSE 0 END) AS CommissionAmount
                    FROM Bonds b
                    {userJoin}
                    WHERE {string.Join(" AND ", whereConditions)}
                    GROUP BY u.Id, u.CommissionPercent";

                var commissions = (await conn.QueryAsync<dynamic>(
                    sql,
                    new { month, year }
                )).ToList();

                int insertedCount = 0;
                int updatedCount = 0;
                const string insertSql = @"
                    INSERT INTO CommissionPayments 
                    (Id, UserId, PaymentMonth, PaymentYear, CommissionBase, CommissionPercent, CommissionAmount, PaymentStatus, CreatedBy)
                    VALUES 
                    (@Id, @UserId, @PaymentMonth, @PaymentYear, @CommissionBase, @CommissionPercent, @CommissionAmount, 'Pending', @CreatedBy)";

                const string updateSql = @"
                    UPDATE CommissionPayments
                    SET CommissionBase = @CommissionBase,
                        CommissionPercent = @CommissionPercent,
                        CommissionAmount = @CommissionAmount,
                        PaymentStatus = CASE
                            WHEN ISNULL(PaidAmount,0) <= 0 THEN 'Pending'
                            WHEN ISNULL(PaidAmount,0) >= @CommissionAmount THEN 'Settled'
                            ELSE 'Partial'
                        END,
                        ModifiedAt = GETDATE(),
                        ModifiedBy = @ModifiedBy
                    WHERE PaymentMonth = @PaymentMonth
                      AND PaymentYear = @PaymentYear
                      AND CAST(UserId AS NVARCHAR(50)) = @UserId
                      AND ISNULL(IsDeleted,0) = 0";

                foreach (var commission in commissions)
                {
                    if (commission.CommissionAmount > 0)
                    {
                        var affected = await conn.ExecuteAsync(updateSql, new
                        {
                            UserId = (string)commission.UserId,
                            PaymentMonth = month,
                            PaymentYear = year,
                            CommissionBase = (decimal)commission.CommissionBase,
                            CommissionPercent = (decimal)commission.CommissionPercent,
                            CommissionAmount = (decimal)commission.CommissionAmount,
                            ModifiedBy = CurrentUserEmail
                        });

                        if (affected > 0)
                        {
                            updatedCount++;
                            continue;
                        }

                        await conn.ExecuteAsync(insertSql, new
                        {
                            Id = Guid.NewGuid(),
                            UserId = (string)commission.UserId,
                            PaymentMonth = month,
                            PaymentYear = year,
                            CommissionBase = (decimal)commission.CommissionBase,
                            CommissionPercent = (decimal)commission.CommissionPercent,
                            CommissionAmount = (decimal)commission.CommissionAmount,
                            CreatedBy = CurrentUserEmail
                        });

                        insertedCount++;
                    }
                }

                Success($"Commission payments synced for {month:D2}/{year}: {insertedCount} created, {updatedCount} updated.");
                return RedirectToAction(nameof(Payments));
            }
            catch (Exception ex)
            {
                Error($"Error creating commission payments: {ex.Message}");
                return RedirectToAction(nameof(Payments));
            }
        }
    }
}
