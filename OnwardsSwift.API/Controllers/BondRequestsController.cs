using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Infrastructure.Data;
using System.Data;
using System.Text;

namespace OnwardsSwift.API.Controllers
{
    [ApiController]
    [Route("api/bond-requests")]
    [Authorize]
    public class BondRequestsController : ControllerBase
    {
        private readonly DapperContext _ctx;

        public BondRequestsController(DapperContext ctx)
        {
            _ctx = ctx;
        }

        [HttpPost]
        public async Task<IActionResult> CreateBondRequest([FromBody] BondRequest request)
        {
            if (request == null)
            {
                return BadRequest("Request body is required.");
            }

            using var conn = _ctx.Create();
            var bondTypes = request.BondTypes?.Any() == true
                ? string.Join(", ", request.BondTypes.Select(ToBondTypeText))
                : string.Empty;

            var attachmentPaths = request.Attachments?.Where(x => !string.IsNullOrWhiteSpace(x.FilePath)).Select(x => x.FilePath.Trim()).ToList() ?? new List<string>();
            var attachmentSummary = attachmentPaths.Any()
                ? string.Join(" | ", attachmentPaths)
                : null;

            var sql = @"
INSERT INTO dbo.BondApplications
    (ApplicantName, ApplicantAddress, ApplicantEmail, ApplicantPhone, Procuring, ProcAddress,
     GuaranteeFigures, GuaranteeWords, BondTypes, TypeOther, TenderRef,
     GuaranteeFrom, GuaranteeTo, SigName1, SigSignature1, SigName2, SigSignature2,
     AttachmentSummary, Status, CreatedAt, CreatedBy)
VALUES
    (@ApplicantName, @ApplicantAddress, @ApplicantEmail, @ApplicantPhone, @Procuring, @ProcAddress,
     @GuaranteeFigures, @GuaranteeWords, @BondTypes, @TypeOther, @TenderRef,
     @GuaranteeFrom, @GuaranteeTo, @SigName1, @SigSignature1, @SigName2, @SigSignature2,
     @AttachmentSummary, @Status, GETUTCDATE(), @CreatedBy);
SELECT CAST(SCOPE_IDENTITY() AS int);";

            var createdId = await conn.QuerySingleAsync<int>(sql, new
            {
                ApplicantName = request.PrincipalName,
                ApplicantAddress = request.PrincipalAddress,
                ApplicantEmail = request.PrincipalEmail,
                ApplicantPhone = request.PrincipalPhone,
                Procuring = request.BeneficiaryName,
                ProcAddress = request.BeneficiaryAddress,
                GuaranteeFigures = request.GuaranteeAmount,
                GuaranteeWords = request.GuaranteeAmountWords,
                BondTypes = bondTypes,
                TypeOther = request.OtherBondType,
                TenderRef = request.TenderReference,
                GuaranteeFrom = request.EffectiveDate,
                GuaranteeTo = request.ExpiryDate,
                SigName1 = request.Signatory1Name,
                SigSignature1 = request.Signatory1SignaturePath,
                SigName2 = request.Signatory2Name,
                SigSignature2 = request.Signatory2SignaturePath,
                AttachmentSummary = attachmentSummary,
                Status = string.IsNullOrWhiteSpace(request.Status) ? "Submitted" : request.Status,
                CreatedBy = User?.Identity?.Name ?? "api-client"
            });

            return Ok(new { id = createdId });
        }

        [HttpPost("{requestId}/indemnity")]
        public async Task<IActionResult> SubmitBondIndemnity(int requestId, [FromBody] BondIndemnity indemnity)
        {
            if (indemnity == null)
            {
                return BadRequest("Request body is required.");
            }

            using var conn = _ctx.Create();
            if (!await TableExistsAsync(conn, "dbo.BondApplications"))
            {
                return NotFound();
            }

            var existingId = await conn.QuerySingleOrDefaultAsync<int?>("SELECT Id FROM dbo.BondApplications WHERE Id = @Id", new { Id = requestId });
            if (!existingId.HasValue)
            {
                return NotFound();
            }

            var date = indemnity.IndemnityDate;
            var updateSql = @"
UPDATE dbo.BondApplications
SET IndemnityDateDay = @Day,
    IndemnityDateMonth = @Month,
    IndemnityDateYear = @Year,
    IndemnityName1 = @AuthorizedSignatoryName,
    IndemnitySignature1 = @AuthorizedSignatorySignaturePath,
    CompanySealStamp = @CompanySealPath,
    IndemnityText = @IndemnityText
WHERE Id = @Id";

            await conn.ExecuteAsync(updateSql, new
            {
                Id = requestId,
                Day = date.Day.ToString(),
                Month = date.ToString("MMMM"),
                Year = date.Year.ToString(),
                indemnity.AuthorizedSignatoryName,
                indemnity.AuthorizedSignatorySignaturePath,
                CompanySealPath = indemnity.CompanySealPath,
                indemnity.IndemnityText
            });

            return Ok(new { success = true });
        }

