using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Infrastructure.Services;

namespace OnwardsSwift.API.Controllers
{
    [ApiController]
    [Route("api/forms/onboarding")]
    public class OnboardingController : ControllerBase
    {
        private const string StoragePath = "App_Data";
        private const string OnboardingFile = "onboarding-applications.json";
        private const string VerificationLog = "verification-log.json";
        private const string AuditLog = "audit-logs.json";
        
        private readonly IIprsService _iprsService;

        public OnboardingController(IIprsService iprsService)
        {
            _iprsService = iprsService;
        }

        [HttpPost("save-draft")]
        public async Task<IActionResult> SaveOnboardingDraft([FromBody] JsonElement payload)
        {
            try
            {
                var root = Path.Combine(AppContext.BaseDirectory, StoragePath);
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);
                var file = Path.Combine(root, OnboardingFile);

                // load existing
                object[] items;
                if (System.IO.File.Exists(file))
                {
                    var existing = await System.IO.File.ReadAllTextAsync(file);
                    try { items = JsonSerializer.Deserialize<object[]>(existing) ?? Array.Empty<object>(); }
                    catch { items = Array.Empty<object>(); }
                }
                else items = Array.Empty<object>();

                var id = Guid.NewGuid().ToString();
                using var doc = JsonDocument.Parse(payload.GetRawText());
                var obj = doc.RootElement.Clone();
                var wrapper = new { Id = id, ReceivedAt = DateTime.UtcNow, Status = "Draft", Data = obj };

                var list = new object[items.Length + 1];
                for (int i = 0; i < items.Length; i++) list[i] = items[i];
                list[list.Length - 1] = wrapper;

                var outJson = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                await System.IO.File.WriteAllTextAsync(file, outJson);

                // audit
                await AppendAudit(new { Action = "SaveDraft", ApplicationId = id, Timestamp = DateTime.UtcNow });

                return Ok(new { status = "saved", id });
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        [HttpPost("submit-multipart")]
        [RequestSizeLimit(80_000_000)]
        public async Task<IActionResult> SubmitOnboardingMultipart()
        {
            try
            {
                var req = Request;
                if (!req.HasFormContentType) return BadRequest("Expected multipart/form-data");
                var form = await req.ReadFormAsync();

                var root = Path.Combine(AppContext.BaseDirectory, StoragePath);
                var uploads = Path.Combine(root, "uploads");
                if (!Directory.Exists(uploads)) Directory.CreateDirectory(uploads);
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);

                // collect fields
                var data = form.Where(kv => kv.Key != "documents" && kv.Key != "cheques")
                               .ToDictionary(k => k.Key, k => (object)k.Value.ToString());

                if (form.TryGetValue("cheques", out var chequeVals))
                {
                    try { data["cheques"] = JsonDocument.Parse(chequeVals.ToString()).RootElement.Clone(); } catch { }
                }

                // save files
                var savedFiles = new System.Collections.Generic.List<object>();
                var allowed = new[] { "application/pdf", "image/png", "image/jpeg", "image/jpg" };
                const long maxBytes = 20L * 1024L * 1024L; // 20MB per file for onboarding
                foreach (var file in form.Files)
                {
                    if (file.Length == 0) continue;
                    if (file.Length > maxBytes) return BadRequest($"File '{file.FileName}' exceeds the maximum allowed size of 20MB.");
                    if (!string.IsNullOrWhiteSpace(file.ContentType) && !allowed.Contains(file.ContentType.ToLower()))
                        return BadRequest($"File '{file.FileName}' has disallowed content type '{file.ContentType}'.");

                    var safeName = Path.GetFileName(file.FileName);
                    var unique = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "_" + Guid.NewGuid().ToString("N").Substring(0,6) + "_" + safeName;
                    var dest = Path.Combine(uploads, unique);
                    await using var fs = System.IO.File.Create(dest);
                    await file.CopyToAsync(fs);
                    savedFiles.Add(new { originalName = file.FileName, fileName = unique, size = file.Length, contentType = file.ContentType, path = Path.GetRelativePath(AppContext.BaseDirectory, dest) });
                }

                data["documents"] = savedFiles.ToArray();

                // persist application
                var filePath = Path.Combine(root, OnboardingFile);
                object[] items;
                if (System.IO.File.Exists(filePath))
                {
                    var existing = await System.IO.File.ReadAllTextAsync(filePath);
                    try { items = JsonSerializer.Deserialize<object[]>(existing) ?? Array.Empty<object>(); }
                    catch { items = Array.Empty<object>(); }
                }
                else items = Array.Empty<object>();

                var appId = Guid.NewGuid().ToString();
                var wrapper = new { Id = appId, ReceivedAt = DateTime.UtcNow, Status = "Submitted", Data = data };
                var list = new object[items.Length + 1];
                for (int i = 0; i < items.Length; i++) list[i] = items[i];
                list[list.Length - 1] = wrapper;

                var outJson = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                await System.IO.File.WriteAllTextAsync(filePath, outJson);

                await AppendAudit(new { Action = "SubmitApplication", ApplicationId = appId, Timestamp = DateTime.UtcNow });

                return Ok(new { status = "submitted", id = appId, attachments = savedFiles });
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        [HttpGet("{id}")]
        public async Task<IActionResult> GetOnboarding(string id)
        {
            try
            {
                var root = Path.Combine(AppContext.BaseDirectory, StoragePath);
                var file = Path.Combine(root, OnboardingFile);
                if (!System.IO.File.Exists(file)) return NotFound();
                var existing = await System.IO.File.ReadAllTextAsync(file);
                var arr = JsonSerializer.Deserialize<JsonElement[]>(existing);
                if (arr == null) return NotFound();
                foreach (var el in arr)
                {
                    if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("Id", out var pid) && pid.GetString() == id)
                        return Ok(el);
                }
                return NotFound();
            }
            catch (Exception ex) { return Problem(detail: ex.Message); }
        }

        [HttpGet("list")]
        public async Task<IActionResult> ListOnboarding()
        {
            try
            {
                var root = Path.Combine(AppContext.BaseDirectory, StoragePath);
                var file = Path.Combine(root, OnboardingFile);
                if (!System.IO.File.Exists(file)) return Ok(new object[0]);
                var existing = await System.IO.File.ReadAllTextAsync(file);
                try { var arr = JsonSerializer.Deserialize<object[]>(existing) ?? Array.Empty<object>(); return Ok(arr); }
                catch { return Ok(new object[0]); }
            }
            catch (Exception ex) { return Problem(detail: ex.Message); }
        }

        [HttpPost("{id}/status")]
        public async Task<IActionResult> UpdateOnboardingStatus(string id, [FromBody] JsonElement payload)
        {
            try
            {
                var root = Path.Combine(AppContext.BaseDirectory, StoragePath);
                var file = Path.Combine(root, OnboardingFile);
                if (!System.IO.File.Exists(file)) return NotFound();
                var existing = await System.IO.File.ReadAllTextAsync(file);
                var arr = JsonSerializer.Deserialize<JsonElement[]>(existing) ?? Array.Empty<JsonElement>();
                var updated = false;
                var list = new object[arr.Length];
                for (int i = 0; i < arr.Length; i++)
                {
                    var el = arr[i];
                    if (el.ValueKind == JsonValueKind.Object && el.TryGetProperty("Id", out var pid) && pid.GetString() == id)
                    {
                        using var doc = JsonDocument.Parse(el.GetRawText());
                        var rootEl = doc.RootElement.Clone();
                        var status = payload.TryGetProperty("status", out var st) ? st.GetString() ?? "" : "";
                        var note = payload.TryGetProperty("note", out var nt) ? nt.GetString() ?? string.Empty : string.Empty;
                        var wrapper = new { Id = id, ReceivedAt = DateTime.UtcNow, Status = status, Note = note, Data = rootEl.GetProperty("Data") };
                        list[i] = wrapper;
                        updated = true;
                    }
                    else list[i] = JsonSerializer.Deserialize<object>(el.GetRawText()) ?? new object();
                }
                if (!updated) return NotFound();
                var outJson = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                await System.IO.File.WriteAllTextAsync(file, outJson);

                await AppendAudit(new { Action = "UpdateStatus", ApplicationId = id, NewStatus = payload.GetProperty("status").GetString(), Timestamp = DateTime.UtcNow });

                return Ok(new { status = "updated" });
            }
            catch (Exception ex) { return Problem(detail: ex.Message); }
        }

        [HttpPost("verify-iprs")]
        public async Task<IActionResult> VerifyIprs([FromBody] JsonElement payload)
        {
            try
            {
                var idNo = payload.TryGetProperty("idNumber", out var p) ? p.GetString() ?? string.Empty : string.Empty;
                var name = payload.TryGetProperty("fullName", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                
                if (string.IsNullOrWhiteSpace(idNo))
                {
                    return BadRequest(new { status = "failed", message = "ID Number is required" });
                }

                // Call real IPRS service
                var iprsResult = await _iprsService.VerifyIdentityAsync(idNo, name);

                var result = new
                {
                    idNumber = iprsResult.IdNumber ?? idNo,
                    fullName = iprsResult.FullName ?? name,
                    phone = iprsResult.Phone ?? string.Empty,
                    kraPin = iprsResult.KraPin ?? string.Empty,
                    status = iprsResult.Success ? "Success" : "Failed",
                    message = iprsResult.Message,
                    timestamp = DateTime.UtcNow,
                    reference = iprsResult.Reference
                };

                // Append to verification log (do not fail verification if logging is unavailable)
                try
                {
                    var root = Path.Combine(AppContext.BaseDirectory, StoragePath);
                    if (!Directory.Exists(root)) Directory.CreateDirectory(root);
                    var file = Path.Combine(root, VerificationLog);
                    object[] items;
                    if (System.IO.File.Exists(file))
                    {
                        var existing = await System.IO.File.ReadAllTextAsync(file);
                        try { items = JsonSerializer.Deserialize<object[]>(existing) ?? Array.Empty<object>(); }
                        catch { items = Array.Empty<object>(); }
                    }
                    else items = Array.Empty<object>();

                    var list = new object[items.Length + 1];
                    for (int i = 0; i < items.Length; i++) list[i] = items[i];
                    list[list.Length - 1] = result;
                    await System.IO.File.WriteAllTextAsync(file, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch
                {
                    // Ignore logging failures so verification itself can still return a response.
                }

                await AppendAudit(new
                {
                    Action = "IPRSVerify",
                    IdNumber = idNo,
                    Result = result.status,
                    Reference = iprsResult.Reference,
                    Timestamp = DateTime.UtcNow
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        [HttpPost("verify-company")]
        public async Task<IActionResult> VerifyCompany([FromBody] JsonElement payload)
        {
            try
            {
                var regNo = payload.TryGetProperty("registrationNumber", out var p) ? p.GetString() ?? string.Empty : string.Empty;

                if (string.IsNullOrWhiteSpace(regNo))
                {
                    return BadRequest(new { status = "failed", message = "Business registration number is required" });
                }

                // Call real company search service
                var companyResult = await _iprsService.VerifyCompanyAsync(regNo);

                var result = new
                {
                    registrationNumber = companyResult.RegistrationNumber ?? regNo,
                    companyName = companyResult.CompanyName ?? string.Empty,
                    companyStatus = companyResult.Status ?? string.Empty,
                    registrationDate = companyResult.RegistrationDate ?? string.Empty,
                    natureOfBusiness = companyResult.NatureOfBusiness ?? string.Empty,
                    kraPin = companyResult.KraPin ?? string.Empty,
                    status = companyResult.Success ? "Success" : "Failed",
                    message = companyResult.Message,
                    timestamp = DateTime.UtcNow,
                    reference = companyResult.Reference
                };

                // Append to verification log (do not fail verification if logging is unavailable)
                try
                {
                    var root = Path.Combine(AppContext.BaseDirectory, StoragePath);
                    if (!Directory.Exists(root)) Directory.CreateDirectory(root);
                    var file = Path.Combine(root, VerificationLog);
                    object[] items;
                    if (System.IO.File.Exists(file))
                    {
                        var existing = await System.IO.File.ReadAllTextAsync(file);
                        try { items = JsonSerializer.Deserialize<object[]>(existing) ?? Array.Empty<object>(); }
                        catch { items = Array.Empty<object>(); }
                    }
                    else items = Array.Empty<object>();

                    var list = new object[items.Length + 1];
                    for (int i = 0; i < items.Length; i++) list[i] = items[i];
                    list[list.Length - 1] = result;
                    await System.IO.File.WriteAllTextAsync(file, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
                }
                catch
                {
                    // Ignore logging failures so verification itself can still return a response.
                }

                await AppendAudit(new
                {
                    Action = "CompanyVerify",
                    RegistrationNumber = regNo,
                    Result = result.status,
                    Reference = companyResult.Reference,
                    Timestamp = DateTime.UtcNow
                });

                return Ok(result);
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        private async Task AppendAudit(object entry)
        {
            try
            {
                var root = Path.Combine(AppContext.BaseDirectory, StoragePath);
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);
                var file = Path.Combine(root, AuditLog);
                object[] items;
                if (System.IO.File.Exists(file))
                {
                    var existing = await System.IO.File.ReadAllTextAsync(file);
                    try { items = JsonSerializer.Deserialize<object[]>(existing) ?? Array.Empty<object>(); }
                    catch { items = Array.Empty<object>(); }
                }
                else items = Array.Empty<object>();

                var list = new object[items.Length + 1];
                for (int i = 0; i < items.Length; i++) list[i] = items[i];
                list[list.Length - 1] = entry;
                await System.IO.File.WriteAllTextAsync(file, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
