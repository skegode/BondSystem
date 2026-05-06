using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnwardsSwift.Core.DTOs
{
    public class DashboardViewModel
    {
        // ── KPI counters ────────────────────────────────────────
        public int     ActiveBonds      { get; set; }
        public int     PendingReview    { get; set; }
        public int     CashCovers       { get; set; }
        public int     IssuingBanks     { get; set; }
        public decimal TotalExposure    { get; set; }
        public decimal CommissionEarned { get; set; }
        public int     TotalClients     { get; set; }
        public int     PendingApprovals { get; set; }
        public decimal ChequePortfolio  { get; set; }
        public int     ChequeCount      { get; set; }

        // ── Recent table ────────────────────────────────────────
        public List<RecentBondDto> RecentBonds { get; set; } = new();

        // ── Chart data ──────────────────────────────────────────
        // Monthly bond submissions (last 6 months)
        public List<MonthlyStatDto>   MonthlySubs     { get; set; } = new();
        // Monthly revenue / commission
        public List<MonthlyStatDto>   MonthlyRevenue  { get; set; } = new();
        // Bond status breakdown
        public List<StatusStatDto>    StatusBreakdown { get; set; } = new();
        // Top 5 clients by exposure
        public List<ClientExposureDto> TopClients     { get; set; } = new();
        // Bond type distribution
        public List<BondTypeStat>     Distribution    { get; set; } = new();
    }

    public class MonthlyStatDto
    {
        public string  MonthLabel { get; set; } = string.Empty;
        public int     Count      { get; set; }
        public decimal Amount     { get; set; }
    }

    public class StatusStatDto
    {
        public string  Label  { get; set; } = string.Empty;
        public int     Count  { get; set; }
        public decimal Amount { get; set; }
    }

    public class ClientExposureDto
    {
        public string  ClientName { get; set; } = string.Empty;
        public int     BondCount  { get; set; }
        public decimal Exposure   { get; set; }
    }

    public class BondTypeStat
    {
        public string  TypeName   { get; set; } = string.Empty;
        public int     Count      { get; set; }
        public decimal Exposure   { get; set; }
        public double  Percentage { get; set; }
    }

    public class RecentBondDto
    {
        public int      Id        { get; set; }
        public string   Reference { get; set; } = string.Empty;
        public string   Applicant { get; set; } = string.Empty;
        public string   Bank      { get; set; } = string.Empty;
        public decimal  Value     { get; set; }
        public string   Status    { get; set; } = string.Empty;
        public DateTime Expiry    { get; set; }
    }
}
