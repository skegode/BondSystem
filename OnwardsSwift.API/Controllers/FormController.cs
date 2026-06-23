using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using QuestPDF.Helpers;

namespace OnwardsSwift.API.Controllers
{
    [ApiController]
    [Route("api/forms")]
    public class FormController : ControllerBase
    {
        private const string StoragePath = "App_Data";
        private const string FileName = "form-submissions.json";

        [HttpPost("submit")]
        public async Task<IActionResult> Submit([FromBody] JsonElement payload)
        {
            try
            {
                var validationError = ValidateJsonPayload(payload);
                if (validationError != null) return BadRequest(validationError);

                var root = Path.Combine(AppContext.BaseDirectory, StoragePath);
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);

                var file = Path.Combine(root, FileName);

                JsonElement[] items;
                if (System.IO.File.Exists(file))
                {
                    var existing = await System.IO.File.ReadAllTextAsync(file);
                    try { items = JsonSerializer.Deserialize<JsonElement[]>(existing) ?? Array.Empty<JsonElement>(); }
                    catch { items = Array.Empty<JsonElement>(); }
                }
                else items = Array.Empty<JsonElement>();

                using var doc = JsonDocument.Parse(payload.GetRawText());
                var obj = doc.RootElement.Clone();

                var wrapper = new { ReceivedAt = DateTime.UtcNow, Data = obj };
                var list = new object[items.Length + 1];
                for (int i = 0; i < items.Length; i++) list[i] = items[i];
                list[list.Length - 1] = wrapper;

                var outJson = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                await System.IO.File.WriteAllTextAsync(file, outJson);

                return Ok(new { status = "saved" });
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        [HttpPost("submit-multipart")]
        [RequestSizeLimit(50_000_000)]
        public async Task<IActionResult> SubmitMultipart()
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

                var data = form.Where(kv => kv.Key != "cheques")
                               .ToDictionary(k => k.Key, k => (object)k.Value.ToString());

                if (form.TryGetValue("cheques", out var chequeVals))
                {
                    try { data["cheques"] = JsonDocument.Parse(chequeVals.ToString()).RootElement.Clone(); } catch { }
                }

                var savedFiles = new System.Collections.Generic.List<object>();
                var allowed = new[] { "application/pdf", "image/png", "image/jpeg", "image/jpg" };
                const long maxBytes = 10L * 1024L * 1024L;
                foreach (var file in form.Files)
                {
                    if (file.Length == 0) continue;
                    if (file.Length > maxBytes) return BadRequest($"File '{file.FileName}' exceeds the maximum allowed size of 10MB.");
                    if (!string.IsNullOrWhiteSpace(file.ContentType) && !allowed.Contains(file.ContentType.ToLower()))
                        return BadRequest($"File '{file.FileName}' has disallowed content type '{file.ContentType}'.");

                    var safeName = Path.GetFileName(file.FileName);
                    var unique = DateTime.UtcNow.ToString("yyyyMMddHHmmssfff") + "_" + Guid.NewGuid().ToString("N").Substring(0,6) + "_" + safeName;
                    var dest = Path.Combine(uploads, unique);
                    await using var fs = System.IO.File.Create(dest);
                    await file.CopyToAsync(fs);
                    savedFiles.Add(new { originalName = file.FileName, fileName = unique, size = file.Length, contentType = file.ContentType, path = Path.GetRelativePath(AppContext.BaseDirectory, dest) });
                }

                data["attachments"] = savedFiles.ToArray();

                var filePath = Path.Combine(root, FileName);
                object[] items;
                if (System.IO.File.Exists(filePath))
                {
                    var existing = await System.IO.File.ReadAllTextAsync(filePath);
                    try { items = JsonSerializer.Deserialize<object[]>(existing) ?? Array.Empty<object>(); }
                    catch { items = Array.Empty<object>(); }
                }
                else items = Array.Empty<object>();

                var wrapper = new { ReceivedAt = DateTime.UtcNow, Data = data };
                var list = new object[items.Length + 1];
                for (int i = 0; i < items.Length; i++) list[i] = items[i];
                list[list.Length - 1] = wrapper;

                var outJson = JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true });
                await System.IO.File.WriteAllTextAsync(filePath, outJson);

