using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Dapper;
using Microsoft.Data.SqlClient;
using Xunit;

namespace OnwardsSwift.API.IntegrationTests;

// These tests exercise the live API (ONWARDS_API_BASE_URL) plus a direct connection to the
// same database (ONWARDS_DB_CONNECTION_STRING) for state setup/cleanup -- following the same
// pattern as MobileAuthProfilePersistenceTests. Both env vars must be set or the test is a
// no-op, matching this project's existing convention.
public class RbacAndIdempotencyTests
{
    private static string? BaseUrl => Environment.GetEnvironmentVariable("ONWARDS_API_BASE_URL");
    private static string? DbConnectionString => Environment.GetEnvironmentVariable("ONWARDS_DB_CONNECTION_STRING");

    private static HttpClient CreateClient(bool followRedirects = true)
    {
        var handler = new HttpClientHandler { AllowAutoRedirect = followRedirects };
        return new HttpClient(handler) { BaseAddress = new Uri(BaseUrl!.TrimEnd('/') + "/") };
    }

    private static StringContent Json(object payload) =>
        new(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

    [Fact]
    public async Task MobileLogin_Should_Block_Until_Granted_Then_Block_Again_After_Revoke()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(DbConnectionString))
        {
            return;
        }

        using var client = CreateClient();
        var suffix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + Random.Shared.Next(1000, 9999);
        var phone = "+2547" + suffix[^8..];

        var signupResponse = await client.PostAsync("api/auth/signup", Json(new
        {
            nationalId = "ID" + suffix,
            fullName = "Rbac Test " + suffix,
            phone,
            email = "rbac.test." + suffix + "@example.com",
            pin = "1234",
            iprsVerified = true
        }));
        signupResponse.EnsureSuccessStatusCode();

        using var conn = new SqlConnection(DbConnectionString);
        await conn.OpenAsync();
        var userId = await conn.ExecuteScalarAsync<int>(
            "SELECT Id FROM dbo.SystemUsers WHERE Phone = @phone", new { phone });

