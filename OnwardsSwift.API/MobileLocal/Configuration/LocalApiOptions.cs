namespace OnwardsSwift.API.MobileLocal.Configuration;

public class LocalApiOptions
{
    public string SqlitePath { get; set; } = "App_Data/onwardsswift.local.db";
    public string UploadsSubFolder { get; set; } = "mobile";
    public OtpOptions Otp { get; set; } = new();
}

public class OtpOptions
{
    public int Length { get; set; } = 6;
    public int ExpiryMinutes { get; set; } = 5;
    public int MaxAttempts { get; set; } = 5;
}
