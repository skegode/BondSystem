namespace OnwardsSwift.API.MobileLocal.Contracts;

public class ChequeRequestCreateRequest
{
    public string? Reference { get; set; }
    public string ApplicantName { get; set; } = string.Empty;
    public string? IdNumber { get; set; }
    public string? PostalAddress { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Purpose { get; set; } = string.Empty;
    public bool TermsAccepted { get; set; }
    public string? DeclarantName { get; set; }
    public string? DeclarantRole { get; set; }
    public string? DeclarantDate { get; set; }
}

public class ChequeItemCreateRequest
{
    public long RequestId { get; set; }
    public string ChequeNumber { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Dated { get; set; } = string.Empty;
    public string Drawer { get; set; } = string.Empty;
    public string Bank { get; set; } = string.Empty;
    public string Branch { get; set; } = string.Empty;
    public string Payee { get; set; } = string.Empty;
}

public class OfficialUseCreateRequest
{
    public string? CheckedBy { get; set; }
    public string? CheckedDate { get; set; }
    public string? ConfirmedWith { get; set; }
    public string? Designation { get; set; }
    public string? BuildingStreet { get; set; }
    public string? DrawerStatus { get; set; }
    public string? ReasonForPayment { get; set; }
    public string? AccountConfirmedBy { get; set; }
    public string? AccountStatus { get; set; }
    public string? HeadOfTradeFinance { get; set; }
    public string? HeadOfTradeDate { get; set; }
    public string? InChargeFinance { get; set; }
    public string? InChargeFinanceDate { get; set; }
    public string? Ceo { get; set; }
    public string? CeoDate { get; set; }
    public string? PaidByName { get; set; }
}
