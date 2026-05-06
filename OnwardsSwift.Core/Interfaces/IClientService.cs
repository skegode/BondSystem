using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OnwardsSwift.Core.DTOs;

namespace OnwardsSwift.Core.Interfaces
{
    public interface IClientService
    {
        Task<ClientResponse?> GetByIdAsync(int id);
        Task<PagedResult<ClientResponse>> GetAllAsync(int page, int pageSize, string? search);
        Task<bool> UpdateAsync(int id, CreateClientRequest request, string updatedBy);
        Task<bool> UpdateCreditLimitAsync(int id, decimal limit, string updatedBy);
        Task<bool> VerifyCrbAsync(int id, string updatedBy);
        Task<bool> UpdateStatusAsync(int id, int status, string? rejectionReason, string updatedBy);
        Task<List<dynamic>> GetClientFacilitiesAsync(int clientId);
    }

}
