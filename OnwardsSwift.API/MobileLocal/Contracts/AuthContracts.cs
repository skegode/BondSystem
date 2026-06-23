namespace OnwardsSwift.API.MobileLocal.Contracts;

using System.Text.Json.Serialization;

public class SignupRequest
{
    public string NationalId { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
    public bool IprsVerified { get; set; }
    public string? Email { get; set; }
    public string? KraPin { get; set; }
    public string? Gender { get; set; }
    public string? PostalAddress { get; set; }
    public string? PhysicalAddress { get; set; }
    public string? IprsReference { get; set; }
}

public class SigninRequest
{
    public string Phone { get; set; } = string.Empty;
    public string Pin { get; set; } = string.Empty;
}

public class PinResetRequest
{
    public string? Phone { get; set; }
    public string? PhoneRaw { get; set; }
    public string? Email { get; set; }
    public string? Identifier { get; set; }
    public string? NationalId { get; set; }
    public string NewPin { get; set; } = string.Empty;
    [JsonPropertyName("new_pin")]
    public string? NewPinAlt { get; set; }
    public string? RequestId { get; set; }
}

public class OtpRequestCommand
{
    public string? Phone { get; set; }
    public string? Identifier { get; set; }
    public string Purpose { get; set; } = "signin";
    public string? Email { get; set; }
    public List<string>? DeliveryChannels { get; set; }
}

public class OtpVerifyCommand
{
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Identifier { get; set; }
    public string? RequestId { get; set; }
    public string Purpose { get; set; } = "signin";
    public string Otp { get; set; } = string.Empty;
}

public class LogoutRequest
{
    public string? Jti { get; set; }
}

public class UpdateProfileRequest
{
    public string Email { get; set; } = string.Empty;
    public string? PostalAddress { get; set; }
    public string? PhysicalAddress { get; set; }
    public string? AlternativePhone { get; set; }
    public string? PlaceOfWork { get; set; }
    public string? WorkTelephone { get; set; }
    public string? WorkPhysicalAddress { get; set; }
    public string? ClientSignature { get; set; }
}
