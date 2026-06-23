using System;
using System.Collections.Generic;

namespace OnwardsSwift.Core.DTOs
{
    public class CommissionPaymentViewModel
    {
        public Guid Id { get; set; }
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public int PaymentMonth { get; set; }
        public int PaymentYear { get; set; }
        public decimal CommissionBase { get; set; }
        public decimal CommissionPercent { get; set; }
        public decimal CommissionAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public string PaymentStatus { get; set; } = "Pending";  // Pending, Partial, Settled
        public DateTime? PaymentDate { get; set; }
        public string PaymentReference { get; set; } = string.Empty;
        public string Notes { get; set; } = string.Empty;
        public DateTime CreatedAt { get; set; }
        public string Period => $"{PaymentMonth:D2}/{PaymentYear}";
        public decimal RemainingAmount => CommissionAmount - PaidAmount;
    }

    public class CommissionPaymentListViewModel
    {
        public List<CommissionPaymentViewModel> Payments { get; set; } = new();
        public List<UserCommissionSummaryForPayment> CalculatedSummaries { get; set; } = new();
        public List<UserDropdownItem> Users { get; set; } = new();
        public int SelectedMonth { get; set; }
        public int SelectedYear { get; set; }
        public decimal TotalPending { get; set; }
        public decimal TotalSettled { get; set; }
        public decimal TotalCommissionDue { get; set; }
        public decimal TotalCommissionBase { get; set; }
        public int TotalPayments { get; set; }
        public int TotalUsers { get; set; }
    }

    public class ProcessCommissionPaymentRequest
    {
        public Guid CommissionPaymentId { get; set; }
        public decimal PaidAmount { get; set; }
        public string PaymentReference { get; set; } = string.Empty;
        public string PaymentStatus { get; set; } = "Settled";  // Pending, Partial, Settled
        public string Notes { get; set; } = string.Empty;
    }

    public class UserCommissionSummaryForPayment
    {
        public string UserId { get; set; } = string.Empty;
        public string UserName { get; set; } = string.Empty;
        public int PaymentMonth { get; set; }
        public int PaymentYear { get; set; }
        public decimal CommissionBase { get; set; }
        public decimal CommissionPercent { get; set; }
        public decimal CommissionAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal OutstandingAmount { get; set; }
        public string PaymentStatus { get; set; } = "Pending";
        public string Period => $"{PaymentMonth:D2}/{PaymentYear}";
    }
}