        try
        {
            var blocked = await client.PostAsync("api/auth/signin", Json(new { phone, pin = "1234" }));
            Assert.Equal(HttpStatusCode.Forbidden, blocked.StatusCode);
            using var blockedDoc = JsonDocument.Parse(await blocked.Content.ReadAsStringAsync());
            Assert.Equal("account_pending_activation", blockedDoc.RootElement.GetProperty("errorCode").GetString());

            await conn.ExecuteAsync(@"
INSERT INTO dbo.UserPermissions (UserId, PermissionId, GrantedBy)
SELECT @userId, Id, 'integration-test' FROM dbo.Permissions WHERE Code = 'Mobile.Login';",
                new { userId });

            var allowed = await client.PostAsync("api/auth/signin", Json(new { phone, pin = "1234" }));
            allowed.EnsureSuccessStatusCode();

            await conn.ExecuteAsync(@"
DELETE up FROM dbo.UserPermissions up
JOIN dbo.Permissions p ON p.Id = up.PermissionId
WHERE up.UserId = @userId AND p.Code = 'Mobile.Login';",
                new { userId });

            var blockedAgain = await client.PostAsync("api/auth/signin", Json(new { phone, pin = "1234" }));
            Assert.Equal(HttpStatusCode.Forbidden, blockedAgain.StatusCode);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM dbo.UserPermissions WHERE UserId = @userId;", new { userId });
            await conn.ExecuteAsync("DELETE FROM dbo.SystemUsers WHERE Id = @userId;", new { userId });
        }
    }

    [Fact]
    public async Task CreateChequeRequest_Should_Be_Idempotent_For_Same_Reference()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(DbConnectionString))
        {
            return;
        }

        using var conn = new SqlConnection(DbConnectionString);
        await conn.OpenAsync();

        var (token, userId) = await SignUpGrantAndSignInAsync(conn);
        var suffix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var reference = "test-ref-cheque-" + suffix;
        int? requestId = null;

        try
        {
            using var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                reference,
                applicantName = "Idempotency Test " + suffix,
                idNumber = "ID" + suffix,
                phone = "+2547" + suffix[^8..],
                purpose = "Test purpose",
                termsAccepted = true
            };

            var first = await client.PostAsync("api/sql/cheques/request", Json(payload));
            first.EnsureSuccessStatusCode();
            using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
            requestId = firstDoc.RootElement.GetProperty("data").GetProperty("requestId").GetInt32();

            var second = await client.PostAsync("api/sql/cheques/request", Json(payload));
            second.EnsureSuccessStatusCode();
            using var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
            var secondRequestId = secondDoc.RootElement.GetProperty("data").GetProperty("requestId").GetInt32();

            Assert.Equal(requestId, secondRequestId);

            var rowCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.ChequeEncashmentRequests WHERE Reference = @reference", new { reference });
            Assert.Equal(1, rowCount);
        }
        finally
        {
            if (requestId.HasValue)
            {
                await conn.ExecuteAsync("DELETE FROM dbo.ChequeEncashmentRequests WHERE Id = @requestId;", new { requestId });
            }

            await conn.ExecuteAsync("DELETE FROM dbo.UserPermissions WHERE UserId = @userId;", new { userId });
            await conn.ExecuteAsync("DELETE FROM dbo.SystemUsers WHERE Id = @userId;", new { userId });
        }
    }

    // The sequential test above only proves the upfront "does this reference already exist"
    // SELECT works -- it does not exercise the actual race, since under READ COMMITTED two
    // truly concurrent requests with the same Reference can both pass that SELECT before either
    // INSERT commits. This fires requests in genuine parallel to prove the unique-constraint
    // fallback (see IsUniqueConstraintViolation in MobileSqlController) holds under that race.
    [Fact]
    public async Task CreateChequeRequest_Should_Be_Idempotent_Under_Genuine_Concurrency()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(DbConnectionString))
        {
            return;
        }

        using var conn = new SqlConnection(DbConnectionString);
        await conn.OpenAsync();

        var (token, userId) = await SignUpGrantAndSignInAsync(conn);
        var suffix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var reference = "test-ref-concurrent-cheque-" + suffix;

        try
        {
            var payload = new
            {
                reference,
                applicantName = "Concurrency Test " + suffix,
                idNumber = "ID" + suffix,
                phone = "+2547" + suffix[^8..],
                purpose = "Test purpose",
                termsAccepted = true
            };

            const int concurrentRequests = 8;
            var tasks = Enumerable.Range(0, concurrentRequests).Select(async _ =>
            {
                using var client = CreateClient();
                client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await client.PostAsync("api/sql/cheques/request", Json(payload));
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("data").GetProperty("requestId").GetInt32();
            });

            var requestIds = await Task.WhenAll(tasks);

            Assert.Single(requestIds.Distinct());

            var rowCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.ChequeEncashmentRequests WHERE Reference = @reference", new { reference });
            Assert.Equal(1, rowCount);
        }
        finally
        {
            await conn.ExecuteAsync("DELETE FROM dbo.ChequeEncashmentRequests WHERE Reference = @reference;", new { reference });
            await conn.ExecuteAsync("DELETE FROM dbo.UserPermissions WHERE UserId = @userId;", new { userId });
            await conn.ExecuteAsync("DELETE FROM dbo.SystemUsers WHERE Id = @userId;", new { userId });
        }
    }

    [Fact]
    public async Task CreateBondApplication_Should_Be_Idempotent_For_Same_Reference()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(DbConnectionString))
        {
            return;
        }

        using var conn = new SqlConnection(DbConnectionString);
        await conn.OpenAsync();

        var (token, userId) = await SignUpGrantAndSignInAsync(conn);
        var suffix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        var reference = "test-ref-bond-" + suffix;
        int? applicationId = null;

        try
        {
            using var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var payload = new
            {
                reference,
                applicantName = "Idempotency Bond Test " + suffix,
                phone = "+2547" + suffix[^8..],
                procuringEntity = "Test Procuring Entity",
                amount = 1000m,
                types = new[] { new { typeCode = "BB", typeName = "Bid Bond" } }
            };

            var first = await client.PostAsync("api/sql/bonds/application", Json(payload));
            first.EnsureSuccessStatusCode();
            using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
            applicationId = firstDoc.RootElement.GetProperty("data").GetProperty("applicationId").GetInt32();

            var second = await client.PostAsync("api/sql/bonds/application", Json(payload));
            second.EnsureSuccessStatusCode();
            using var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
            var secondApplicationId = secondDoc.RootElement.GetProperty("data").GetProperty("applicationId").GetInt32();

            Assert.Equal(applicationId, secondApplicationId);

            var rowCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.BondApplications WHERE Reference = @reference", new { reference });
            Assert.Equal(1, rowCount);
        }
        finally
        {
            if (applicationId.HasValue)
            {
                await conn.ExecuteAsync("DELETE FROM dbo.BondApplications WHERE Id = @applicationId;", new { applicationId });
            }

            await conn.ExecuteAsync("DELETE FROM dbo.UserPermissions WHERE UserId = @userId;", new { userId });
            await conn.ExecuteAsync("DELETE FROM dbo.SystemUsers WHERE Id = @userId;", new { userId });
        }
    }

    [Fact]
    public async Task AddChequeItem_Should_Be_Idempotent_For_Same_ChequeNumber()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(DbConnectionString))
        {
            return;
        }

        using var conn = new SqlConnection(DbConnectionString);
        await conn.OpenAsync();

        var (token, userId) = await SignUpGrantAndSignInAsync(conn);
        var suffix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        int? requestId = null;

        try
        {
            using var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var requestResponse = await client.PostAsync("api/sql/cheques/request", Json(new
            {
                applicantName = "Cheque Item Idempotency Test " + suffix,
                idNumber = "ID" + suffix,
                phone = "+2547" + suffix[^8..],
                purpose = "Test purpose",
                termsAccepted = true
            }));
            requestResponse.EnsureSuccessStatusCode();
            using var requestDoc = JsonDocument.Parse(await requestResponse.Content.ReadAsStringAsync());
            requestId = requestDoc.RootElement.GetProperty("data").GetProperty("requestId").GetInt32();

            var itemPayload = new
            {
                requestId,
                chequeNumber = "CHQ-" + suffix,
                amount = 500m,
                dated = "2026-01-01",
                drawer = "Drawer Co",
                bank = "Test Bank",
                branch = "Test Branch",
                payee = "Payee Co"
            };

            var first = await client.PostAsync("api/sql/cheques/items", Json(itemPayload));
            first.EnsureSuccessStatusCode();
            using var firstDoc = JsonDocument.Parse(await first.Content.ReadAsStringAsync());
            var itemId = firstDoc.RootElement.GetProperty("data").GetProperty("itemId").GetInt32();

            var second = await client.PostAsync("api/sql/cheques/items", Json(itemPayload));
            second.EnsureSuccessStatusCode();
            using var secondDoc = JsonDocument.Parse(await second.Content.ReadAsStringAsync());
            var secondItemId = secondDoc.RootElement.GetProperty("data").GetProperty("itemId").GetInt32();

            Assert.Equal(itemId, secondItemId);

            var rowCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.ChequeEncashmentCheques WHERE RequestId = @requestId AND ChequeNumber = @chequeNumber",
                new { requestId, chequeNumber = itemPayload.chequeNumber });
            Assert.Equal(1, rowCount);
        }
        finally
        {
            if (requestId.HasValue)
            {
                await conn.ExecuteAsync("DELETE FROM dbo.ChequeEncashmentCheques WHERE RequestId = @requestId;", new { requestId });
                await conn.ExecuteAsync("DELETE FROM dbo.ChequeEncashmentRequests WHERE Id = @requestId;", new { requestId });
            }

            await conn.ExecuteAsync("DELETE FROM dbo.UserPermissions WHERE UserId = @userId;", new { userId });
            await conn.ExecuteAsync("DELETE FROM dbo.SystemUsers WHERE Id = @userId;", new { userId });
        }
    }

    // Same rationale as CreateChequeRequest_Should_Be_Idempotent_Under_Genuine_Concurrency --
    // proves the unique-constraint fallback in AddChequeItem holds when concurrent requests
    // race past the lack of an upfront SELECT (there isn't one; the unique index plus catch is
    // the only guard here).
    [Fact]
    public async Task AddChequeItem_Should_Be_Idempotent_Under_Genuine_Concurrency()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl) || string.IsNullOrWhiteSpace(DbConnectionString))
        {
            return;
        }

        using var conn = new SqlConnection(DbConnectionString);
        await conn.OpenAsync();

        var (token, userId) = await SignUpGrantAndSignInAsync(conn);
        var suffix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString();
        int? requestId = null;

        try
        {
            using var client = CreateClient();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            var requestResponse = await client.PostAsync("api/sql/cheques/request", Json(new
            {
                applicantName = "Cheque Item Concurrency Test " + suffix,
                idNumber = "ID" + suffix,
                phone = "+2547" + suffix[^8..],
                purpose = "Test purpose",
                termsAccepted = true
            }));
            requestResponse.EnsureSuccessStatusCode();
            using var requestDoc = JsonDocument.Parse(await requestResponse.Content.ReadAsStringAsync());
            requestId = requestDoc.RootElement.GetProperty("data").GetProperty("requestId").GetInt32();

            var itemPayload = new
            {
                requestId,
                chequeNumber = "CHQ-CONC-" + suffix,
                amount = 750m,
                dated = "2026-01-01",
                drawer = "Drawer Co",
                bank = "Test Bank",
                branch = "Test Branch",
                payee = "Payee Co"
            };

            const int concurrentRequests = 8;
            var tasks = Enumerable.Range(0, concurrentRequests).Select(async _ =>
            {
                using var itemClient = CreateClient();
                itemClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
                var response = await itemClient.PostAsync("api/sql/cheques/items", Json(itemPayload));
                response.EnsureSuccessStatusCode();
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                return doc.RootElement.GetProperty("data").GetProperty("itemId").GetInt32();
            });

            var itemIds = await Task.WhenAll(tasks);

            Assert.Single(itemIds.Distinct());

            var rowCount = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM dbo.ChequeEncashmentCheques WHERE RequestId = @requestId AND ChequeNumber = @chequeNumber",
                new { requestId, chequeNumber = itemPayload.chequeNumber });
            Assert.Equal(1, rowCount);
        }
        finally
        {
            if (requestId.HasValue)
            {
                await conn.ExecuteAsync("DELETE FROM dbo.ChequeEncashmentCheques WHERE RequestId = @requestId;", new { requestId });
                await conn.ExecuteAsync("DELETE FROM dbo.ChequeEncashmentRequests WHERE Id = @requestId;", new { requestId });
            }

            await conn.ExecuteAsync("DELETE FROM dbo.UserPermissions WHERE UserId = @userId;", new { userId });
            await conn.ExecuteAsync("DELETE FROM dbo.SystemUsers WHERE Id = @userId;", new { userId });
        }
    }

    [Fact]
    public async Task OfficialUse_Should_Redirect_Anonymous_Users_To_Login_Not_Render_Content()
    {
        if (string.IsNullOrWhiteSpace(BaseUrl))
        {
            return;
        }

        using var client = CreateClient(followRedirects: false);
        var response = await client.GetAsync("Forms/OfficialUse?requestId=1");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("Account/Login", response.Headers.Location?.ToString() ?? string.Empty);
    }

    private static async Task<(string Token, int UserId)> SignUpGrantAndSignInAsync(SqlConnection conn)
    {
        var suffix = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString() + Random.Shared.Next(1000, 9999);
        var phone = "+2547" + suffix[^8..];

        using var client = CreateClient();
        var signupResponse = await client.PostAsync("api/auth/signup", Json(new
        {
            nationalId = "ID" + suffix,
            fullName = "Idempotency Helper " + suffix,
            phone,
            email = "idempotency.helper." + suffix + "@example.com",
            pin = "1234",
            iprsVerified = true
        }));
        signupResponse.EnsureSuccessStatusCode();

        var userId = await conn.ExecuteScalarAsync<int>(
            "SELECT Id FROM dbo.SystemUsers WHERE Phone = @phone", new { phone });

        await conn.ExecuteAsync(@"
INSERT INTO dbo.UserPermissions (UserId, PermissionId, GrantedBy)
SELECT @userId, Id, 'integration-test' FROM dbo.Permissions WHERE Code = 'Mobile.Login';",
            new { userId });

        var signinResponse = await client.PostAsync("api/auth/signin", Json(new { phone, pin = "1234" }));
        signinResponse.EnsureSuccessStatusCode();
        using var signinDoc = JsonDocument.Parse(await signinResponse.Content.ReadAsStringAsync());
        var token = signinDoc.RootElement.GetProperty("data").GetProperty("token").GetString()!;
        return (token, userId);
    }
}
