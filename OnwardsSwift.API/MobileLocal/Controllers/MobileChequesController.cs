using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OnwardsSwift.API.MobileLocal.Configuration;
using OnwardsSwift.API.MobileLocal.Contracts;
using OnwardsSwift.API.MobileLocal.Services;
using OnwardsSwift.Infrastructure.Data;
using System.Text.Json;

namespace OnwardsSwift.API.MobileLocal.Controllers;

[Route("api/cheques")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class MobileChequesController : MobileApiControllerBase
{
    private readonly LocalSqliteContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly LocalApiOptions _options;
    private readonly DapperContext _sql;

    public MobileChequesController(LocalSqliteContext db, IWebHostEnvironment env, IOptions<LocalApiOptions> options, DapperContext sql)
    {
        _db = db;
        _env = env;
        _options = options.Value;
        _sql = sql;
    }

    [HttpPost("request")]
    public async Task<ActionResult<ApiEnvelope<object>>> CreateRequest([FromBody] ChequeRequestCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApplicantName) || string.IsNullOrWhiteSpace(request.Phone) || string.IsNullOrWhiteSpace(request.Purpose))
        {
            return FailEnvelope<object>(400, "applicantName, phone, and purpose are required.", "validation_error");
        }

        var userId = GetUserIdOrThrow();
        var now = DateTime.UtcNow.ToString("O");

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var requestId = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO cheque_requests(
    user_id, applicant_name, id_number, postal_address, phone, purpose, terms_accepted,
    declarant_name, declarant_role, declarant_date, status, created_at_utc, updated_at_utc)
VALUES(
    @userId, @applicantName, @idNumber, @postalAddress, @phone, @purpose, @termsAccepted,
    @declarantName, @declarantRole, @declarantDate, 'pending', @createdAt, @updatedAt);
SELECT last_insert_rowid();", new
        {
            userId,
            applicantName = request.ApplicantName.Trim(),
            idNumber = request.IdNumber,
            postalAddress = request.PostalAddress,
            phone = request.Phone.Trim(),
            purpose = request.Purpose.Trim(),
            termsAccepted = request.TermsAccepted ? 1 : 0,
            declarantName = request.DeclarantName,
            declarantRole = request.DeclarantRole,
            declarantDate = request.DeclarantDate,
            createdAt = now,
            updatedAt = now
        });

        await AddTransactionAsync(conn, userId, "cheque", requestId, "Cheque Encashment", null, "pending", new { requestId });

        return OkEnvelope<object>(new
        {
            requestId,
            status = "pending",
            createdAtUtc = now
        }, "Cheque request created");
    }

    [HttpPost("items")]
    public async Task<ActionResult<ApiEnvelope<object>>> AddItem([FromBody] ChequeItemCreateRequest request)
    {
        if (request.RequestId <= 0 || string.IsNullOrWhiteSpace(request.ChequeNumber) || request.Amount <= 0)
        {
            return FailEnvelope<object>(400, "requestId, chequeNumber, and positive amount are required.", "validation_error");
        }

        var userId = GetUserIdOrThrow();

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var ownerId = await conn.ExecuteScalarAsync<long?>("SELECT user_id FROM cheque_requests WHERE id=@id", new { id = request.RequestId });
        if (ownerId == null)
        {
            return FailEnvelope<object>(404, "Cheque request not found.", "not_found");
        }

        if (ownerId.Value != userId)
        {
            return FailEnvelope<object>(403, "You cannot modify this request.", "forbidden");
        }

        var itemId = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO cheque_items(request_id, cheque_number, amount, dated, drawer, bank, branch, payee, created_at_utc)
VALUES(@requestId, @chequeNumber, @amount, @dated, @drawer, @bank, @branch, @payee, @createdAt);
SELECT last_insert_rowid();", new
        {
            requestId = request.RequestId,
            chequeNumber = request.ChequeNumber.Trim(),
            amount = request.Amount,
            dated = request.Dated,
            drawer = request.Drawer,
            bank = request.Bank,
            branch = request.Branch,
            payee = request.Payee,
            createdAt = DateTime.UtcNow.ToString("O")
        });

        return OkEnvelope<object>(new { itemId, requestId = request.RequestId }, "Cheque item added");
    }

    [HttpPost("attachments")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<ApiEnvelope<object>>> UploadAttachment([FromForm] long requestId, [FromForm] string attachmentType, [FromForm] IFormFile file)
    {
        if (requestId <= 0 || string.IsNullOrWhiteSpace(attachmentType) || file == null || file.Length == 0)
        {
            return FailEnvelope<object>(400, "requestId, attachmentType, and file are required.", "validation_error");
        }

        var userId = GetUserIdOrThrow();

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var ownerId = await conn.ExecuteScalarAsync<long?>("SELECT user_id FROM cheque_requests WHERE id=@id", new { id = requestId });
        if (ownerId == null)
        {
            return FailEnvelope<object>(404, "Cheque request not found.", "not_found");
        }

        if (ownerId.Value != userId)
        {
            return FailEnvelope<object>(403, "You cannot modify this request.", "forbidden");
        }

        var saved = await SaveFileAsync("cheques", requestId, file);

        var attachmentId = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO cheque_attachments(request_id, attachment_type, file_name, content_type, file_size, file_path, created_at_utc)
VALUES(@requestId, @attachmentType, @fileName, @contentType, @fileSize, @filePath, @createdAt);
SELECT last_insert_rowid();", new
        {
            requestId,
            attachmentType = attachmentType.Trim().ToLowerInvariant(),
            fileName = file.FileName,
            contentType = file.ContentType ?? "application/octet-stream",
            fileSize = file.Length,
            filePath = saved.relativePath,
            createdAt = DateTime.UtcNow.ToString("O")
        });

        return OkEnvelope<object>(new
        {
            attachmentId,
            requestId,
            attachmentType,
            fileName = file.FileName,
            fileUrl = saved.publicUrl
        }, "Attachment uploaded");
    }

    [HttpPost("/api/officialuse/{requestId:long}")]
    public async Task<ActionResult<ApiEnvelope<object>>> SaveOfficialUse(
        [FromRoute] long requestId,
        [FromForm] OfficialUseCreateRequest request,
        [FromForm] IFormFile? checkedSignature,
        [FromForm] IFormFile? headOfTradeSignature,
        [FromForm] IFormFile? inChargeFinanceSignature,
        [FromForm] IFormFile? ceoSignature,
        [FromForm] IFormFile? paidBySignature)
    {
        if (requestId <= 0)
        {
            return FailEnvelope<object>(400, "requestId is required.", "validation_error");
        }

        var userId = GetUserIdOrThrow();

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var ownerId = await conn.ExecuteScalarAsync<long?>("SELECT user_id FROM cheque_requests WHERE id=@id", new { id = requestId });
        if (ownerId == null)
        {
            return FailEnvelope<object>(404, "Cheque request not found.", "not_found");
        }

        if (ownerId.Value != userId)
        {
            return FailEnvelope<object>(403, "You cannot modify this request.", "forbidden");
        }

        var checkedPath = checkedSignature == null ? null : (await SaveFileAsync("officialuse", requestId, checkedSignature)).relativePath;
        var headPath = headOfTradeSignature == null ? null : (await SaveFileAsync("officialuse", requestId, headOfTradeSignature)).relativePath;
        var financePath = inChargeFinanceSignature == null ? null : (await SaveFileAsync("officialuse", requestId, inChargeFinanceSignature)).relativePath;
        var ceoPath = ceoSignature == null ? null : (await SaveFileAsync("officialuse", requestId, ceoSignature)).relativePath;
        var paidByPath = paidBySignature == null ? null : (await SaveFileAsync("officialuse", requestId, paidBySignature)).relativePath;

        var exists = await conn.ExecuteScalarAsync<long>("SELECT COUNT(1) FROM official_use_records WHERE request_id=@requestId", new { requestId });
        var now = DateTime.UtcNow.ToString("O");

        if (exists == 0)
        {
            await conn.ExecuteAsync(@"
INSERT INTO official_use_records(
    request_id, checked_by, checked_signature_path, checked_date, confirmed_with, designation,
    building_street, drawer_status, reason_for_payment, account_confirmed_by, account_status,
    head_of_trade_finance, head_of_trade_signature_path, head_of_trade_date,
    in_charge_finance, in_charge_finance_signature_path, in_charge_finance_date,
    ceo, ceo_signature_path, ceo_date, paid_by_name, paid_by_signature_path, created_at_utc, updated_at_utc)
VALUES(
    @requestId, @checkedBy, @checkedSignaturePath, @checkedDate, @confirmedWith, @designation,
    @buildingStreet, @drawerStatus, @reasonForPayment, @accountConfirmedBy, @accountStatus,
    @headOfTradeFinance, @headOfTradeSignaturePath, @headOfTradeDate,
    @inChargeFinance, @inChargeFinanceSignaturePath, @inChargeFinanceDate,
    @ceo, @ceoSignaturePath, @ceoDate, @paidByName, @paidBySignaturePath, @createdAt, @updatedAt);", MapOfficialUseParams(requestId, request, checkedPath, headPath, financePath, ceoPath, paidByPath, now));
        }
        else
        {
            await conn.ExecuteAsync(@"
UPDATE official_use_records
SET checked_by = @checkedBy,
    checked_signature_path = COALESCE(@checkedSignaturePath, checked_signature_path),
    checked_date = @checkedDate,
    confirmed_with = @confirmedWith,
    designation = @designation,
    building_street = @buildingStreet,
    drawer_status = @drawerStatus,
    reason_for_payment = @reasonForPayment,
    account_confirmed_by = @accountConfirmedBy,
    account_status = @accountStatus,
    head_of_trade_finance = @headOfTradeFinance,
    head_of_trade_signature_path = COALESCE(@headOfTradeSignaturePath, head_of_trade_signature_path),
    head_of_trade_date = @headOfTradeDate,
    in_charge_finance = @inChargeFinance,
    in_charge_finance_signature_path = COALESCE(@inChargeFinanceSignaturePath, in_charge_finance_signature_path),
    in_charge_finance_date = @inChargeFinanceDate,
    ceo = @ceo,
    ceo_signature_path = COALESCE(@ceoSignaturePath, ceo_signature_path),
    ceo_date = @ceoDate,
    paid_by_name = @paidByName,
    paid_by_signature_path = COALESCE(@paidBySignaturePath, paid_by_signature_path),
    updated_at_utc = @updatedAt
WHERE request_id = @requestId;", MapOfficialUseParams(requestId, request, checkedPath, headPath, financePath, ceoPath, paidByPath, now));
        }

        await conn.ExecuteAsync("UPDATE cheque_requests SET status='official_use_completed', updated_at_utc=@updatedAt WHERE id=@id", new { updatedAt = now, id = requestId });
        await AddTransactionAsync(conn, userId, "cheque", requestId, "Official Use Updated", null, "official_use_completed", new { requestId });

        return OkEnvelope<object>(new { requestId, status = "official_use_completed" }, "Official use record saved");
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiEnvelope<object>>> GetCheque([FromRoute] long id)
    {
        var userId = GetUserIdOrThrow();

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var header = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT id, user_id, applicant_name, id_number, postal_address, phone, purpose, terms_accepted,
       declarant_name, declarant_role, declarant_date, status, created_at_utc, updated_at_utc
FROM cheque_requests
WHERE id = @id", new { id });

        if (header != null)
        {
            if ((long)header.user_id != userId)
                return FailEnvelope<object>(403, "You cannot access this request.", "forbidden");

            var items = (await conn.QueryAsync<dynamic>(@"
SELECT id, request_id, cheque_number, amount, dated, drawer, bank, branch, payee, created_at_utc
FROM cheque_items WHERE request_id = @id ORDER BY id", new { id })).ToList();

            var attachments = (await conn.QueryAsync<dynamic>(@"
SELECT id, request_id, attachment_type, file_name, content_type, file_size, file_path, created_at_utc
FROM cheque_attachments WHERE request_id = @id ORDER BY id", new { id }))
                .Select(x => new
                {
                    x.id, x.request_id, x.attachment_type, x.file_name,
                    x.content_type, x.file_size, x.file_path,
                    fileUrl = ToPublicUrl((string)x.file_path),
                    x.created_at_utc
                }).ToList();

            var officialUse = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT * FROM official_use_records WHERE request_id = @id", new { id });

            return OkEnvelope<object>(new
            {
                requestId = (long)header.id,
                applicantName = (string)header.applicant_name,
                idNumber = (string?)header.id_number,
                postalAddress = (string?)header.postal_address,
                phone = (string)header.phone,
                purpose = (string)header.purpose,
                termsAccepted = Convert.ToInt32(header.terms_accepted) == 1,
                declarantName = (string?)header.declarant_name,
                declarantRole = (string?)header.declarant_role,
                declarantDate = (string?)header.declarant_date,
                status = (string)header.status,
                createdAtUtc = (string)header.created_at_utc,
                updatedAtUtc = (string?)header.updated_at_utc,
                items,
                attachments,
                officialUse,
                source = "local"
            });
        }

        // Fallback: check SQL Server for records submitted by this mobile user
        using var sqlConn = _sql.CreateConnection();
        var sqlHeader = await sqlConn.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT r.Id, r.ApplicantName, r.IdNumber, r.Phone, r.Purpose, r.CreatedBy,
       CONVERT(varchar(30), r.CreatedAt, 127) AS CreatedAt
FROM dbo.ChequeEncashmentRequests r
WHERE r.Id = @id AND r.CreatedBy = @createdBy", new { id, createdBy = userId.ToString() });

        if (sqlHeader == null)
            return FailEnvelope<object>(404, "Cheque request not found.", "not_found");

        var sqlItems = (await sqlConn.QueryAsync<dynamic>(@"
SELECT Id AS id, RequestId AS request_id, ChequeNumber AS cheque_number,
       Amount AS amount, Dated AS dated, Drawer AS drawer, Bank AS bank,
       NULL AS branch, NULL AS payee,
       CONVERT(varchar(30), GETUTCDATE(), 127) AS created_at_utc
FROM dbo.ChequeEncashmentCheques
WHERE RequestId = @id ORDER BY Id", new { id })).ToList();

        var sqlAttachments = (await sqlConn.QueryAsync<dynamic>(@"
SELECT Id AS id, RequestId AS request_id, 'attachment' AS attachment_type,
       FileName AS file_name, ContentType AS content_type, 0 AS file_size,
       FilePath AS file_path,
       CONVERT(varchar(30), CreatedAt, 127) AS created_at_utc
FROM dbo.ChequeEncashmentAttachments
WHERE RequestId = @id ORDER BY Id", new { id }))
            .Select(x => new
            {
                x.id, x.request_id, x.attachment_type, x.file_name,
                x.content_type, x.file_size, x.file_path,
                fileUrl = ToPublicUrl((string)x.file_path),
                x.created_at_utc
            }).ToList();

        return OkEnvelope<object>(new
        {
            requestId = (long)sqlHeader.Id,
            applicantName = (string?)sqlHeader.ApplicantName,
            idNumber = (string?)sqlHeader.IdNumber,
            postalAddress = (string?)null,
            phone = (string?)sqlHeader.Phone,
            purpose = (string?)sqlHeader.Purpose,
            termsAccepted = false,
            declarantName = (string?)null,
            declarantRole = (string?)null,
            declarantDate = (string?)null,
            status = "pending",
            createdAtUtc = (string)sqlHeader.CreatedAt,
            updatedAtUtc = (string?)null,
            items = sqlItems,
            attachments = sqlAttachments,
            officialUse = (object?)null,
            source = "portal"
        });
    }

    private async Task AddTransactionAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        long userId,
        string sourceType,
        long sourceId,
        string title,
        decimal? amount,
        string status,
        object metadata)
    {
        await conn.ExecuteAsync(@"
INSERT INTO transaction_history(user_id, source_type, source_id, title, amount, status, created_at_utc, metadata_json)
VALUES(@userId, @sourceType, @sourceId, @title, @amount, @status, @createdAt, @metadata);", new
        {
            userId,
            sourceType,
            sourceId,
            title,
            amount,
            status,
            createdAt = DateTime.UtcNow.ToString("O"),
            metadata = JsonSerializer.Serialize(metadata)
        });
    }

    private object MapOfficialUseParams(
        long requestId,
        OfficialUseCreateRequest request,
        string? checkedPath,
        string? headPath,
        string? financePath,
        string? ceoPath,
        string? paidByPath,
        string now)
    {
        return new
        {
            requestId,
            checkedBy = request.CheckedBy,
            checkedSignaturePath = checkedPath,
            checkedDate = request.CheckedDate,
            confirmedWith = request.ConfirmedWith,
            designation = request.Designation,
            buildingStreet = request.BuildingStreet,
            drawerStatus = request.DrawerStatus,
            reasonForPayment = request.ReasonForPayment,
            accountConfirmedBy = request.AccountConfirmedBy,
            accountStatus = request.AccountStatus,
            headOfTradeFinance = request.HeadOfTradeFinance,
            headOfTradeSignaturePath = headPath,
            headOfTradeDate = request.HeadOfTradeDate,
            inChargeFinance = request.InChargeFinance,
            inChargeFinanceSignaturePath = financePath,
            inChargeFinanceDate = request.InChargeFinanceDate,
            ceo = request.Ceo,
            ceoSignaturePath = ceoPath,
            ceoDate = request.CeoDate,
            paidByName = request.PaidByName,
            paidBySignaturePath = paidByPath,
            createdAt = now,
            updatedAt = now
        };
    }

    private async Task<(string relativePath, string publicUrl)> SaveFileAsync(string area, long parentId, IFormFile file)
    {
        var safeArea = area.Trim().ToLowerInvariant();
        var uploadsRoot = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads", _options.UploadsSubFolder, safeArea, parentId.ToString());
        Directory.CreateDirectory(uploadsRoot);

        var extension = Path.GetExtension(file.FileName);
        var safeName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(uploadsRoot, safeName);

        await using (var stream = System.IO.File.Create(absolutePath))
        {
            await file.CopyToAsync(stream);
        }

        var relativePath = Path.Combine(_options.UploadsSubFolder, safeArea, parentId.ToString(), safeName).Replace('\\', '/');
        return (relativePath, ToPublicUrl(relativePath));
    }

}
