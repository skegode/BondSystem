using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnwardsSwift.Core.DTOs
{
    public class RevenueReportViewModel
    {

        // Summary totals for the top of the report
        public decimal TotalRevenue { get; set; }

        // Optional: Breakdown by specific categories if needed later
        public decimal BidBondFees { get; set; }

        // The list of actual ledger entries to display in the table
        public List<RevenueLine> Details { get; set; } = new List<RevenueLine>();
    }
    public class RevenueLine
    {
        public DateTime Date { get; set; }
        public string Reference { get; set; } = ""; // Invoice Number
        public string ClientName { get; set; } = "";
        public string Description { get; set; } = "";
        public string BondNumber { get; set; } = ""; // Added
        public decimal Amount { get; set; }
    }
}
