using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Enums;

namespace OnwardsSwift.Core.Interfaces
{
 
 

    public interface IInvoiceDiscountService
    {
        // Added productId here to match your implementation
        Task<InvoiceDiscountResponse> CreateAsync(CreateInvoiceDiscountRequest request, string createdBy, int productId);
        Task<InvoiceDiscountResponse?> GetByIdAsync(int id);
        Task<PagedResult<InvoiceDiscountResponse>> GetAllAsync(FacilityFilter filter);
        Task<bool> RecordDebtorPaymentAsync(RecordDebtorPaymentRequest request, string updatedBy);

        // This must be implemented in the class
        Task<CalculateResponse> CalculateCost(CalculateRequest req, string bankId);
    }



    public interface ICalculatorService
    {
        Task<CalculateResponse> Calculate(CalculateRequest req, string bankId);
    }

    public interface IAuthService
    {
        Task<LoginResponse?> LoginAsync(LoginRequest request);
        Task<bool> ChangePasswordAsync(string userId, ChangePasswordRequest request);
        string GenerateJwtToken(string id, string email, string fullName, string role);
    }

    public interface INotificationService
    {
        Task<OnwardsSwift.Core.DTOs.EmailSendResult> SendEmailAsync(string to, string subject, string htmlBody, string? plainBody = null);
        Task SendSmsAsync(string phone, string message);
    }
}
