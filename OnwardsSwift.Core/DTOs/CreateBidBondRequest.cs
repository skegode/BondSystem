using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnwardsSwift.Core.DTOs
{
    public class CreateBidBondRequest
    {
        // --- STEP 1: PARTIES & PRODUCT ---
        [Required(ErrorMessage = "Please select a client")]
        public int ClientId { get; set; }

        public int? AgentId { get; set; }

        [Required(ErrorMessage = "Please select a bond type")]
        public int BondTypeId { get; set; }

        [Required(ErrorMessage = "Please select application status")]
        public string ApplicationStatus { get; set; } = "New Application";

        [Required(ErrorMessage = "Procuring Entity is required"), MaxLength(300)]
        public string ProcuringEntity { get; set; } = string.Empty;


        // --- STEP 2: FINANCIAL DETAILS ---
        [Required(ErrorMessage = "Tender Name is required"), MaxLength(250)]
        public string TenderName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Tender Number is required"), MaxLength(100)]
        public string TenderNumber { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please select an issuing bank")]
        public int IssuingBank { get; set; }

        [Required, Range(1000, 500_000_000, ErrorMessage = "Amount must be between 1,000 and 500M")]
        public decimal Amount { get; set; }

        [Required]
        public DateTime TenderClosingDate { get; set; } // <--- RESTORED FIELD

        public DateTime? ProcessingDate { get; set; }

        [Required, Range(30, 1825)]
        public int TenorDays { get; set; }


        // --- STEP 3: SECURITY / CASH COVER ---
        public bool HasCashCover { get; set; }
        public decimal? CashCoverAmount { get; set; }
        public int? CashCoverBankId { get; set; }
        public string? CashCoverReference { get; set; }
        public string? CashCoverReceiptPath { get; set; }
        public DateTime? CashCoverDueDate { get; set; }


        // --- STEP 4: PAYMENT & ATTACHMENTS ---
        public bool IsDeferredPayment { get; set; }
        public bool IsPaid { get; set; }
        public decimal? AmountPaid { get; set; }
        public int? PaymentBankId { get; set; }
        public string? PaymentReference { get; set; }
        public string? PaymentMethod { get; set; }
        public string? PaymentNotes { get; set; }
        public string? PaymentReceiptPath { get; set; }

        // File Upload Paths
        public string? TenderDocPath { get; set; }
        public string? CR12Path { get; set; }
        public string? CompanyProfilePath { get; set; }
        public string? IndemnityDocPath { get; set; }


        // --- HIDDEN CALCULATION FIELDS ---
        public decimal AppliedRate { get; set; }
        public decimal ApplicationFee { get; set; }
        public decimal CommissionAmount { get; set; }
        // Client & Bank charge inputs
        public decimal ClientCharges { get; set; }
        public decimal AmendmentFee { get; set; }
        public decimal BankCharges { get; set; }
        public decimal TaxPercentage { get; set; }
        public decimal TaxCalculation { get; set; }
        public decimal TotalBankCharge { get; set; }
        // Computed
        public decimal NetProfit { get; set; }


        // --- EXTRA METADATA ---
        public string? ClientName { get; set; }
        public string? Notes { get; set; }
        public string? DisbursementAccount { get; set; }
        public string? DisbursementBank { get; set; }
    }
}
