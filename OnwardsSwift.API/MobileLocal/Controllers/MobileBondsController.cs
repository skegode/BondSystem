using Dapper;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using OnwardsSwift.API.MobileLocal.Configuration;
using OnwardsSwift.API.MobileLocal.Contracts;
using OnwardsSwift.API.MobileLocal.Services;
using System.Data;
using System.Linq;
using System.Text.Json;

namespace OnwardsSwift.API.MobileLocal.Controllers;

[Route("api/bonds")]
[Authorize(AuthenticationSchemes = "Bearer")]
public class MobileBondsController : MobileApiControllerBase
{
    private readonly LocalSqliteContext _db;
    private readonly IWebHostEnvironment _env;
    private readonly LocalApiOptions _options;
    private readonly OnwardsSwift.Infrastructure.Data.DapperContext _sqlCtx;

    public MobileBondsController(LocalSqliteContext db, IWebHostEnvironment env, IOptions<LocalApiOptions> options, OnwardsSwift.Infrastructure.Data.DapperContext sqlCtx)
    {
        _db = db;
        _env = env;
        _options = options.Value;
        _sqlCtx = sqlCtx;
    }

    // Bond type catalog + required documents per type, configured by staff under
    // Admin > Product Types. Consumed by the mobile app's Bond Application wizard
    // (BondStep1Screen / getBondTypeCatalog) -- falls back to a hardcoded list client-side
    // if this ever 404s, so this is safe to evolve independently.
    [HttpGet("types")]
    public async Task<ActionResult<ApiEnvelope<object>>> GetBondTypeCatalog()
    {
        using var conn = _sqlCtx.Create();

        const int bondProductCategory = 1;
        var types = (await conn.QueryAsync<(int Id, string ProductName)>(
            "SELECT Id, ProductName FROM dbo.ProductTypes WHERE ProductType = @Category ORDER BY ProductName",
            new { Category = bondProductCategory })).ToList();

        var documents = (await conn.QueryAsync<(int ProductTypeId, string DocumentKey, string Label, string? Description, bool Required, int SortOrder)>(
            "SELECT ProductTypeId, DocumentKey, Label, Description, Required, SortOrder FROM dbo.ProductTypeDocuments ORDER BY ProductTypeId, SortOrder, Id")).ToList();

        var catalog = types.Select(t => new
        {
            id = t.Id,
            name = t.ProductName,
            documentRequirements = documents
                .Where(d => d.ProductTypeId == t.Id)
                .Select(d => new
                {
                    key = d.DocumentKey,
                    label = d.Label,
                    description = d.Description ?? "",
                    required = d.Required
                })
        });

        return OkEnvelope<object>(catalog, "Bond type catalog loaded");
    }

    [HttpPost("application")]
    public async Task<ActionResult<ApiEnvelope<object>>> CreateApplication([FromBody] BondApplicationCreateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApplicantName) || string.IsNullOrWhiteSpace(request.Phone))
        {
            return FailEnvelope<object>(400, "applicantName and phone are required.", "validation_error");
        }

        var userId = GetUserIdOrThrow();
        var now = DateTime.UtcNow.ToString("O");

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();
        using var tx = await conn.BeginTransactionAsync();

        var applicationId = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO bond_applications(
    user_id, applicant_name, phone, email, id_number, tender_name, tender_number, procuring_entity,
    amount, currency, tenor_days, indemnity_text, status, created_at_utc, updated_at_utc)
VALUES(
    @userId, @applicantName, @phone, @email, @idNumber, @tenderName, @tenderNumber, @procuringEntity,
    @amount, @currency, @tenorDays, @indemnityText, 'pending', @createdAt, @updatedAt);
