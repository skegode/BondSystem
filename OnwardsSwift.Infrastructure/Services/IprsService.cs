using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace OnwardsSwift.Infrastructure.Services
{
    /// <summary>
    /// Service for integrating with Spinmobile IPRS (Kenya Integrated Population Registration System)
    /// </summary>
    public interface IIprsService
    {
        Task<IprsVerificationResult> VerifyIdentityAsync(string idNumber, string fullName, string consent = "true", string consentCollectedBy = "system");
        Task<CompanyVerificationResult> VerifyCompanyAsync(string registrationNumber, string consent = "1", string consentCollectedBy = "system");
    }

    public class IprsService : IIprsService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;
        private readonly ILogger<IprsService> _logger;
        private string _accessToken;
        private DateTime _tokenExpiry;

        public IprsService(HttpClient httpClient, IConfiguration configuration, ILogger<IprsService> logger)
        {
            _httpClient = httpClient;
            _configuration = configuration;
            _logger = logger;
            _accessToken = string.Empty;
            _tokenExpiry = DateTime.MinValue;
        }

        public async Task<IprsVerificationResult> VerifyIdentityAsync(string idNumber, string fullName, string consent = "true", string consentCollectedBy = "system")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(idNumber))
                {
                    return new IprsVerificationResult
                    {
                        Success = false,
                        Message = "ID Number is required",
                        Reference = null
                    };
                }

                // Ensure we have a valid access token
                if (string.IsNullOrWhiteSpace(_accessToken) || DateTime.UtcNow >= _tokenExpiry)
                {
                    var tokenResult = await GetAccessTokenAsync();
                    if (!tokenResult.Success)
                    {
                        _logger.LogError("Failed to obtain IPRS access token: {Message}", tokenResult.Message);
                        return new IprsVerificationResult
                        {
                            Success = false,
                            Message = "Unable to authenticate with IPRS service",
                            Reference = null
                        };
                    }
                    _accessToken = tokenResult.Token;
                    _tokenExpiry = tokenResult.ExpiresAt;
                }

                // Make the verification request
                var baseUrl = _configuration["Iprs:BaseUrl"];
                var verificationUrl = $"{baseUrl}/analytics/account/iprs";

                var requestPayload = new
                {
                    search_type = "identity",
                    identifier = idNumber.Trim(),
                    consent = consent,
                    consent_collected_by = consentCollectedBy
                };

                using (var request = new HttpRequestMessage(HttpMethod.Post, verificationUrl))
                {
                    request.Headers.Add("Authorization", $"Bearer {_accessToken}");
                    request.Headers.Add("Accept", "application/json");
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(requestPayload),
                        Encoding.UTF8,
                        "application/json"
                    );

                    var response = await _httpClient.SendAsync(request);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogError("IPRS verification failed with status {StatusCode}: {Content}", response.StatusCode, errorContent);
                        return new IprsVerificationResult
                        {
                            Success = false,
                            Message = $"IPRS verification failed: {response.StatusCode}",
                            Reference = null
                        };
                    }

                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

                    // Check if the response contains success data
                    if (result.TryGetProperty("code", out var codeElement))
                    {
                        var code = codeElement.GetString();
                        if (code?.StartsWith("200") == true) // 200.xxx indicates success
                        {
                            if (result.TryGetProperty("response", out var dataElement))
                            {
                                var idNumberFromIprs = dataElement.TryGetProperty("identification_number", out var idEl) ? idEl.GetString() : string.Empty;
                                if (string.IsNullOrWhiteSpace(idNumberFromIprs))
                                {
                                    idNumberFromIprs = dataElement.TryGetProperty("id_serial", out var serialEl) ? serialEl.GetString() : string.Empty;
                                }

                                var firstName = dataElement.TryGetProperty("first_name", out var fnEl) ? fnEl.GetString() : string.Empty;
                                var middleName = dataElement.TryGetProperty("middle_name", out var mnEl) ? mnEl.GetString() : string.Empty;
                                var lastName = dataElement.TryGetProperty("last_name", out var lnEl) ? lnEl.GetString() : string.Empty;
                                var gender = dataElement.TryGetProperty("gender", out var gEl) ? gEl.GetString() : string.Empty;
                                var dateOfBirth = dataElement.TryGetProperty("date_of_birth", out var dobEl) ? dobEl.GetString() : string.Empty;
                                var kraPin = dataElement.TryGetProperty("kra_pin", out var kpEl) ? kpEl.GetString() : string.Empty;
                                var phone = dataElement.TryGetProperty("phone", out var phoneEl) ? phoneEl.GetString() : string.Empty;
                                var identityVerified = dataElement.TryGetProperty("identity_verified", out var ivEl) && ivEl.GetInt32() == 1;

                                var fullNameFromIprs = string.Join(" ", new[] { firstName, middleName, lastName }.Where(s => !string.IsNullOrWhiteSpace(s)));

                                _logger.LogInformation("IPRS verification successful for ID: {IdNumber}", idNumberFromIprs);

                                return new IprsVerificationResult
                                {
                                    Success = true,
                                    Message = "IPRS verification successful",
                                    Reference = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(),
                                    IdNumber = idNumberFromIprs,
                                    FullName = fullNameFromIprs,
                                    Gender = gender,
                                    DateOfBirth = dateOfBirth,
                                    KraPin = kraPin,
                                    Phone = phone,
                                    IdentityVerified = identityVerified,
                                    RawResponse = responseContent
                                };
                            }
                        }
                    }

                    _logger.LogWarning("IPRS verification returned unexpected response: {Response}", responseContent);
                    return new IprsVerificationResult
                    {
                        Success = false,
                        Message = "IPRS verification failed - no matching record found",
                        Reference = null
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during IPRS verification for ID: {IdNumber}", idNumber);
                return new IprsVerificationResult
                {
                    Success = false,
                    Message = $"IPRS verification error: {ex.Message}",
                    Reference = null
                };
            }
        }

        public async Task<CompanyVerificationResult> VerifyCompanyAsync(string registrationNumber, string consent = "1", string consentCollectedBy = "system")
        {
            try
            {
                if (string.IsNullOrWhiteSpace(registrationNumber))
                {
                    return new CompanyVerificationResult
                    {
                        Success = false,
                        Message = "Business registration number is required",
                        Reference = null
                    };
                }

                // Ensure we have a valid access token (shared with IPRS identity verification)
                if (string.IsNullOrWhiteSpace(_accessToken) || DateTime.UtcNow >= _tokenExpiry)
                {
                    var tokenResult = await GetAccessTokenAsync();
                    if (!tokenResult.Success)
                    {
                        _logger.LogError("Failed to obtain access token for company verification: {Message}", tokenResult.Message);
                        return new CompanyVerificationResult
                        {
                            Success = false,
                            Message = "Unable to authenticate with company search service",
                            Reference = null
                        };
                    }
                    _accessToken = tokenResult.Token;
                    _tokenExpiry = tokenResult.ExpiresAt;
                }

                var baseUrl = _configuration["Iprs:BaseUrl"];
                var verificationUrl = $"{baseUrl}/analytics/account/companysearchregno";

                var requestPayload = new
                {
                    search_type = "COMPANYSEARCHREGNO",
                    identifier = registrationNumber.Trim(),
                    consent = consent,
                    consent_collected_by = consentCollectedBy
                };

                using (var request = new HttpRequestMessage(HttpMethod.Post, verificationUrl))
                {
                    request.Headers.Add("Authorization", $"Bearer {_accessToken}");
                    request.Headers.Add("Accept", "application/json");
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(requestPayload),
                        Encoding.UTF8,
                        "application/json"
                    );

                    var response = await _httpClient.SendAsync(request);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogError("Company verification failed with status {StatusCode}: {Content}", response.StatusCode, errorContent);
                        return new CompanyVerificationResult
                        {
                            Success = false,
                            Message = $"Company verification failed: {response.StatusCode}",
                            Reference = null
                        };
                    }

                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

                    if (result.TryGetProperty("code", out var codeElement))
                    {
                        var code = codeElement.GetString();
                        if (code?.StartsWith("200") == true) // 200.xxx indicates success
                        {
                            if (result.TryGetProperty("response", out var dataElement))
                            {
                                string? GetStr(string name) =>
                                    dataElement.TryGetProperty(name, out var el) && el.ValueKind != JsonValueKind.Null
                                        ? el.GetString()
                                        : null;

                                var companyName = GetStr("company_name") ?? GetStr("name");
                                var regNumber = GetStr("registration_number") ?? GetStr("reg_no") ?? registrationNumber;
                                var status = GetStr("status") ?? GetStr("company_status");
                                var registrationDate = GetStr("registration_date") ?? GetStr("date_of_registration");
                                var natureOfBusiness = GetStr("nature_of_business") ?? GetStr("business_nature");
                                var kraPin = GetStr("kra_pin") ?? GetStr("pin");

                                _logger.LogInformation("Company verification successful for: {RegNumber}", regNumber);

                                return new CompanyVerificationResult
                                {
                                    Success = true,
                                    Message = "Company verification successful",
                                    Reference = Guid.NewGuid().ToString("N").Substring(0, 8).ToUpper(),
                                    CompanyName = companyName,
                                    RegistrationNumber = regNumber,
                                    Status = status,
                                    RegistrationDate = registrationDate,
                                    NatureOfBusiness = natureOfBusiness,
                                    KraPin = kraPin,
                                    RawResponse = responseContent
                                };
                            }
                        }
                    }

                    _logger.LogWarning("Company verification returned unexpected response: {Response}", responseContent);
                    return new CompanyVerificationResult
                    {
                        Success = false,
                        Message = "Company verification failed - no matching record found",
                        Reference = null,
                        RawResponse = responseContent
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during company verification for: {RegNumber}", registrationNumber);
                return new CompanyVerificationResult
                {
                    Success = false,
                    Message = $"Company verification error: {ex.Message}",
                    Reference = null
                };
            }
        }

        private async Task<TokenResult> GetAccessTokenAsync()
        {
            try
            {
                var baseUrl = _configuration["Iprs:BaseUrl"];
                var consumerKey = _configuration["Iprs:ConsumerKey"];
                var consumerSecret = _configuration["Iprs:ConsumerSecret"];

                if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(consumerKey) || string.IsNullOrWhiteSpace(consumerSecret))
                {
                    return new TokenResult
                    {
                        Success = false,
                        Message = "IPRS configuration is incomplete"
                    };
                }

                var authUrl = $"{baseUrl}/analytics/auth/";

                var requestPayload = new
                {
                    consumer_key = consumerKey,
                    consumer_secret = consumerSecret
                };

                using (var request = new HttpRequestMessage(HttpMethod.Post, authUrl))
                {
                    request.Headers.Add("Accept", "application/json");
                    request.Content = new StringContent(
                        JsonSerializer.Serialize(requestPayload),
                        Encoding.UTF8,
                        "application/json"
                    );

                    var response = await _httpClient.SendAsync(request);

                    if (!response.IsSuccessStatusCode)
                    {
                        var errorContent = await response.Content.ReadAsStringAsync();
                        _logger.LogError("IPRS token request failed with status {StatusCode}: {Content}", response.StatusCode, errorContent);
                        return new TokenResult
                        {
                            Success = false,
                            Message = $"Token request failed: {response.StatusCode}"
                        };
                    }

                    var responseContent = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<JsonElement>(responseContent);

                    if (result.TryGetProperty("token", out var tokenElement) && result.TryGetProperty("expires", out var expiresElement))
                    {
                        var token = tokenElement.GetString();
                        var expiresStr = expiresElement.GetString();

                        if (long.TryParse(expiresStr, out var expiresUnix))
                        {
                            var expiresAt = UnixTimeStampToDateTime(expiresUnix).AddSeconds(-60); // 60 second buffer
                            _logger.LogInformation("IPRS token obtained successfully, expires at: {ExpiryTime}", expiresAt);

                            return new TokenResult
                            {
                                Success = true,
                                Token = token,
                                ExpiresAt = expiresAt
                            };
                        }
                    }

                    return new TokenResult
                    {
                        Success = false,
                        Message = "Invalid token response format"
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during IPRS token request");
                return new TokenResult
                {
                    Success = false,
                    Message = $"Token request error: {ex.Message}"
                };
            }
        }

        private DateTime UnixTimeStampToDateTime(long unixTimeStamp)
        {
            var dateTime = new DateTime(1970, 1, 1, 0, 0, 0, 0, DateTimeKind.Utc);
            dateTime = dateTime.AddSeconds(unixTimeStamp).ToLocalTime();
            return dateTime;
        }
    }

    public class IprsVerificationResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Reference { get; set; }
        public string? IdNumber { get; set; }
        public string? FullName { get; set; }
        public string? Gender { get; set; }
        public string? DateOfBirth { get; set; }
        public string? KraPin { get; set; }
        public string? Phone { get; set; }
        public bool IdentityVerified { get; set; }
        public string? RawResponse { get; set; }
    }

    public class TokenResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Token { get; set; }
        public DateTime ExpiresAt { get; set; }
    }

    public class CompanyVerificationResult
    {
        public bool Success { get; set; }
        public string? Message { get; set; }
        public string? Reference { get; set; }
        public string? CompanyName { get; set; }
        public string? RegistrationNumber { get; set; }
        public string? Status { get; set; }
        public string? RegistrationDate { get; set; }
        public string? NatureOfBusiness { get; set; }
        public string? KraPin { get; set; }
        public string? RawResponse { get; set; }
    }
}
