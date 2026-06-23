using System.Text.Json.Serialization;

namespace OnwardsSwift.API.SignupWizard;

public sealed class Step1VerifyRequest
{
    [JsonPropertyName("fullName")]
    public string? FullName { get; set; }

    [JsonPropertyName("nationalId")]
    public string? NationalId { get; set; }
}

public sealed class WizardSessionRequest
{
    [JsonPropertyName("wizard_session_id")]
    public string? WizardSessionId { get; set; }
}
