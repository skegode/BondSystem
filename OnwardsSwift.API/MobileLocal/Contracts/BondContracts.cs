namespace OnwardsSwift.API.MobileLocal.Contracts;

public class BondApplicationCreateRequest
{
    public string? Reference { get; set; }
    public string ApplicantName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? IdNumber { get; set; }
    public string? TenderName { get; set; }
    public string? TenderNumber { get; set; }
    public string? ProcuringEntity { get; set; }
    public decimal? Amount { get; set; }
    public string? Currency { get; set; }
    public int? TenorDays { get; set; }
    public string? IndemnityText { get; set; }
    public List<BondApplicationTypeRequest> Types { get; set; } = new();
}

public class BondApplicationTypeRequest
{
    public string TypeCode { get; set; } = string.Empty;
    public string TypeName { get; set; } = string.Empty;
}

public class BondSignatoryRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? Designation { get; set; }
    public string? Phone { get; set; }
    public string? IdNumber { get; set; }
}

public class BondIndemnitorRequest
{
    public string FullName { get; set; } = string.Empty;
    public string? IdNumber { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
}

public class BondIndemnityRequest
{
    public string? IndemnityText { get; set; }
    public List<BondSignatoryRequest> Signatories { get; set; } = new();
    public List<BondIndemnitorRequest> Indemnitors { get; set; } = new();
}
