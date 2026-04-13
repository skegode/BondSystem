using System;

namespace OnwardsSwift.Core.DTOs
{
    public class BidBondResponse
    {
        // --- EXISTING CORE FIELDS ---
        public int Id { get; set; }
        public string ReferenceNo { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty;
        public string TenderNumber { get; set; } = string.Empty;
        public string ProcuringEntity { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public decimal Rate { get; set; }
        public int TenorDays { get; set; }
        public string IssuingBank { get; set; } = string.Empty;
        public DateTime TenderClosingDate { get; set; }
        public string Status { get; set; }
        public string? BondNumber { get; set; }
        public DateTime CreatedAt { get; set; }

        // --- NEW ENRICHED FIELDS FOR REVIEW & APPROVAL ---

        // 1. Calculations
        public decimal CommissionFee { get; set; }     // The calculated commission
        public decimal ApplicationFee { get; set; }    // The processing fee charged to client
        public decimal BankCharge { get; set; }       // What the bank charges us (for net profit)

        // 2. Tender & Project Info
        public string TenderName { get; set; } = string.Empty;
        public string? Notes { get; set; }             // User notes from application
        public string? StatusNotes { get; set; }       // Manager remarks from approval

        // 3. Document Tracking
        public string? TenderDocPath { get; set; }
        public string? CR12Path { get; set; }
        public string? CompanyProfilePath { get; set; }
        public string? IndemnityDocPath { get; set; }
        public string? BondTypeName { get; set; }


        // 4. Payment & Deferred Logic
        public bool IsDeferredPayment { get; set; }    // The "Pay Later" indicator
        public string? PaymentReference { get; set; }  // M-Pesa/Bank Ref
        public string? PaymentReceiptPath { get; set; } // Path to fee receipt
        public int? PaymentBankId { get; set; }        // ID of the internal collection bank

        // 5. Audit Details
        public string? CreatedBy { get; set; }
        public string? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }

        // --- RESALE & LEGACY SUPPORT ---
        public bool IsResold { get; set; }
        public string? ResalePartner { get; set; }
        public DateTime? DisbursedAt { get; set; }

        public string? ProcuringEntityName { get; set; }
        public string? AgentName { get; set; }
        public decimal PayableCharge { get; set; } // The combined fee for display

        public bool HasCashCover { get; set; }
        public decimal? CashCoverAmount { get; set; }
        public string? CashCoverReceiptRef { get; set; }
        public string? CashCoverReceiptPath { get; set; } // Added
        public DateTime? CashCoverMaturityDate { get; set; } // Added
        public DateTime? CashCoverDate { get; set; }
    }
}