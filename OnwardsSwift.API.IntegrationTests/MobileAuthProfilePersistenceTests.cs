using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace OnwardsSwift.API.IntegrationTests;

public class MobileAuthProfilePersistenceTests
{
    [Fact]
    public async Task Signup_And_Me_Should_RoundTrip_Profile_Fields_From_SystemUsers()
    {
        var baseUrl = Environment.GetEnvironmentVariable("ONWARDS_API_BASE_URL");
        var dbConnectionString = Environment.GetEnvironmentVariable("ONWARDS_DB_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(baseUrl) || string.IsNullOrWhiteSpace(dbConnectionString))
        {
            return;
        }

        using var client = new HttpClient
        {
            BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/")
        };

        var suffix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + Random.Shared.Next(1000, 9999);
        var phone = "+2547" + suffix[^8..];
        var signupPayload = new
        {
            nationalId = "ID" + suffix,
            fullName = "Profile Test " + suffix,
            phone,
            pin = "1234",
            iprsVerified = true,
            email = "profile.test." + suffix + "@example.com",
            kraPin = "A123456789Z",
            gender = "Female",
            postalAddress = "P.O. Box 12345-00100",
            physicalAddress = "Nairobi, Kenya",
            iprsReference = "IPRS-" + suffix
        };

        var signupResponse = await client.PostAsync(
            "api/auth/signup",
            new StringContent(JsonSerializer.Serialize(signupPayload), Encoding.UTF8, "application/json"));

        signupResponse.EnsureSuccessStatusCode();
        using var signupDoc = JsonDocument.Parse(await signupResponse.Content.ReadAsStringAsync());
        var signupRoot = signupDoc.RootElement;
        Assert.True(signupRoot.GetProperty("success").GetBoolean());

        var signupData = signupRoot.GetProperty("data");
        Assert.Equal(signupPayload.kraPin, signupData.GetProperty("kraPin").GetString());
        Assert.Equal(signupPayload.fullName, signupData.GetProperty("fullName").GetString());
        Assert.Equal(signupPayload.nationalId, signupData.GetProperty("nationalId").GetString());
        Assert.Equal(signupPayload.email, signupData.GetProperty("email").GetString());
        Assert.Equal(signupPayload.gender, signupData.GetProperty("gender").GetString());
        Assert.Equal(signupPayload.postalAddress, signupData.GetProperty("postalAddress").GetString());
        Assert.Equal(signupPayload.physicalAddress, signupData.GetProperty("physicalAddress").GetString());
        Assert.Equal(signupPayload.iprsReference, signupData.GetProperty("iprsReference").GetString());
        Assert.True(signupData.GetProperty("iprsVerified").GetBoolean());

        using var conn = new SqlConnection(dbConnectionString);
        await conn.OpenAsync();
        var userId = await conn.ExecuteScalarAsync<int>(
            "SELECT Id FROM dbo.SystemUsers WHERE Phone = @phone", new { phone = signupPayload.phone });

        try
        {
            // New signups are blocked from PIN sign-in until staff grant Mobile.Login --
            // simulate that grant directly so this profile-roundtrip test isn't testing the
            // RBAC gate itself (that's covered by RbacAndIdempotencyTests).
            await conn.ExecuteAsync(@"
INSERT INTO dbo.UserPermissions (UserId, PermissionId, GrantedBy)
SELECT @userId, Id, 'integration-test' FROM dbo.Permissions WHERE Code = 'Mobile.Login';",
                new { userId });

            var signinPayload = new
            {
                phone = signupPayload.phone,
                pin = signupPayload.pin
            };

            var signinResponse = await client.PostAsync(
                "api/auth/signin",
                new StringContent(JsonSerializer.Serialize(signinPayload), Encoding.UTF8, "application/json"));

            signinResponse.EnsureSuccessStatusCode();
            using var signinDoc = JsonDocument.Parse(await signinResponse.Content.ReadAsStringAsync());
            var signinData = signinDoc.RootElement.GetProperty("data");
            var token = signinData.GetProperty("token").GetString();
            Assert.False(string.IsNullOrWhiteSpace(token));

            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var meResponse = await client.GetAsync("api/auth/me");
            meResponse.EnsureSuccessStatusCode();

            using var meDoc = JsonDocument.Parse(await meResponse.Content.ReadAsStringAsync());
            var meRoot = meDoc.RootElement;
            Assert.True(meRoot.GetProperty("success").GetBoolean());

            var meData = meRoot.GetProperty("data");
            Assert.Equal(signupPayload.kraPin, meData.GetProperty("kraPin").GetString());
            Assert.Equal(signupPayload.fullName, meData.GetProperty("fullName").GetString());
            Assert.Equal(signupPayload.nationalId, meData.GetProperty("nationalId").GetString());
            Assert.Equal(signupPayload.email, meData.GetProperty("email").GetString());
            Assert.Equal(signupPayload.gender, meData.GetProperty("gender").GetString());
            Assert.Equal(signupPayload.postalAddress, meData.GetProperty("postalAddress").GetString());
            Assert.Equal(signupPayload.physicalAddress, meData.GetProperty("physicalAddress").GetString());
            Assert.Equal(signupPayload.iprsReference, meData.GetProperty("iprsReference").GetString());
            Assert.True(meData.GetProperty("iprsVerified").GetBoolean());
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM dbo.UserPermissions WHERE UserId = @userId;", new { userId });
            await conn.ExecuteAsync("DELETE FROM dbo.SystemUsers WHERE Id = @userId;", new { userId });
        }
    }
}
