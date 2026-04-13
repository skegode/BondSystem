using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnwardsSwift.Core.DTOs
{
    public class PendingBondVM
    {
        public int Id { get; set; }
        public string TenderNumber { get; set; } = string.Empty;
        public string TenderName { get; set; } = string.Empty;
        public string ClientName { get; set; } = string.Empty; // Joined from Clients
        public string BondTypeName { get; set; } = string.Empty; // Joined from BondTypes
        public decimal Amount { get; set; }
        public DateTime TenderClosingDate { get; set; }
        public bool IsDeferredPayment { get; set; }
        public DateTime CreatedAt { get; set; }
    }
}
