using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Dapper;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.Infrastructure.Services
{
    public class ClientService : IClientService
    {
        private readonly DapperContext _ctx;
        public ClientService(DapperContext ctx) => _ctx = ctx;

        private const string ClientSelect = @"
    SELECT 
        c.Id, 
        c.CompanyName,
        ISNULL(c.KraPin, '') AS KraPin,
        ISNULL(c.BusinessRegNumber, '') AS BusinessRegNumber,
        ISNULL(c.ContactPerson, '') AS ContactPerson,
        ISNULL(c.Email, '') AS Email,
        ISNULL(c.Phone, '') AS Phone,
        ISNULL(c.PhoneAlt, '') AS PhoneAlt,
        c.ClientType,
        ISNULL(c.PhysicalAddress, '') AS PhysicalAddress,
        ISNULL(c.PostalAddress, '') AS PostalAddress,
        ISNULL(c.CreditLimit, 0) AS CreditLimit,
        ISNULL(c.UtilisedLimit, 0) AS UtilisedLimit,
        (ISNULL(c.CreditLimit, 0) - ISNULL(c.UtilisedLimit, 0)) AS AvailableLimit,
        ISNULL(c.Status, 1) AS Status,
        ISNULL(c.IsCrbCleared, 0) AS IsCrbCleared,
        c.CreatedAt,
        ISNULL(c.RejectionReason, '') AS RejectionReason,
        ISNULL(c.Notes, '') AS Notes,
        
        (
            (SELECT COUNT(*) FROM Bonds b WHERE b.ClientId = c.Id AND b.isapproved = 0) +
            (SELECT COUNT(*) FROM InvoiceDiscounts i WHERE i.ClientId = c.Id AND i.status = 0) +
            (SELECT COUNT(*) FROM ChequeDiscounts q WHERE q.ClientId = c.Id AND q.status = 0)
        ) AS TotalActiveProducts
        
    FROM Clients c";

        public async Task<ClientResponse> CreateAsync(CreateClientRequest req, string by)
        {
            using var conn = _ctx.Create();

            // 1. Add 'OUTPUT INSERTED.Id' or use 'SELECT SCOPE_IDENTITY()'
            var sql = @"
        INSERT INTO Clients
            (CompanyName, KraPin, ContactPerson, Email, Phone, PhoneAlt, ClientType,
             PhysicalAddress, PostalAddress, BusinessRegNumber,
             CreditLimit, UtilisedLimit, Status, IsCrbCleared, CreatedAt, CreatedBy, IsDeleted)
        VALUES
            (@Co, @Kra, @Con, @Em, @Ph, @PhA, @CT, @Addr, @Post, @Reg, @Lim, 0, 1, 0, GETUTCDATE(), @By, 0);
        
        SELECT CAST(SCOPE_IDENTITY() as int);";

            // 2. Execute and get the new ID
            var newId = await conn.QuerySingleAsync<int>(sql, new
            {
                Co = req.CompanyName,
                Kra = req.KraPin,
                Con = req.ContactPerson,
                Em = req.Email,
                Ph = req.Phone,
                PhA = req.PhoneAlt,
                CT = (int)req.ClientType,
                Addr = req.PhysicalAddress,
                Post = req.PostalAddress,
                Reg = req.BusinessRegNumber,
                Lim = req.CreditLimit,
                By = by
            });

            // 3. Use the newId to fetch the full object
            return await GetByIdAsync(newId) ?? throw new Exception("Client not found after insert.");
        }
        public async Task<ClientResponse?> GetByIdAsync(int id)
        {
            using var conn = _ctx.Create();

            // 1. Change <dynamic> to <ClientResponse>
            // 2. Dapper will automatically map columns to properties
            var client = await conn.QueryFirstOrDefaultAsync<ClientResponse>(
                $"{ClientSelect} WHERE c.Id = @Id ",
                new { Id = id }
            );

            return client;
        }
        public async Task<PagedResult<ClientResponse>> GetAllAsync(int page, int pageSize, string? search)
        {
            var where = string.IsNullOrWhiteSpace(search)
                ? "c.IsDeleted = 0"
                : "c.IsDeleted = 0 AND (c.CompanyName LIKE @S OR c.KraPin LIKE @S OR c.Email LIKE @S)";

            var param = new
            {
                S = $"%{search}%",
                Skip = (page - 1) * pageSize,
                Take = pageSize
            };

            using var conn = _ctx.Create();

            // 1. Get the total count for pagination metadata
            var total = await conn.ExecuteScalarAsync<int>(
                $"SELECT COUNT(*) FROM Clients c WHERE {where}", param);

            // 2. Get the paged rows directly as ClientResponse
            var rows = await conn.QueryAsync<ClientResponse>(
                $@"{ClientSelect} 
           WHERE {where} 
           ORDER BY c.CompanyName 
           OFFSET @Skip ROWS FETCH NEXT @Take ROWS ONLY", param);

            // 3. Return the wrapper object
            return new PagedResult<ClientResponse>
            {
                Items = rows.ToList(),
                TotalCount = total,
                Page = page,
                PageSize = pageSize
            };
        }

        public async Task<bool> UpdateAsync(int id, CreateClientRequest req, string by)
        {
            using var conn = _ctx.Create();
            return await conn.ExecuteAsync(@"
                UPDATE Clients SET
                    CompanyName=@Co, ContactPerson=@Con, Email=@Em, Phone=@Ph, PhoneAlt=@PhA,
                    PhysicalAddress=@Addr, PostalAddress=@Post, BusinessRegNumber=@Reg,
                    UpdatedAt=GETUTCDATE(), UpdatedBy=@By
                WHERE Id=@Id AND IsDeleted=0",
                new
                {
                    Id = id,
                    Co = req.CompanyName,
                    Con = req.ContactPerson,
                    Em = req.Email,
                    Ph = req.Phone,
                    PhA = req.PhoneAlt,
                    Addr = req.PhysicalAddress,
                    Post = req.PostalAddress,
                    Reg = req.BusinessRegNumber,
                    By = by
                }) > 0;
        }

        public async Task<bool> UpdateCreditLimitAsync(int id, decimal limit, string by)
        {
            using var conn = _ctx.Create();
            return await conn.ExecuteAsync(
                "UPDATE Clients SET CreditLimit=@L, UpdatedAt=GETUTCDATE(), UpdatedBy=@By WHERE Id=@Id AND IsDeleted=0",
                new { Id = id, L = limit, By = by }) > 0;
        }

        public async Task<bool> VerifyCrbAsync(int id, string by)
        {
            using var conn = _ctx.Create();
            return await conn.ExecuteAsync(
                "UPDATE Clients SET IsCrbCleared=1, CrbCheckedAt=GETUTCDATE(), Status=3, UpdatedAt=GETUTCDATE(), UpdatedBy=@By WHERE Id=@Id AND IsDeleted=0",
                new { Id = id, By = by }) > 0;
        }

        public async Task<bool> UpdateStatusAsync(int id, int status, string? reason, string by)
        {
            using var conn = _ctx.Create();
            return await conn.ExecuteAsync(@"
                UPDATE Clients SET Status=@S,
                    RejectionReason=CASE WHEN @HasR=1 THEN @R ELSE RejectionReason END,
                    UpdatedAt=GETUTCDATE(), UpdatedBy=@By
                WHERE Id=@Id AND IsDeleted=0",
                new { Id = id, S = status, HasR = string.IsNullOrEmpty(reason) ? 0 : 1, R = reason, By = by }) > 0;
        }

        public async Task<List<dynamic>> GetClientFacilitiesAsync(int clientId)
        {
            using var conn = _ctx.Create();
            var sql = @"
    SELECT 
        b.Id, 
        b.TenderNumber AS ReferenceNo, 
        b.BondTypeId AS Type, 
        b.Amount, 
        b.ApplicationFee AS FinanceFee, 
        -- Status Mapping
        CASE b.isApproved 
            WHEN 1 THEN 'Approved' 
            WHEN 2 THEN 'Rejected' 
            ELSE 'Pending' 
        END AS Status, 
        b.CreatedAt, 
        b.TenderName,
        o.Name as ProcuringEntity,
        ISNULL(c.CompanyName, '') AS ClientName
    FROM Bonds b 
    INNER JOIN Clients c ON c.Id = b.ClientId
    INNER JOIN Obligees o ON o.Id = b.ProcuringEntity
    WHERE b.ClientId = @Cid 
    ORDER BY b.CreatedAt DESC";

            return (await conn.QueryAsync<dynamic>(sql, new { Cid = clientId })).ToList();
        }

     
    }

}
