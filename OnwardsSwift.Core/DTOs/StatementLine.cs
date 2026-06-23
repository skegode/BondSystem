using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnwardsSwift.Core.DTOs
{
    public class StatementLine
    {
        public DateTime TransactionDate { get; set; }
        public string Description { get; set; } = string.Empty;
        public string ProductItem { get; set; } = string.Empty;
        public string ProcuringEntity { get; set; } = string.Empty;
        public string ReferenceNo { get; set; } = string.Empty;
        public decimal BondAmount { get; set; }
        public decimal CashCoverAmount { get; set; }
        public decimal Debit { get; set; }
        public decimal Credit { get; set; }
        public decimal RunningBalance { get; set; }
        public bool IsPaymentLine { get; set; }
        public int? ParentBondId { get; set; }
        public int DisplayOrder { get; set; }
    }
}
