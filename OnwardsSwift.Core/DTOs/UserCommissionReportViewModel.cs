using System;
using System.Collections.Generic;

namespace OnwardsSwift.Core.DTOs
{
    public class UserCommissionReportViewModel
    {
        public decimal TotalCommission { get; set; }
        public decimal TotalCommissionBase { get; set; }
        public int TotalApplications { get; set; }
        public List<UserCommissionLine> Details { get; set; } = new();
        public List<UserCommissionSummaryLine> Summary { get; set; } = new();
        public List<UserDropdownItem> Users { get; set; } = new();
    }

    public class UserDropdownItem
    {
        public string Id { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
    }

    public class UserCommissionLine
    {
        public DateTime ApplicationDate { get; set; }
        public string Reference { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string ProcuringEntity { get; set; } = string.Empty;
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public decimal CommissionPercent { get; set; }
        public decimal ClientCharges { get; set; }
        public decimal BankCharges { get; set; }
        public int FacilityType { get; set; }
        public decimal CommissionBase { get; set; }
        public decimal CommissionAmount { get; set; }
    }

    public class UserCommissionSummaryLine
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public decimal CommissionPercent { get; set; }
        public int Applications { get; set; }
        public decimal ClientCharges { get; set; }
        public decimal BankCharges { get; set; }
        public decimal CommissionBase { get; set; }
        public decimal CommissionAmount { get; set; }
    }
}