                return Ok(new { status = "saved", attachments = savedFiles });
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        [HttpPost("export-pdf")]
        public IActionResult ExportPdf([FromBody] JsonElement payload)
        {
            try
            {
                var title = "Submission Export";
                if (payload.TryGetProperty("formType", out var ft) && ft.GetString() == "cheque-encashment") title = "Cheque Encashment";

                var ms = new MemoryStream();

                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4);
                        page.Margin(40);
                        page.DefaultTextStyle(x => x.FontSize(12));

                        page.Background().Element(x => { x.AlignCenter().Text("ONWARDS SWIFT").FontSize(56).FontColor(Colors.Grey.Lighten2); });

                        page.Header().Row(row =>
                        {
                            row.RelativeColumn().Column(col =>
                            {
                                col.Item().Text(title).SemiBold().FontSize(16);
                                col.Item().Text($"Exported: {DateTime.UtcNow:yyyy-MM-dd HH:mm}").FontSize(9).FontColor(Colors.Grey.Darken1);
                            });
                        });

                        page.Content().Column(col =>
                        {
                            col.Item().Element(el =>
                            {
                                if (payload.ValueKind == JsonValueKind.Object)
                                {
                                    foreach (var prop in payload.EnumerateObject())
                                    {
                                        if (prop.NameEquals("cheques") || prop.NameEquals("attachments")) continue;
                                        el.Row(r => { r.ConstantColumn(140).Text(prop.Name).SemiBold(); r.RelativeColumn().Text(prop.Value.ToString()); });
                                    }
                                }
                            });

                            if (payload.TryGetProperty("cheques", out var cheques) && cheques.ValueKind == JsonValueKind.Array)
                            {
                                col.Item().PaddingTop(6).Text("Cheques:").SemiBold();
                                col.Item().Table(table =>
                                {
                                    table.ColumnsDefinition(columns => { columns.RelativeColumn(); columns.RelativeColumn(); columns.RelativeColumn(); columns.RelativeColumn(); columns.RelativeColumn(); columns.RelativeColumn(); columns.RelativeColumn(); });
                                    table.Header(header =>
                                    {
                                        header.Cell().Text("No.").SemiBold();
                                        header.Cell().Text("Cheque #").SemiBold();
                                        header.Cell().Text("Amount (KES)").SemiBold();
                                        header.Cell().Text("Dated").SemiBold();
                                        header.Cell().Text("Drawer").SemiBold();
                                        header.Cell().Text("Bank").SemiBold();
                                        header.Cell().Text("Payee").SemiBold();
                                    });

                                    int idx = 1;
                                    foreach (var c in cheques.EnumerateArray())
                                    {
                                        table.Cell().Text(idx.ToString());
                                        table.Cell().Text(c.TryGetProperty("chequeNumber", out var cn) ? cn.GetString() ?? string.Empty : string.Empty);
                                        table.Cell().Text(c.TryGetProperty("amountKES", out var am) ? am.GetString() ?? string.Empty : string.Empty);
                                        table.Cell().Text(c.TryGetProperty("dated", out var dt) ? dt.GetString() ?? string.Empty : string.Empty);
                                        table.Cell().Text(c.TryGetProperty("drawer", out var dr) ? dr.GetString() ?? string.Empty : string.Empty);
                                        table.Cell().Text(c.TryGetProperty("bank", out var bk) ? bk.GetString() ?? string.Empty : string.Empty);
                                        table.Cell().Text(c.TryGetProperty("payee", out var py) ? py.GetString() ?? string.Empty : string.Empty);
                                        idx++;
                                    }
                                });
                            }

                            if (payload.TryGetProperty("attachments", out var atts) && atts.ValueKind == JsonValueKind.Array)
                            {
                                col.Item().PaddingTop(6).Text("Attachments:").SemiBold();
                                int aidx = 1;
                                foreach (var a in atts.EnumerateArray())
                                {
                                    var name = a.TryGetProperty("name", out var n) ? n.GetString() : a.ToString();
                                    col.Item().Text($"{aidx}. {name}");
                                    aidx++;
                                }
                            }
                        });

                        page.Footer().AlignCenter().Text($"Payment details: see bank remittance / charges. Generated {DateTime.UtcNow:yyyy-MM-dd}").FontSize(9);
                    });
                }).GeneratePdf(ms);

                ms.Position = 0;
                return File(ms.ToArray(), "application/pdf", "export.pdf");
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        [HttpPost("export-official")]
        public IActionResult ExportOfficial([FromBody] JsonElement payload)
        {
            try
            {
                var applicant = payload.TryGetProperty("appName", out var pa) ? pa.GetString() ?? string.Empty : string.Empty;
                if (string.IsNullOrWhiteSpace(applicant)) applicant = payload.TryGetProperty("sigName", out var ps) ? ps.GetString() ?? string.Empty : applicant;
                var idNo = payload.TryGetProperty("idNo", out var pid) ? pid.GetString() ?? string.Empty : string.Empty;
                var tcName = payload.TryGetProperty("tcName", out var ptc) ? ptc.GetString() ?? string.Empty : string.Empty;
                var tcId = payload.TryGetProperty("tcId", out var ptci) ? ptci.GetString() ?? string.Empty : string.Empty;
                var purpose = payload.TryGetProperty("purpose", out var pp) ? pp.GetString() ?? string.Empty : string.Empty;

                decimal totalAmount = 0m; int chequeCount = 0;
                if (payload.TryGetProperty("cheques", out var cheques) && cheques.ValueKind == JsonValueKind.Array)
                {
                    foreach (var c in cheques.EnumerateArray())
                    {
                        if (c.TryGetProperty("amountKES", out var am) && !string.IsNullOrWhiteSpace(am.GetString()))
                        {
                            var s = am.GetString() ?? string.Empty; s = s.Replace(",", "").Replace(" ", "");
                            if (decimal.TryParse(s, System.Globalization.NumberStyles.AllowDecimalPoint, System.Globalization.CultureInfo.InvariantCulture, out var v)) totalAmount += v;
                        }
                        chequeCount++;
                    }
                }

                var ms = new MemoryStream();
                Document.Create(container =>
                {
                    container.Page(page =>
                    {
                        page.Size(PageSizes.A4); page.Margin(28); page.DefaultTextStyle(x => x.FontSize(11));
                        page.Header().AlignCenter().Text("FOR ONWARDS SWIFT LIMITED  OFFICIAL  USE  ONLY").SemiBold().FontSize(12);
                        page.Content().Column(col =>
                        {
                            col.Item().PaddingTop(6).Row(r => { r.RelativeColumn().Text($"Applicant: {applicant}").SemiBold(); r.ConstantColumn(220).Text($"Applicant ID: {idNo}").SemiBold(); });
                            col.Item().PaddingTop(10).Text($"CHEQUE(S) SUMMARY ({chequeCount} cheques) — Total KES: {totalAmount:N2}").SemiBold();
                        });
                        page.Footer().AlignLeft().Text($"Generated: {DateTime.UtcNow:yyyy-MM-dd HH:mm}").FontSize(9);
                    });
                }).GeneratePdf(ms);

                ms.Position = 0;
                return File(ms.ToArray(), "application/pdf", "official-use.pdf");
            }
            catch (Exception ex)
            {
                return Problem(detail: ex.Message);
            }
        }

        private string? ValidateJsonPayload(JsonElement payload)
        {
            try
            {
                if (payload.ValueKind != JsonValueKind.Object) return null;
                if (payload.TryGetProperty("formType", out var ft) && ft.GetString() == "cheque-encashment")
                {
                    if (!payload.TryGetProperty("tcAccepted", out var tc) || tc.ValueKind != JsonValueKind.True) return "Terms & Conditions must be accepted.";
                    if (!payload.TryGetProperty("cheques", out var cheques) || cheques.ValueKind != JsonValueKind.Array || cheques.GetArrayLength() == 0) return "At least one cheque is required.";
                    foreach (var c in cheques.EnumerateArray())
                    {
                        if (!c.TryGetProperty("chequeNumber", out var cn) || string.IsNullOrWhiteSpace(cn.GetString())) return "Each cheque must include a chequeNumber.";
                        if (!c.TryGetProperty("amountKES", out var am) || string.IsNullOrWhiteSpace(am.GetString())) return "Each cheque must include amountKES.";
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
