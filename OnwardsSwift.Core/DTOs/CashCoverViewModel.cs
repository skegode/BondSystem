using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnwardsSwift.Core.DTOs
{
    public class CashCoverViewModel
    {

        public int Id { get; set; }
        public string BondRef { get; set; } = "";
        public string ClientName { get; set; } = "";
        public decimal BondAmount { get; set; }
        public decimal CashCoverAmount { get; set; }
        public decimal? CashCoverPct { get; set; }
        public DateTime? MaturityDate { get; set; }
        public int DaysRemaining { get; set; }
        public string Status { get; set; } = "";
        public string HoldingBank { get; set; } = "";
        public string TenderName { get; set; } = "";
    }
}