SELECT last_insert_rowid();", new
        {
            userId,
            applicantName = request.ApplicantName.Trim(),
            phone = request.Phone.Trim(),
            email = request.Email,
            idNumber = request.IdNumber,
            tenderName = request.TenderName,
            tenderNumber = request.TenderNumber,
            procuringEntity = request.ProcuringEntity,
            amount = request.Amount,
            currency = request.Currency,
            tenorDays = request.TenorDays,
            indemnityText = request.IndemnityText,
            createdAt = now,
            updatedAt = now
        }, tx);

        foreach (var type in request.Types)
        {
            if (string.IsNullOrWhiteSpace(type.TypeCode) || string.IsNullOrWhiteSpace(type.TypeName))
            {
                continue;
            }

            await conn.ExecuteAsync(@"
INSERT INTO bond_application_types(application_id, type_code, type_name, created_at_utc)
VALUES(@applicationId, @typeCode, @typeName, @createdAt);", new
            {
                applicationId,
                typeCode = type.TypeCode.Trim(),
                typeName = type.TypeName.Trim(),
                createdAt = now
            }, tx);
        }

        await AddTransactionAsync(conn, tx, userId, "bond", applicationId, "Bond Application", request.Amount, "pending", new { applicationId });
        await tx.CommitAsync();

        return OkEnvelope<object>(new
        {
            applicationId,
            status = "pending",
            createdAtUtc = now
        }, "Bond application created");
    }

    [HttpPost("indemnity/{applicationId:long}")]
    [RequestSizeLimit(25_000_000)]
    public async Task<ActionResult<ApiEnvelope<object>>> SaveIndemnity(
        [FromRoute] long applicationId,
        [FromForm] string? indemnityText,
        [FromForm] string? signatoriesJson,
        [FromForm] string? indemnitorsJson,
        [FromForm] List<IFormFile>? supportingDocuments)
    {
        if (applicationId <= 0)
        {
            return FailEnvelope<object>(400, "applicationId is required.", "validation_error");
        }

        var userId = GetUserIdOrThrow();

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var ownerId = await conn.ExecuteScalarAsync<long?>("SELECT user_id FROM bond_applications WHERE id=@id", new { id = applicationId });
        if (ownerId == null)
        {
            return FailEnvelope<object>(404, "Bond application not found.", "not_found");
        }

        if (ownerId.Value != userId)
        {
            return FailEnvelope<object>(403, "You cannot modify this application.", "forbidden");
        }

        var signatories = ParseJsonList<BondSignatoryRequest>(signatoriesJson);
        var indemnitors = ParseJsonList<BondIndemnitorRequest>(indemnitorsJson);

        using var tx = await conn.BeginTransactionAsync();
        var now = DateTime.UtcNow.ToString("O");

        await conn.ExecuteAsync("UPDATE bond_applications SET indemnity_text=@text, updated_at_utc=@updated WHERE id=@id",
            new { text = indemnityText, updated = now, id = applicationId }, tx);

        await conn.ExecuteAsync("DELETE FROM bond_signatories WHERE application_id=@id", new { id = applicationId }, tx);
        foreach (var signatory in signatories)
        {
            if (string.IsNullOrWhiteSpace(signatory.FullName))
            {
                continue;
            }

            await conn.ExecuteAsync(@"
INSERT INTO bond_signatories(application_id, full_name, designation, phone, id_number, created_at_utc)
VALUES(@applicationId, @fullName, @designation, @phone, @idNumber, @createdAt);", new
            {
                applicationId,
                fullName = signatory.FullName.Trim(),
                designation = signatory.Designation,
                phone = signatory.Phone,
                idNumber = signatory.IdNumber,
                createdAt = now
            }, tx);
        }

        await conn.ExecuteAsync("DELETE FROM bond_indemnitors WHERE application_id=@id", new { id = applicationId }, tx);
        foreach (var indemnitor in indemnitors)
        {
            if (string.IsNullOrWhiteSpace(indemnitor.FullName))
            {
                continue;
            }

            await conn.ExecuteAsync(@"
INSERT INTO bond_indemnitors(application_id, full_name, id_number, phone, email, address, created_at_utc)
VALUES(@applicationId, @fullName, @idNumber, @phone, @email, @address, @createdAt);", new
            {
                applicationId,
                fullName = indemnitor.FullName.Trim(),
                idNumber = indemnitor.IdNumber,
                phone = indemnitor.Phone,
                email = indemnitor.Email,
                address = indemnitor.Address,
                createdAt = now
            }, tx);
        }

        var uploadedDocs = new List<object>();
        if (supportingDocuments != null)
        {
            foreach (var file in supportingDocuments.Where(f => f != null && f.Length > 0))
            {
                var saved = await SaveFileAsync(applicationId, file);
                var docId = await conn.ExecuteScalarAsync<long>(@"
INSERT INTO bond_supporting_documents(application_id, document_type, file_name, content_type, file_size, file_path, created_at_utc)
VALUES(@applicationId, @documentType, @fileName, @contentType, @fileSize, @filePath, @createdAt);
SELECT last_insert_rowid();", new
                {
                    applicationId,
                    documentType = "supporting_document",
                    fileName = file.FileName,
                    contentType = file.ContentType ?? "application/octet-stream",
                    fileSize = file.Length,
                    filePath = saved.relativePath,
                    createdAt = now
                }, tx);

                uploadedDocs.Add(new
                {
                    documentId = docId,
                    fileName = file.FileName,
                    fileUrl = saved.publicUrl
                });
            }
        }

        await conn.ExecuteAsync("UPDATE bond_applications SET status='indemnity_submitted', updated_at_utc=@updated WHERE id=@id",
            new { updated = now, id = applicationId }, tx);

        await AddTransactionAsync(conn, tx, userId, "bond", applicationId, "Bond Indemnity Submitted", null, "indemnity_submitted", new { applicationId });
        await tx.CommitAsync();

        return OkEnvelope<object>(new
        {
            applicationId,
            status = "indemnity_submitted",
            signatoriesCount = signatories.Count,
            indemnitorsCount = indemnitors.Count,
            uploadedDocuments = uploadedDocs
        }, "Bond indemnity saved");
    }

    [HttpGet("{id:long}")]
    public async Task<ActionResult<ApiEnvelope<object>>> GetBond([FromRoute] long id)
    {
        var userId = GetUserIdOrThrow();

        using var conn = _db.CreateConnection();
        await conn.OpenAsync();

        var app = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
SELECT id, user_id, applicant_name, phone, email, id_number, tender_name, tender_number, procuring_entity,
       amount, currency, tenor_days, indemnity_text, status, created_at_utc, updated_at_utc
FROM bond_applications WHERE id=@id", new { id });

        if (app == null)
        {
            return FailEnvelope<object>(404, "Bond application not found.", "not_found");
        }

        if ((long)app.user_id != userId)
        {
            return FailEnvelope<object>(403, "You cannot access this application.", "forbidden");
        }

        var types = (await conn.QueryAsync<dynamic>("SELECT id, application_id, type_code, type_name, created_at_utc FROM bond_application_types WHERE application_id=@id", new { id })).ToList();
        var signatories = (await conn.QueryAsync<dynamic>("SELECT id, application_id, full_name, designation, phone, id_number, created_at_utc FROM bond_signatories WHERE application_id=@id", new { id })).ToList();
        var indemnitors = (await conn.QueryAsync<dynamic>("SELECT id, application_id, full_name, id_number, phone, email, address, created_at_utc FROM bond_indemnitors WHERE application_id=@id", new { id })).ToList();
        var documents = (await conn.QueryAsync<dynamic>("SELECT id, application_id, document_type, file_name, content_type, file_size, file_path, created_at_utc FROM bond_supporting_documents WHERE application_id=@id", new { id }))
            .Select(x => new
            {
                x.id,
                x.application_id,
                x.document_type,
                x.file_name,
                x.content_type,
                x.file_size,
                x.file_path,
                fileUrl = ToPublicUrl((string)x.file_path),
                x.created_at_utc
            }).ToList();

        return OkEnvelope<object>(new
        {
            applicationId = (long)app.id,
            applicantName = (string)app.applicant_name,
            phone = (string)app.phone,
            email = (string?)app.email,
            idNumber = (string?)app.id_number,
            tenderName = (string?)app.tender_name,
            tenderNumber = (string?)app.tender_number,
            procuringEntity = (string?)app.procuring_entity,
            amount = (decimal?)app.amount,
            currency = (string?)app.currency,
            tenorDays = (long?)app.tenor_days,
            indemnityText = (string?)app.indemnity_text,
            status = (string)app.status,
            createdAtUtc = (string)app.created_at_utc,
            updatedAtUtc = (string?)app.updated_at_utc,
            types,
            signatories,
            indemnitors,
            supportingDocuments = documents
        });
    }

    private async Task AddTransactionAsync(
        Microsoft.Data.Sqlite.SqliteConnection conn,
        IDbTransaction tx,
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
        }, tx);
    }

    private static List<T> ParseJsonList<T>(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<T>();
        }

        try
        {
            return JsonSerializer.Deserialize<List<T>>(json) ?? new List<T>();
        }
        catch
        {
            return new List<T>();
        }
    }

    private async Task<(string relativePath, string publicUrl)> SaveFileAsync(long parentId, IFormFile file)
    {
        var uploadsRoot = Path.Combine(_env.ContentRootPath, "wwwroot", "uploads", _options.UploadsSubFolder, "bonds", parentId.ToString());
        Directory.CreateDirectory(uploadsRoot);

        var extension = Path.GetExtension(file.FileName);
        var safeName = $"{Guid.NewGuid():N}{extension}";
        var absolutePath = Path.Combine(uploadsRoot, safeName);

        await using (var stream = System.IO.File.Create(absolutePath))
        {
            await file.CopyToAsync(stream);
        }

        var relativePath = Path.Combine(_options.UploadsSubFolder, "bonds", parentId.ToString(), safeName).Replace('\\', '/');
        return (relativePath, ToPublicUrl(relativePath));
    }

}
