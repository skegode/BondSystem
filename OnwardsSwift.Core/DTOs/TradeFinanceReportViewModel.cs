using System;
using System.Collections.Generic;

namespace OnwardsSwift.Core.DTOs
{
    public class TradeFinanceReportViewModel
    {
        public DateTime FromDate { get; set; }
        public DateTime ToDate { get; set; }
        public int TotalApplications { get; set; }
        public decimal TotalBondAmount { get; set; }
        public decimal TotalClientCharges { get; set; }
        public decimal TotalTaxCalculation { get; set; }
        public decimal TotalBankCharges { get; set; }
        public decimal TotalNetProfit { get; set; }
        public List<TradeFinanceReportLine> Details { get; set; } = new();
    }

    public class TradeFinanceReportLine
    {
        public DateTime ApplicationDate { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public decimal BondAmount { get; set; }
        public int TenorDays { get; set; }
        public string IssuingBank { get; set; } = string.Empty;
        public decimal ClientCharges { get; set; }
        public decimal BankCharges { get; set; }
        public decimal TaxCalculation { get; set; }
        public decimal NetProfit { get; set; }
    }

    // --- Pending Client Charges Report ---

    public class PendingClientChargesViewModel
    {
        public int TotalClients { get; set; }
        public int TotalBonds { get; set; }
        public decimal TotalClientCharges { get; set; }
        public decimal TotalBankCharges { get; set; }
        public decimal TotalTaxCalculation { get; set; }
        public decimal TotalNetProfit { get; set; }
        public List<PendingBondLine> Bonds { get; set; } = new();
    }

    public class PendingClientSummary
    {
        public string ClientName { get; set; } = string.Empty;
        public int Applications { get; set; }
        public decimal TotalClientCharges { get; set; }
        public decimal TotalBankCharges { get; set; }
        public decimal TotalTaxCalculation { get; set; }
        public decimal TotalNetProfit { get; set; }
        public List<PendingBondLine> Bonds { get; set; } = new();
    }

    public class PendingBondLine
    {
        public DateTime ApplicationDate { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public decimal BondAmount { get; set; }
        public int TenorDays { get; set; }
        public string IssuingBank { get; set; } = string.Empty;
        public decimal ClientCharges { get; set; }
        public decimal BankCharges { get; set; }
        public decimal TaxCalculation { get; set; }
        public decimal NetProfit { get; set; }
    }
}
