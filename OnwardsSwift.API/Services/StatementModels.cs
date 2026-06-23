using System;

namespace OnwardsSwift.API.Services
{
    public class StatementInvoice
    {
        public string InvoiceNumber { get; set; } = string.Empty;
        public decimal Amount { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class StatementBond
    {
        public string BondNumber { get; set; } = string.Empty;
        public decimal ApplicationFee { get; set; }
        public decimal BankCharge { get; set; }
        public DateTime CreatedAt { get; set; }
    }

    public class InvoicePdfLine
    {
        public string InvoiceNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string ProductItem { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Amount { get; set; }
        public decimal ChargedAmount { get; set; }
        public decimal PaidAmount { get; set; }
        public decimal OutstandingAmount { get; set; }
        public DateTime InvoiceDate { get; set; }
        public DateTime DueDate { get; set; }
    }
}
