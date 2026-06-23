using BCrypt.Net;
using Microsoft.Extensions.Options;
using OnwardsSwift.API.MobileLocal.Configuration;
using System.Security.Cryptography;

namespace OnwardsSwift.API.MobileLocal.Services;

public class OtpService
{
    private readonly OtpOptions _options;

    public OtpService(IOptions<LocalApiOptions> options)
    {
        _options = options.Value.Otp;
    }

    public string GenerateOtpCode()
    {
        var min = (int)Math.Pow(10, _options.Length - 1);
        var max = (int)Math.Pow(10, _options.Length) - 1;
        var value = RandomNumberGenerator.GetInt32(min, max + 1);
        return value.ToString();
    }

    public string HashOtp(string otp)
    {
        return BCrypt.Net.BCrypt.HashPassword(otp);
    }

    public bool VerifyOtp(string otp, string hash)
    {
        return BCrypt.Net.BCrypt.Verify(otp, hash);
    }
}
