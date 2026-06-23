using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OnwardsSwift.API.MobileLocal.Configuration;
using OnwardsSwift.API.MobileLocal.Contracts;
using OnwardsSwift.API.MobileLocal.Services;
using OnwardsSwift.Core.Enums;
using OnwardsSwift.Infrastructure.Data;
using System.Data;
using System.Security.Claims;
using System.Text.Json;

namespace OnwardsSwift.API.MobileLocal.Controllers;

/// <summary>
/// Mirrors the mobile cheque/bond routes but writes to the live SQL Server DB
/// (OnwardsSwiftDB) instead of the local SQLite file.
/// Routes: /api/sql/cheques/..., /api/sql/officialuse/{id}, /api/sql/bonds/...
/// </summary>
[Route("api/sql")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class MobileSqlController : MobileApiControllerBase
{
    private readonly DapperContext _sqlCtx;
    private readonly LocalSqliteContext _sqlite;
    private readonly IWebHostEnvironment _env;
    private readonly LocalApiOptions _options;

    public MobileSqlController(
        DapperContext sqlCtx,
        LocalSqliteContext sqlite,
        IWebHostEnvironment env,
        IOptions<LocalApiOptions> options)
    {
        _sqlCtx = sqlCtx;
        _sqlite = sqlite;
        _env = env;
        _options = options.Value;
    }

    // ──────────────────────────────────────────────────────────
    //  CHEQUE ENCASHMENT → dbo.ChequeEncashmentRequests
    // ──────────────────────────────────────────────────────────

    [HttpPost("cheques/request")]
    public async Task<ActionResult<ApiEnvelope<object>>> CreateChequeRequest(
        [FromBody] ChequeRequestCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApplicantName) || string.IsNullOrWhiteSpace(request.Phone))
            return FailEnvelope<object>(400, "applicantName and phone are required.", "validation_error");

        var createdBy = GetCreatedBy();
        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();

        using var conn = _sqlCtx.Create();

        // Idempotency: if the app already submitted this exact reference (e.g. a sync retry
        // after the original response never reached the device), return the existing request
        // instead of creating a duplicate row.
        if (reference != null)
        {
            var existingId = await conn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 Id FROM dbo.ChequeEncashmentRequests WHERE Reference = @reference", new { reference });
            if (existingId.HasValue)
            {
                return OkEnvelope<object>(new { requestId = existingId.Value, id = existingId.Value, status = "pending" }, "Cheque request already exists");
            }
        }

        int requestId;
        try
        {
            requestId = await conn.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.ChequeEncashmentRequests
    (Reference, ApplicantName, IdNumber, PostalAddress, Phone, Purpose, TermsAccepted,
     DeclarantName, DeclarantRole, DeclarantDate, Status, CreatedAt, CreatedBy)
VALUES
    (@Reference, @ApplicantName, @IdNumber, @PostalAddress, @Phone, @Purpose, @TermsAccepted,
     @DeclarantName, @DeclarantRole, @DeclarantDate, @Status, SYSUTCDATETIME(), @CreatedBy);
SELECT CAST(SCOPE_IDENTITY() AS int);", new
            {
                Reference      = reference,
                ApplicantName  = request.ApplicantName.Trim(),
                IdNumber       = request.IdNumber,
                PostalAddress  = request.PostalAddress,
                Phone          = request.Phone.Trim(),
                Purpose        = request.Purpose?.Trim(),
                TermsAccepted  = request.TermsAccepted,
                DeclarantName  = request.DeclarantName,
                DeclarantRole  = request.DeclarantRole,
                DeclarantDate  = request.DeclarantDate,
                Status         = (int)FacilityStatus.Pending,
                CreatedBy      = createdBy
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (reference != null && IsUniqueConstraintViolation(ex))
        {
            // The upfront SELECT above is not atomic with this INSERT (READ COMMITTED) -- a
            // genuinely concurrent retry with the same Reference can race past that check.
            // The filtered unique index is the real guarantee; fall back to it gracefully
            // instead of surfacing a raw SQL error for what is still a successful idempotent call.
            var existingId = await conn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 Id FROM dbo.ChequeEncashmentRequests WHERE Reference = @reference", new { reference });
            if (!existingId.HasValue)
            {
                throw;
            }

            return OkEnvelope<object>(new { requestId = existingId.Value, id = existingId.Value, status = "pending" }, "Cheque request already exists");
        }

        await WriteTransactionHistoryAsync("cheque", requestId, "Cheque Encashment", null, "pending", new { requestId });
        await WriteStatusHistoryAsync(conn, "cheque", requestId, FacilityStatus.Pending, null, createdBy);

        return OkEnvelope<object>(new { requestId, id = requestId, status = "pending" }, "Cheque request created");
    }

    // SQL Server: 2627 = unique constraint (PRIMARY KEY/UNIQUE), 2601 = unique index.
    private static bool IsUniqueConstraintViolation(Microsoft.Data.SqlClient.SqlException ex) =>
        ex.Number == 2627 || ex.Number == 2601;

    // ──────────────────────────────────────────────────────────
    //  CHEQUE ITEMS → dbo.ChequeEncashmentCheques
    // ──────────────────────────────────────────────────────────

    [HttpPost("cheques/items")]
    public async Task<ActionResult<ApiEnvelope<object>>> AddChequeItem(
        [FromBody] ChequeItemCreateRequest request)
    {
        if (request.RequestId <= 0 || string.IsNullOrWhiteSpace(request.ChequeNumber) || request.Amount <= 0)
            return FailEnvelope<object>(400, "requestId, chequeNumber, and positive amount are required.", "validation_error");

        var chequeNumber = request.ChequeNumber.Trim();
        using var conn = _sqlCtx.Create();

        int itemId;
        try
        {
            itemId = await conn.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.ChequeEncashmentCheques
    (RequestId, ChequeNumber, Amount, Dated, Drawer, Bank, Branch, Payee)
VALUES
    (@RequestId, @ChequeNumber, @Amount, @Dated, @Drawer, @Bank, @Branch, @Payee);
SELECT CAST(SCOPE_IDENTITY() AS int);", new
            {
                RequestId     = (int)request.RequestId,
                ChequeNumber  = chequeNumber,
                Amount        = request.Amount,
                Dated         = request.Dated,
                Drawer        = request.Drawer,
                Bank          = request.Bank,
                Branch        = request.Branch,
                Payee         = request.Payee
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (IsUniqueConstraintViolation(ex))
        {
            // Same retry-safety as CreateChequeRequest/CreateBondApplication: a sync retry that
            // resubmits the same cheque number under the same request races the unique index
            // (RequestId, ChequeNumber) instead of creating a duplicate line item.
            var existingId = await conn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 Id FROM dbo.ChequeEncashmentCheques WHERE RequestId = @RequestId AND ChequeNumber = @ChequeNumber",
                new { RequestId = (int)request.RequestId, ChequeNumber = chequeNumber });
            if (!existingId.HasValue)
            {
                throw;
            }

            return OkEnvelope<object>(new { itemId = existingId.Value, requestId = request.RequestId }, "Cheque item already exists");
        }

        return OkEnvelope<object>(new { itemId, requestId = request.RequestId }, "Cheque item added");
    }

    // ──────────────────────────────────────────────────────────
    //  CHEQUE ATTACHMENTS → dbo.ChequeEncashmentAttachments
    // ──────────────────────────────────────────────────────────

    [HttpPost("cheques/attachments")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<ApiEnvelope<object>>> UploadChequeAttachment(
        [FromForm] long requestId,
        [FromForm] string? attachmentType,
        [FromForm] IFormFile? file)
    {
        if (requestId <= 0 || file == null || file.Length == 0)
            return FailEnvelope<object>(400, "requestId and file are required.", "validation_error");

        var saved = await SaveFileAsync("cheques", requestId, file);

        using var conn = _sqlCtx.Create();
        var attachmentId = await conn.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.ChequeEncashmentAttachments (RequestId, FilePath, FileName, ContentType, CreatedAt)
VALUES (@RequestId, @FilePath, @FileName, @ContentType, SYSUTCDATETIME());
SELECT CAST(SCOPE_IDENTITY() AS int);", new
        {
            RequestId   = (int)requestId,
            FilePath    = saved.relativePath,
            FileName    = file.FileName,
            ContentType = file.ContentType ?? "application/octet-stream"
        });

        return OkEnvelope<object>(new { attachmentId, requestId, fileName = file.FileName, fileUrl = saved.publicUrl }, "Attachment uploaded");
    }

    // ──────────────────────────────────────────────────────────
    //  OFFICIAL USE → dbo.OfficialUseRecords
    // ──────────────────────────────────────────────────────────

    [HttpPost("officialuse/{requestId:long}")]
    public Task<ActionResult<ApiEnvelope<object>>> SaveOfficialUse(
        [FromRoute] long requestId,
        [FromForm] OfficialUseCreateRequest request)
    {
        // Official Use (Step 3) is staff-only and web-portal-only — clients submitting via the
        // mobile app must never be able to write approval/signatory data. See FormsController's
        // OfficialUse action (web portal, RBAC-gated) for the only supported way to submit this.
        return Task.FromResult(FailEnvelope<object>(403, "Official Use can only be submitted through the staff web portal.", "forbidden"));
    }

    // ──────────────────────────────────────────────────────────
    //  BOND APPLICATION → dbo.BondApplications
    // ──────────────────────────────────────────────────────────

    [HttpPost("bonds/application")]
    public async Task<ActionResult<ApiEnvelope<object>>> CreateBondApplication(
        [FromBody] BondApplicationCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApplicantName) || string.IsNullOrWhiteSpace(request.Phone))
            return FailEnvelope<object>(400, "applicantName and phone are required.", "validation_error");

        var createdBy = GetCreatedBy();
        var reference = string.IsNullOrWhiteSpace(request.Reference) ? null : request.Reference.Trim();
        var bondTypes = string.Join(", ", request.Types
            .Select(t => t.TypeName)
            .Where(n => !string.IsNullOrWhiteSpace(n)));

        // Store extra mobile fields (phone, email, idNumber, currency, etc.) as JSON
        // since BondApplications schema was designed for the web form.
        var extraJson = JsonSerializer.Serialize(new
        {
            phone       = request.Phone,
            email       = request.Email,
            idNumber    = request.IdNumber,
            tenderName  = request.TenderName,
            currency    = request.Currency,
            tenorDays   = request.TenorDays,
            types       = request.Types
        });

        using var conn = _sqlCtx.Create();

        // Idempotency: a sync retry sending the same reference returns the existing
        // application instead of creating a duplicate row.
        if (reference != null)
        {
            var existingId = await conn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 Id FROM dbo.BondApplications WHERE Reference = @reference", new { reference });
            if (existingId.HasValue)
            {
                return OkEnvelope<object>(new { applicationId = existingId.Value, id = existingId.Value, status = "pending" }, "Bond application already exists");
            }
        }

        int applicationId;
        try
        {
            applicationId = await conn.ExecuteScalarAsync<int>(@"
INSERT INTO dbo.BondApplications
    (Reference, ApplicantName, ApplicantCode, Procuring, GuaranteeFigures, BondTypes, TenderRef,
     AttachmentSummary, Status, CreatedAt, CreatedBy)
VALUES
    (@Reference, @ApplicantName, @Phone, @Procuring, @GuaranteeFigures, @BondTypes, @TenderRef,
     @AttachmentSummary, @Status, SYSUTCDATETIME(), @CreatedBy);
SELECT CAST(SCOPE_IDENTITY() AS int);", new
            {
                Reference         = reference,
                ApplicantName     = request.ApplicantName.Trim(),
                Phone             = request.Phone.Trim(),
                Procuring         = request.ProcuringEntity,
                GuaranteeFigures  = request.Amount.HasValue ? request.Amount.Value.ToString("F2") : null,
                BondTypes         = bondTypes,
                TenderRef         = request.TenderNumber,
                AttachmentSummary = extraJson,
                Status            = (int)FacilityStatus.Pending,
                CreatedBy         = createdBy
            });
        }
        catch (Microsoft.Data.SqlClient.SqlException ex) when (reference != null && IsUniqueConstraintViolation(ex))
        {
            // Same race as CreateChequeRequest -- the upfront SELECT isn't atomic with this
            // INSERT, so fall back to the unique index's guarantee instead of surfacing a raw
            // SQL error for what is still a successful idempotent call.
            var existingId = await conn.ExecuteScalarAsync<int?>(
                "SELECT TOP 1 Id FROM dbo.BondApplications WHERE Reference = @reference", new { reference });
            if (!existingId.HasValue)
            {
                throw;
            }

            return OkEnvelope<object>(new { applicationId = existingId.Value, id = existingId.Value, status = "pending" }, "Bond application already exists");
        }

        await WriteTransactionHistoryAsync("bond", applicationId, "Bond Application", request.Amount, "pending", new { applicationId });
        await WriteStatusHistoryAsync(conn, "bond", applicationId, FacilityStatus.Pending, null, createdBy);

        return OkEnvelope<object>(new { applicationId, id = applicationId, status = "pending" }, "Bond application created");
    }

    // ──────────────────────────────────────────────────────────
    //  BOND INDEMNITY → dbo.BondApplications (update)
    // ──────────────────────────────────────────────────────────

    [HttpPost("bonds/indemnity/{applicationId:long}")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<ApiEnvelope<object>>> SaveBondIndemnity(
        [FromRoute] long applicationId,
        [FromForm] string? indemnitorsJson,
        [FromForm] string? signatoriesJson,
        [FromForm] List<IFormFile>? supportingDocuments)
    {
        if (applicationId <= 0)
            return FailEnvelope<object>(400, "applicationId is required.", "validation_error");

        var indemnitors  = ParseJsonList<BondIndemnitorRequest>(indemnitorsJson);
        var signatories  = ParseJsonList<BondSignatoryRequest>(signatoriesJson);

        var savedPaths = new List<string>();
        if (supportingDocuments != null)
        {
            foreach (var file in supportingDocuments.Where(f => f != null && f.Length > 0))
            {
                var s = await SaveFileAsync("bonds", applicationId, file);
                savedPaths.Add(s.relativePath);
            }
        }

        var summary = JsonSerializer.Serialize(new
        {
            indemnitors  = indemnitors.Select(x => new { x.FullName, x.IdNumber, x.Address }),
            signatories  = signatories.Select(x => new { x.FullName, x.Designation }),
            files        = savedPaths
        });

        using var conn = _sqlCtx.Create();
        await conn.ExecuteAsync(@"
UPDATE dbo.BondApplications SET
    IndemnityName1    = @IndemnityName1,
    IndemnityName2    = @IndemnityName2,
    SigName1          = @SigName1,
    SigName2          = @SigName2,
    CompanySealStamp  = @CompanySealStamp,
    AttachmentSummary = @AttachmentSummary
WHERE Id = @Id;", new
        {
            Id               = (int)applicationId,
            IndemnityName1   = indemnitors.ElementAtOrDefault(0)?.FullName,
            IndemnityName2   = indemnitors.ElementAtOrDefault(1)?.FullName,
            SigName1         = signatories.ElementAtOrDefault(0)?.FullName,
            SigName2         = signatories.ElementAtOrDefault(1)?.FullName,
            CompanySealStamp = savedPaths.FirstOrDefault(),
            AttachmentSummary = summary
        });

        await WriteTransactionHistoryAsync("bond", applicationId, "Bond Indemnity Submitted", null, "indemnity_submitted", new { applicationId });

        return OkEnvelope<object>(new
        {
            applicationId,
            status           = "indemnity_submitted",
            indemnitorsCount = indemnitors.Count,
            signatoriesCount = signatories.Count,
            filesCount       = savedPaths.Count
        }, "Bond indemnity saved");
    }

    // ──────────────────────────────────────────────────────────
    //  FETCH FULL CHEQUE REQUEST (for mobile refresh after web edit)
    // ──────────────────────────────────────────────────────────

    [HttpGet("cheques/{requestId:int}")]
    public async Task<ActionResult<ApiEnvelope<object>>> GetChequeRequest([FromRoute] int requestId)
    {
        using var conn = _sqlCtx.Create();

        var request = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
SELECT Id, ApplicantName, IdNumber, PostalAddress, Phone, Purpose,
       TermsAccepted, DeclarantName, DeclarantRole, DeclarantDate,
       CreatedAt, CreatedBy
FROM dbo.ChequeEncashmentRequests
WHERE Id = @requestId;", new { requestId });

        if (request == null)
            return FailEnvelope<object>(404, "Cheque request not found.", "not_found");

        var cheques = (await conn.QueryAsync<dynamic>(@"
SELECT ChequeNumber, CAST(Amount AS nvarchar(50)) AS Amount, Dated,
       Drawer, Bank, Branch, Payee
FROM dbo.ChequeEncashmentCheques
WHERE RequestId = @requestId
ORDER BY Id;", new { requestId })).ToList();

        var attachments = (await conn.QueryAsync<dynamic>(@"
SELECT FileName, FilePath, ContentType
FROM dbo.ChequeEncashmentAttachments
WHERE RequestId = @requestId
ORDER BY Id;", new { requestId })).ToList();

        // OfficialUse — guard in case table does not exist yet
        object? officialUse = null;
        try
        {
            var ou = await conn.QuerySingleOrDefaultAsync<dynamic>(@"
SELECT TOP 1 ConfirmedWith, Designation, BuildingStreet, DrawerStatus,
             ReasonForPayment, AccountConfirmedBy, AccountStatus
FROM dbo.OfficialUseRecords
WHERE RequestId = @requestId
ORDER BY Id DESC;", new { requestId });

            if (ou != null)
            {
                officialUse = new
                {
                    confirmedWith      = (string?)ou.ConfirmedWith,
                    designation        = (string?)ou.Designation,
                    buildingStreet     = (string?)ou.BuildingStreet,
                    drawerStatus       = (string?)ou.DrawerStatus,
                    reasonForPayment   = (string?)ou.ReasonForPayment,
                    accountConfirmedBy = (string?)ou.AccountConfirmedBy,
                    accountStatus      = (string?)ou.AccountStatus,
                };
            }
        }
        catch { /* table may not exist */ }

        return OkEnvelope<object>(new
        {
            requestId      = (int)request.Id,
            applicantName  = (string?)request.ApplicantName,
            idNumber       = (string?)request.IdNumber,
            postalAddress  = (string?)request.PostalAddress,
            phone          = (string?)request.Phone,
            purpose        = (string?)request.Purpose,
            termsAccepted  = request.TermsAccepted is bool b && b,
            declarantName  = (string?)request.DeclarantName,
            declarantRole  = (string?)request.DeclarantRole,
            declarantDate  = (string?)request.DeclarantDate,
            createdAt      = (object)request.CreatedAt,
            cheques = cheques.Select(c => new
            {
                chequeNumber = (string?)c.ChequeNumber,
                amount       = (string?)c.Amount,
                chequeDate   = (object?)c.Dated,
                drawer       = (string?)c.Drawer,
                bank         = (string?)c.Bank,
                branch       = (string?)c.Branch,
                payee        = (string?)c.Payee,
            }),
            attachments = attachments.Select(a => new
            {
                fileName     = (string?)a.FileName,
                filePath     = (string?)a.FilePath,
                contentType  = (string?)a.ContentType,
            }),
            officialUse,
        }, "Cheque request retrieved");
    }

    // ──────────────────────────────────────────────────────────
    //  STATUS TIMELINE → dbo.RequestStatusHistory
    // ──────────────────────────────────────────────────────────

    [HttpGet("transactions/{type}/{id:int}/timeline")]
    public async Task<ActionResult<ApiEnvelope<object>>> GetStatusTimeline([FromRoute] string type, [FromRoute] int id)
    {
        var sourceType = type?.Trim().ToLowerInvariant();
        if (sourceType != "cheque" && sourceType != "bond")
            return FailEnvelope<object>(400, "type must be 'cheque' or 'bond'.", "validation_error");

        using var conn = _sqlCtx.Create();

        var exists = sourceType == "cheque"
            ? await conn.ExecuteScalarAsync<int?>("SELECT TOP 1 Id FROM dbo.ChequeEncashmentRequests WHERE Id = @id", new { id })
            : await conn.ExecuteScalarAsync<int?>("SELECT TOP 1 Id FROM dbo.BondApplications WHERE Id = @id", new { id });

        if (!exists.HasValue)
            return FailEnvelope<object>(404, "Request not found.", "not_found");

        var rows = (await conn.QueryAsync<dynamic>(@"
SELECT Status, StatusNote, ChangedBy, ChangedAtUtc
FROM dbo.RequestStatusHistory
WHERE SourceType = @sourceType AND SourceId = @id
ORDER BY ChangedAtUtc ASC, Id ASC;", new { sourceType, id })).ToList();

        var events = rows.Select(r => new
        {
            status      = ((FacilityStatus)(int)r.Status).ToString(),
            statusNote  = (string?)r.StatusNote,
            changedBy   = (string?)r.ChangedBy,
            changedAt   = (object)r.ChangedAtUtc,
        }).ToList();

        var currentStatus = events.Count > 0 ? events[^1].status : FacilityStatus.Pending.ToString();

        return OkEnvelope<object>(new { currentStatus, events }, "Status timeline retrieved");
    }

    private async Task WriteStatusHistoryAsync(
        IDbConnection conn, string sourceType, int sourceId, FacilityStatus status, string? note, string? changedBy)
    {
        await conn.ExecuteAsync(@"
INSERT INTO dbo.RequestStatusHistory (SourceType, SourceId, Status, StatusNote, ChangedBy, ChangedAtUtc)
VALUES (@sourceType, @sourceId, @status, @note, @changedBy, SYSUTCDATETIME());", new
        {
            sourceType,
            sourceId,
            status = (int)status,
            note,
            changedBy
        });
    }

    // ──────────────────────────────────────────────────────────
    //  HELPERS
    // ──────────────────────────────────────────────────────────

    private string GetCreatedBy() =>
        User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub") ?? "mobile";

    private async Task WriteTransactionHistoryAsync(
        string sourceType, long sourceId, string title, decimal? amount, string status, object metadata)
    {
        try
        {
            var userId = GetUserIdOrThrow();
            using var conn = _sqlite.CreateConnection();
            await conn.OpenAsync();
            await conn.ExecuteAsync(@"
INSERT INTO transaction_history (user_id, source_type, source_id, title, amount, status, created_at_utc, metadata_json)
VALUES (@userId, @sourceType, @sourceId, @title, @amount, @status, @createdAt, @metadata);", new
            {
                userId,
                sourceType,
                sourceId,
                title,
                amount,
                status,
                createdAt = DateTime.UtcNow.ToString("O"),
                metadata  = JsonSerializer.Serialize(metadata)
            });
        }
        catch
        {
            // SQLite transaction log is non-critical — don't fail the SQL Server write.
        }
    }

    private static List<T> ParseJsonList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<T>();
        try { return JsonSerializer.Deserialize<List<T>>(json) ?? new List<T>(); }
        catch { return new List<T>(); }
    }

    private async Task<(string relativePath, string publicUrl)> SaveFileAsync(string area, long parentId, IFormFile file)
    {
        var safeArea     = area.Trim().ToLowerInvariant();
        var uploadsRoot  = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads",
                               _options.UploadsSubFolder, safeArea, parentId.ToString());
        Directory.CreateDirectory(uploadsRoot);

        var extension    = Path.GetExtension(file.FileName);
        var safeName     = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(uploadsRoot, safeName);

        await using (var stream = System.IO.File.Create(absolutePath))
            await file.CopyToAsync(stream);

        var relativePath = Path.Combine(_options.UploadsSubFolder, safeArea, parentId.ToString(), safeName)
                               .Replace('\\', '/');
        return (relativePath, $"/uploads/{relativePath.TrimStart('/')}");
    }
}
