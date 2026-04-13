using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnwardsSwift.Core.DTOs
{
    public class LedgerLine
    {
        public DateTime CreatedAt { get; set; }
        public string TransactionType { get; set; } = "";
        public string Description { get; set; } = "";
        public string EntryType { get; set; } = "";
        public decimal Amount { get; set; }
        public int ReferenceId { get; set; }
    }
}
