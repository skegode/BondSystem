using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OnwardsSwift.Core.DTOs;

namespace OnwardsSwift.Core.Interfaces
{
    public interface IChequeDiscountService
    {
        Task<ChequeDiscountResponse> CreateAsync(CreateChequeDiscountRequest request, string createdBy);
        Task<ChequeDiscountResponse?> GetByIdAsync(int id);
        Task<PagedResult<ChequeDiscountResponse>> GetAllAsync(FacilityFilter filter);
        Task<bool> RecordOutcomeAsync(RecordChequeOutcomeRequest request, string updatedBy);
        Task<bool> PresentToBankAsync(int facilityId, string updatedBy);
        Task<CalculateResponse> CalculateCost(CalculateRequest request,string bankid);
    }
}
