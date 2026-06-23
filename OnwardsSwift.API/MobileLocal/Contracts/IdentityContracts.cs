namespace OnwardsSwift.API.MobileLocal.Contracts;

public class IprsVerifyRequest
{
    public string IdNumber { get; set; } = string.Empty;
    public string FirstName { get; set; } = string.Empty;
    public string? MiddleName { get; set; }
    public string LastName { get; set; } = string.Empty;
    public string? DateOfBirth { get; set; }
}

public class KraLookupRequest
{
    public string KraPin { get; set; } = string.Empty;
    public string? TaxpayerName { get; set; }
}
