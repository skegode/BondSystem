using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OnwardsSwift.Core.DTOs;

namespace OnwardsSwift.Core.Interfaces
{
    public interface IBidBondService
    {
        Task<int> CreateAsync(CreateBidBondRequest request, int createdBy);
        Task<BidBondResponse?> GetByIdAsync(int id);
        Task<PagedResult<BidBondResponse>> GetAllAsync(FacilityFilter filter);
        Task<bool> ResellAsync(ResaleBidBondRequest request, string updatedBy);
        Task<bool> ConvertToPerformanceBondAsync(int bidBondFacilityId, string updatedBy);

        Task<CalculateResponse> CalculateCost(CalculateRequest request, int bankId);

        Task<IEnumerable<PendingBondVM>> GetPendingApprovalsAsync();
        Task<bool> ApproveAsync(ApproveBondRequest request, int approvedByUserId);


    }
}
