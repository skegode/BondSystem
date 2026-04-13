using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnwardsSwift.Core.DTOs
{
    public class BankChargeViewModel
    {
        // Total sum of all charges for the selected period
        public decimal TotalCharges { get; set; }

        // Optional: Count of charge entries (useful for high-volume analysis)
        public int TransactionCount => Details?.Count ?? 0;

        // The list of charge entries from the General Ledger
        public List<BankChargeLine> Details { get; set; } = new List<BankChargeLine>();
    }

    public class BankChargeLine
    {
        public DateTime Date { get; set; }
        public string Reference { get; set; } = ""; // Invoice Number
        public string RelatedEntity { get; set; } = "";
        public string BondNumber { get; set; } = ""; // Added
        public string Description { get; set; } = "";
        public decimal Amount { get; set; }
    }
}