        [HttpPost("{requestId}/status")]
        public async Task<IActionResult> UpdateBondRequestStatus(int requestId, [FromBody] BondRequestStatusUpdate statusUpdate)
        {
            if (statusUpdate == null)
            {
                return BadRequest("Request body is required.");
            }

            using var conn = _ctx.Create();
            if (!await TableExistsAsync(conn, "dbo.BondApplications"))
            {
                return NotFound();
            }

            var existingId = await conn.QuerySingleOrDefaultAsync<int?>("SELECT Id FROM dbo.BondApplications WHERE Id = @Id", new { Id = requestId });
            if (!existingId.HasValue)
            {
                return NotFound();
            }

            var updateSql = @"
UPDATE dbo.BondApplications
SET Status = @Status,
    StatusNote = @StatusNote
WHERE Id = @Id";

            await conn.ExecuteAsync(updateSql, new
            {
                Id = requestId,
                statusUpdate.Status,
                StatusNote = statusUpdate.Note
            });

            return Ok(new { success = true });
        }

        [HttpGet]
        public async Task<IActionResult> GetBondRequests()
        {
            using var conn = _ctx.Create();
            if (!await TableExistsAsync(conn, "dbo.BondApplications"))
            {
                return Ok(Array.Empty<BondRequest>());
            }

            var rows = await conn.QueryAsync<dynamic>(@"
SELECT Id, ApplicantName, ApplicantAddress, ApplicantEmail, ApplicantPhone, Procuring, ProcAddress,
       GuaranteeFigures, GuaranteeWords, BondTypes, TypeOther, TenderRef,
       GuaranteeFrom, GuaranteeTo, SigName1, SigSignature1, SigName2, SigSignature2,
       AttachmentSummary, Status, StatusNote
FROM dbo.BondApplications
ORDER BY Id DESC");

            var list = rows.Select(row => new BondRequest
            {
                Id = row.Id,
                PrincipalName = row.ApplicantName ?? string.Empty,
                PrincipalEmail = row.ApplicantEmail ?? string.Empty,
                PrincipalPhone = row.ApplicantPhone ?? string.Empty,
                PrincipalAddress = row.ApplicantAddress ?? string.Empty,
                BeneficiaryName = row.Procuring ?? string.Empty,
                BeneficiaryAddress = row.ProcAddress ?? string.Empty,
                GuaranteeAmount = row.GuaranteeFigures ?? string.Empty,
                GuaranteeAmountWords = row.GuaranteeWords ?? string.Empty,
                BondTypes = ParseBondTypes(row.BondTypes as string),
                OtherBondType = row.TypeOther ?? string.Empty,
                TenderReference = row.TenderRef ?? string.Empty,
                EffectiveDate = row.GuaranteeFrom,
                ExpiryDate = row.GuaranteeTo,
                Signatory1Name = row.SigName1 ?? string.Empty,
                Signatory1SignaturePath = row.SigSignature1 ?? string.Empty,
                Signatory2Name = row.SigName2 ?? string.Empty,
                Signatory2SignaturePath = row.SigSignature2 ?? string.Empty,
                Attachments = ParseAttachments(row.AttachmentSummary as string),
                Status = row.Status ?? string.Empty,
                StatusNote = row.StatusNote ?? string.Empty
            }).ToList();

            return Ok(list);
        }

        [HttpGet("{requestId}")]
        public async Task<IActionResult> GetBondRequest(int requestId)
        {
            using var conn = _ctx.Create();
            if (!await TableExistsAsync(conn, "dbo.BondApplications"))
            {
                return NotFound();
            }

            var row = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
SELECT Id, ApplicantName, ApplicantAddress, ApplicantEmail, ApplicantPhone, Procuring, ProcAddress,
       GuaranteeFigures, GuaranteeWords, BondTypes, TypeOther, TenderRef,
       GuaranteeFrom, GuaranteeTo, SigName1, SigSignature1, SigName2, SigSignature2,
       AttachmentSummary, Status, StatusNote
FROM dbo.BondApplications
WHERE Id = @Id;", new { Id = requestId });

            if (row == null)
            {
                return NotFound();
            }

            var result = new BondRequest
            {
                Id = row.Id,
                PrincipalName = row.ApplicantName ?? string.Empty,
                PrincipalEmail = row.ApplicantEmail ?? string.Empty,
                PrincipalPhone = row.ApplicantPhone ?? string.Empty,
                PrincipalAddress = row.ApplicantAddress ?? string.Empty,
                BeneficiaryName = row.Procuring ?? string.Empty,
                BeneficiaryAddress = row.ProcAddress ?? string.Empty,
                GuaranteeAmount = row.GuaranteeFigures ?? string.Empty,
                GuaranteeAmountWords = row.GuaranteeWords ?? string.Empty,
                BondTypes = ParseBondTypes(row.BondTypes as string),
                OtherBondType = row.TypeOther ?? string.Empty,
                TenderReference = row.TenderRef ?? string.Empty,
                EffectiveDate = row.GuaranteeFrom,
                ExpiryDate = row.GuaranteeTo,
                Signatory1Name = row.SigName1 ?? string.Empty,
                Signatory1SignaturePath = row.SigSignature1 ?? string.Empty,
                Signatory2Name = row.SigName2 ?? string.Empty,
                Signatory2SignaturePath = row.SigSignature2 ?? string.Empty,
                Attachments = ParseAttachments(row.AttachmentSummary as string),
                Status = row.Status ?? string.Empty,
                StatusNote = row.StatusNote ?? string.Empty
            };

            return Ok(result);
        }

        private static string ToBondTypeText(BondType type)
        {
            return type switch
            {
                BondType.BidTender => "Bid/Tender bond",
                BondType.Performance => "Performance bond",
                BondType.AdvancePaymentGuarantee => "Advance Payment Guarantee",
                BondType.Retention => "Retention bond",
                BondType.Other => "Other",
                _ => "Other"
            };
        }

        private static List<BondType> ParseBondTypes(string? bondTypes)
        {
            if (string.IsNullOrWhiteSpace(bondTypes)) return new List<BondType>();
            var parts = bondTypes.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
            var list = new List<BondType>();
            foreach (var part in parts.Select(p => p.Trim()))
            {
                if (part.Contains("Bid", StringComparison.OrdinalIgnoreCase)) list.Add(BondType.BidTender);
                else if (part.Contains("Performance", StringComparison.OrdinalIgnoreCase)) list.Add(BondType.Performance);
                else if (part.Contains("Advance", StringComparison.OrdinalIgnoreCase)) list.Add(BondType.AdvancePaymentGuarantee);
                else if (part.Contains("Retention", StringComparison.OrdinalIgnoreCase)) list.Add(BondType.Retention);
                else if (part.Contains("Other", StringComparison.OrdinalIgnoreCase)) list.Add(BondType.Other);
            }
            return list.Distinct().ToList();
        }

        private static List<Attachment> ParseAttachments(string? attachmentSummary)
        {
            if (string.IsNullOrWhiteSpace(attachmentSummary)) return new List<Attachment>();
            return attachmentSummary
                .Split(new[] { '|'}, StringSplitOptions.RemoveEmptyEntries)
                .Select(path => path.Trim())
                .Where(path => !string.IsNullOrWhiteSpace(path))
                .Select(path => new Attachment
                {
                    FilePath = path,
                    FileName = System.IO.Path.GetFileName(path)
                })
                .ToList();
        }

        private static async Task<bool> TableExistsAsync(IDbConnection conn, string tableName)
        {
            var exists = await conn.QuerySingleAsync<int>("SELECT CASE WHEN OBJECT_ID(@TableName, 'U') IS NULL THEN 0 ELSE 1 END", new { TableName = tableName });
            return exists == 1;
        }
    }
}
