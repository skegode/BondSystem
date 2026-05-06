using OnwardsSwift.Core.Enums;
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace OnwardsSwift.Core.DTOs
{
    // ─────────────────────────────────────────────
    // AUTH
    // ─────────────────────────────────────────────
    public class LoginRequest
    {
        [Required, EmailAddress] public string Email    { get; set; } = string.Empty;
        [Required]               public string Password { get; set; } = string.Empty;
    }

    public class LoginResponse
    {
        public string Token        { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public DateTime ExpiresAt  { get; set; }
        public string FullName     { get; set; } = string.Empty;
        public string Email        { get; set; } = string.Empty;
        public string Role         { get; set; } = string.Empty;
    }

    public class ChangePasswordRequest
    {
        [Required]               public string CurrentPassword { get; set; } = string.Empty;
        [Required, MinLength(8)] public string NewPassword     { get; set; } = string.Empty;
        [Required]               public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class ForgotPasswordRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
    }

    public class VerifyOtpRequest
    {
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required]               public string Otp   { get; set; } = string.Empty;
    }

    public class ResetPasswordViaTokenRequest
    {
        [Required, EmailAddress] public string Email       { get; set; } = string.Empty;
        [Required]               public string ResetToken  { get; set; } = string.Empty;
        [Required, MinLength(8)] public string NewPassword { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────
    // SHARED
    // ─────────────────────────────────────────────
    public class ApiResponse<T>
    {
        public bool        Success { get; set; }
        public string?     Message { get; set; }
        public T?          Data    { get; set; }
        public List<string> Errors { get; set; } = new();

        public static ApiResponse<T> Ok(T data, string? message = null) =>
            new() { Success = true, Data = data, Message = message };
        public static ApiResponse<T> Fail(string error) =>
            new() { Success = false, Errors = new List<string> { error } };
        public static ApiResponse<T> Fail(List<string> errors) =>
            new() { Success = false, Errors = errors };
    }

    public class PagedResult<T>
    {
        public List<T> Items    { get; set; } = new();
        public int TotalCount   { get; set; }
        public int Page         { get; set; }
        public int PageSize     { get; set; }
        public int TotalPages   => (int)Math.Ceiling((double)TotalCount / PageSize);
    }



public class CreateClientRequest
    {
        // Registration Type (1: Individual, 2: Corporate)
        [Required]
        public int Category { get; set; } = 1;

        [Required, MaxLength(200)]
        public string CompanyName { get; set; } = string.Empty;

        [Required, MaxLength(15)]
        public string KraPin { get; set; } = string.Empty;

        // --- Individual Specific Fields ---
        [MaxLength(50)]
        public string? IdNumber { get; set; }

        public int? Gender { get; set; } // 1: Male, 2: Female, 3: Other

        [DataType(DataType.Date)]
        public DateTime? DateOfBirth { get; set; }

        // --- Contact Information ---
        [Required, MaxLength(150)]
        public string ContactPerson { get; set; } = string.Empty;

        [EmailAddress]
        public string Email { get; set; } = string.Empty;

        [Required, Phone]
        public string Phone { get; set; } = string.Empty;

        public string PhoneAlt { get; set; } = string.Empty;

        public ClientType ClientType { get; set; }

        // --- Location ---
        public string PhysicalAddress { get; set; } = string.Empty;

        public string PostalAddress { get; set; } = string.Empty;

        // --- Corporate Specific Fields ---
        public string BusinessRegNumber { get; set; } = string.Empty;

        [Range(0, double.MaxValue)]
        public decimal CreditLimit { get; set; }

        // --- KYC File Uploads (Not mapped to DB directly) ---
        public IFormFile? IdFrontFile { get; set; }
        public IFormFile? IdBackFile { get; set; }
        public IFormFile? PassportPhotoFile { get; set; }
        public IFormFile? RegCertFile { get; set; }
    }

    public class ClientResponse
    {
        public int     Id              { get; set; }
        public string   CompanyName     { get; set; } = string.Empty;
        public string   KraPin          { get; set; } = string.Empty;
        public string   ContactPerson   { get; set; } = string.Empty;
        public string   Email           { get; set; } = string.Empty;
        public string   Phone           { get; set; } = string.Empty;
        public string   ClientType      { get; set; } = string.Empty;
        public string   PhysicalAddress { get; set; } = string.Empty;
        public decimal  CreditLimit     { get; set; }
        public decimal  UtilisedLimit   { get; set; }
        public decimal  AvailableLimit  { get; set; }
        public string   Status          { get; set; } = string.Empty;
        public string?  RejectionReason { get; set; }
        public string?  ApprovalNotes   { get; set; }
        public string?  ApprovedProducts{ get; set; }
        public string?  Notes           { get; set; }
        public bool     IsCrbCleared    { get; set; }
        public DateTime CreatedAt       { get; set; }
        public int      TotalFacilities { get; set; }
    }

    public class ClientStatusRequest
    {
        [Required] public string  Status          { get; set; } = string.Empty;
        public string? RejectionReason { get; set; }
    }

    // ─────────────────────────────────────────────
    // FACILITY SHARED
    // ─────────────────────────────────────────────
    public class FacilityListItem
    {
        public int      Id          { get; set; }
        public string    ReferenceNo { get; set; } = string.Empty;
        public string    Type        { get; set; } = string.Empty;
        public string    ClientName  { get; set; } = string.Empty;
        public int      ClientId    { get; set; }
        public decimal   Amount      { get; set; }
        public decimal   Rate        { get; set; }
        public int       TenorDays   { get; set; }
        public decimal   FinanceFee  { get; set; }
        public decimal   NetAmount   { get; set; }
        public string    Status      { get; set; } = string.Empty;
        public DateTime  CreatedAt   { get; set; }
        public DateTime? DisbursedAt { get; set; }
    }

    public class FacilityFilter
    {
        public FacilityType?   Type     { get; set; }
        public FacilityStatus? Status   { get; set; }
        public Guid?           ClientId { get; set; }
        public DateTime?       FromDate { get; set; }
        public DateTime?       ToDate   { get; set; }
        public string?         Search   { get; set; }
        public int Page     { get; set; } = 1;
        public int PageSize { get; set; } = 20;
    }

    public class ApproveRequest
    {
        public string? Notes               { get; set; }
        public string? DisbursementAccount { get; set; }
        public string? DisbursementBank    { get; set; }
    }

    public class RejectRequest
    {
        [Required] public string Reason { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────
    // DASHBOARD
    // ─────────────────────────────────────────────
    public class DashboardSummary
    {
        public int     TotalFacilities          { get; set; }
        public int     PendingApplications      { get; set; }
        public int     ApprovedFacilities       { get; set; }
        public int     DisbursedFacilities      { get; set; }
        public int     RejectedFacilities       { get; set; }
        public decimal TotalPortfolioValue      { get; set; }
        public decimal TotalDisbursed           { get; set; }
        public decimal TotalFeesEarned          { get; set; }
        public int     TotalClients             { get; set; }
        public int     ActiveClients            { get; set; }
        public int     BidBondCount             { get; set; }
        public int     InvoiceDiscountCount     { get; set; }
        public int     ChequeDiscountCount      { get; set; }
        public decimal BidBondPortfolio         { get; set; }
        public decimal InvoiceDiscountPortfolio { get; set; }
        public decimal ChequeDiscountPortfolio  { get; set; }
        public List<FacilityListItem> RecentApplications { get; set; } = new();
        public List<MonthlyStats>     MonthlyStats        { get; set; } = new();
    }

    public class MonthlyStats
    {
        public string  Month           { get; set; } = string.Empty;
        public int     FacilitiesCount { get; set; }
        public decimal TotalAmount     { get; set; }
        public decimal FeesEarned      { get; set; }
    }










 

    public class ResaleBidBondRequest
    {
        [Required] public Guid   FacilityId   { get; set; }
        [Required] public string ResalePartner { get; set; } = string.Empty;
        [Required, Range(1, double.MaxValue)] public decimal ResaleAmount { get; set; }
        public string? Notes { get; set; }
    }

    // ─────────────────────────────────────────────
    // INVOICE DISCOUNTING
    // ─────────────────────────────────────────────
    public class CreateInvoiceDiscountRequest
    {
        [Required] public Guid     ClientId         { get; set; }
        [Required, MaxLength(100)] public string InvoiceNumber { get; set; } = string.Empty;
        [Required, MaxLength(300)] public string DebtorName   { get; set; } = string.Empty;
        public string DebtorKraPin  { get; set; } = string.Empty;
        public string DebtorContact { get; set; } = string.Empty;
        [Required] public DateTime  InvoiceDate      { get; set; }
        [Required] public DateTime  InvoiceDueDate   { get; set; }
        [Required, Range(50000, 100_000_000)] public decimal InvoiceFaceValue  { get; set; }
        [Required, Range(60, 80)]             public decimal AdvancePercentage { get; set; }
        [Required] public string DisbursementAccount { get; set; } = string.Empty;
        [Required] public string DisbursementBank    { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    public class InvoiceDiscountResponse
    {
        public Guid      Id               { get; set; }
        public Guid      FacilityId       { get; set; }
        public string    ReferenceNo      { get; set; } = string.Empty;
        public string    ClientName       { get; set; } = string.Empty;
        public string    InvoiceNumber    { get; set; } = string.Empty;
        public string    DebtorName       { get; set; } = string.Empty;
        public DateTime  InvoiceDate      { get; set; }
        public DateTime  InvoiceDueDate   { get; set; }
        public decimal   InvoiceFaceValue { get; set; }
        public decimal   AdvancePercentage{ get; set; }
        public decimal   AdvanceAmount    { get; set; }
        public decimal   DiscountFee      { get; set; }
        public decimal   NetAdvance       { get; set; }
        public int       TenorDays        { get; set; }
        public string    Status           { get; set; } = string.Empty;
        public bool      DebtorPaid       { get; set; }
        public DateTime? DebtorPaymentDate{ get; set; }
        public DateTime  CreatedAt        { get; set; }
    }

    public class RecordDebtorPaymentRequest
    {
        [Required] public Guid    FacilityId    { get; set; }
        [Required, Range(1, double.MaxValue)] public decimal PaymentAmount { get; set; }
        [Required] public DateTime PaymentDate  { get; set; }
        public string? Notes { get; set; }
    }

    // ─────────────────────────────────────────────
    // CHEQUE DISCOUNTING
    // ─────────────────────────────────────────────
    public class CreateChequeDiscountRequest
    {
        [Required] public Guid     ClientId       { get; set; }
        [Required, MaxLength(50)]  public string ChequeNumber { get; set; } = string.Empty;
        [Required, MaxLength(300)] public string DrawerName  { get; set; } = string.Empty;
        public string DrawerKraPin  { get; set; } = string.Empty;
        [Required] public int  DraweeBank     { get; set; }
        public string DraweeBranch  { get; set; } = string.Empty;
        [Required] public DateTime  ChequeDate     { get; set; }
        [Required] public DateTime  MaturityDate   { get; set; }
        [Required, Range(50000, 50_000_000)] public decimal ChequeFaceValue { get; set; }
        [Required] public string DisbursementAccount { get; set; } = string.Empty;
        [Required] public string DisbursementBank    { get; set; } = string.Empty;
        public string? Notes { get; set; }
    }

    public class ChequeDiscountResponse
    {
        public Guid     Id              { get; set; }
        public Guid     FacilityId      { get; set; }
        public string   ReferenceNo     { get; set; } = string.Empty;
        public string   ClientName      { get; set; } = string.Empty;
        public string   ChequeNumber    { get; set; } = string.Empty;
        public string   DrawerName      { get; set; } = string.Empty;
        public string   DraweeBank      { get; set; } = string.Empty;
        public DateTime ChequeDate      { get; set; }
        public DateTime MaturityDate    { get; set; }
        public decimal  ChequeFaceValue { get; set; }
        public decimal  AdvanceAmount   { get; set; }
        public decimal  DiscountFee     { get; set; }
        public decimal  NetAdvance      { get; set; }
        public int      TenorDays       { get; set; }
        public string   Status          { get; set; } = string.Empty;
        public bool     PresentedToBank { get; set; }
        public bool     Honoured        { get; set; }
        public bool     Bounced         { get; set; }
        public string?  BounceReason    { get; set; }
        public DateTime CreatedAt       { get; set; }
    }

    public class RecordChequeOutcomeRequest
    {
        [Required] public Guid   FacilityId  { get; set; }
        [Required] public bool   Honoured    { get; set; }
        public string? BounceReason { get; set; }
        public string? Notes        { get; set; }
    }

    // ─────────────────────────────────────────────
    // CALCULATOR
    // ─────────────────────────────────────────────
    public class CalculateRequest
    {
        [Required] public int ProductType { get; set; }
        [Required, Range(50000, 500_000_000)] public decimal Amount     { get; set; }
        [Required, Range(1, 365)]             public int     TenorDays  { get; set; }
        [Range(60, 80)] public decimal? AdvancePercentage { get; set; }
    }

    public class CalculateResponse
    {
        public decimal PrincipalAmount  { get; set; }
        public decimal Rate             { get; set; }
        public int     TenorDays        { get; set; }
        public decimal FinanceFee       { get; set; }
        public decimal AdvanceAmount    { get; set; }
        public decimal NetToClient      { get; set; }
        public string  ProductType      { get; set; } = string.Empty;
        public string  RateDescription  { get; set; } = string.Empty;
        public decimal ApplicationFee { get; set; }
    }

    // ─────────────────────────────────────────────
    // CASH COVER
    // ─────────────────────────────────────────────
    public class CashCoverRequest
    {
        [Required] public Guid     FacilityId      { get; set; }
        [Required, Range(1, double.MaxValue)] public decimal CashCoverAmount { get; set; }
        public decimal?  CashCoverPct  { get; set; }
        [Required] public DateTime MaturityDate    { get; set; }
        public string?  HoldingAccount { get; set; }
        public string?  HoldingBank    { get; set; }
        public string?  Notes          { get; set; }
    }

    // ─────────────────────────────────────────────
    // BOND CLAIM
    // ─────────────────────────────────────────────
    public class BondClaimRequest
    {
        [Required] public Guid    FacilityId     { get; set; }
        [Required] public string  Obligee        { get; set; } = string.Empty;
        public decimal?  DefaultingBid   { get; set; }
        public decimal?  NextLowestBid   { get; set; }
        public decimal   ClaimAmount     { get; set; }
        public DateTime? ClaimDate       { get; set; }
        public string?   ClaimReference  { get; set; }
        public string?   Notes           { get; set; }
    }

    // ─────────────────────────────────────────────
    // ADVANCE PAYMENT
    // ─────────────────────────────────────────────
    public class CreateAdvancePaymentRequest
    {
        [Required] public Guid    ClientId         { get; set; }
        [Required] public string  ContractRef      { get; set; } = string.Empty;
        [Required] public string  Beneficiary      { get; set; } = string.Empty;
        [Required, Range(1, double.MaxValue)] public decimal BondAmount { get; set; }
        [Required] public double  BankRate         { get; set; }
        [Required] public int     TenorDays        { get; set; }
        public decimal BankCharges     { get; set; }
        public decimal Commission      { get; set; }
        public decimal Vat             { get; set; }
        public decimal TotalFee        { get; set; }
        public string? IssuingBank     { get; set; }
        public DateTime? ContractStartDate { get; set; }
        public DateTime? ContractEndDate   { get; set; }
        public string?  PaymentMode    { get; set; }
        public string?  RoId           { get; set; }
        public string?  RoName         { get; set; }
        public string?  Notes          { get; set; }
    }

    // ─────────────────────────────────────────────
    // LEDGER
    // ─────────────────────────────────────────────
    public class LedgerEntryRequest
    {
        public DateTime? LedgerDate   { get; set; }
        [Required] public string EntryType   { get; set; } = "Receipt";
        [Required] public string Reference   { get; set; } = string.Empty;
        public Guid?   FacilityId    { get; set; }
        public Guid?   ClientId      { get; set; }
        public string? ClientName    { get; set; }
        [Required] public string Description  { get; set; } = string.Empty;
        [Required, Range(0.01, double.MaxValue)] public decimal Amount { get; set; }
        public decimal BankCharges   { get; set; }
        public decimal Commission    { get; set; }
        public decimal Vat           { get; set; }
        public bool    IsPaid        { get; set; }
        public string? PaymentMethod { get; set; }
    }

    // ─────────────────────────────────────────────
    // USER MANAGEMENT
    // ─────────────────────────────────────────────
    public class UserResponse
    {
        public Guid      Id         { get; set; }
        public string    FullName   { get; set; } = string.Empty;
        public string    Email      { get; set; } = string.Empty;
        public string?   Phone      { get; set; }
        public string    Role       { get; set; } = string.Empty;
        public string?   Department { get; set; }
        public bool      IsActive   { get; set; }
        public DateTime? LastLoginAt{ get; set; }
        public DateTime  CreatedAt  { get; set; }
    }

    public class CreateUserRequest
    {
        [Required] public string   FullName   { get; set; } = string.Empty;
        [Required, EmailAddress] public string Email { get; set; } = string.Empty;
        [Required] public string   Password   { get; set; } = string.Empty;
        public string?   Phone      { get; set; }
        public UserRole  Role       { get; set; } = UserRole.RelationshipManager;
        public string?   Department { get; set; }
    }

    public class UpdateUserRequest
    {
        [Required] public string   FullName   { get; set; } = string.Empty;
        public string?   Phone      { get; set; }
        public UserRole  Role       { get; set; }
        public string?   Department { get; set; }
        public bool      IsActive   { get; set; } = true;
    }

    public class ResetPasswordRequest
    {
        [Required, MinLength(8)] public string NewPassword { get; set; } = string.Empty;
    }

    // ─────────────────────────────────────────────
    // APPROVAL WORKFLOWS
    // ─────────────────────────────────────────────
    public class CreateWorkflowRequest
    {
        [Required] public string  Name        { get; set; } = string.Empty;
        public string?  Description { get; set; }
        public FacilityType? ProductType { get; set; }
        public decimal? MinAmount   { get; set; }
        public decimal? MaxAmount   { get; set; }
        public List<WorkflowStepRequest> Steps { get; set; } = new();
    }

    public class WorkflowStepRequest
    {
        public int      StepOrder      { get; set; }
        [Required] public string StepName { get; set; } = string.Empty;
        public UserRole? RequiredRole   { get; set; }
        public string?   RequiredUserId { get; set; }
        public bool      IsOptional     { get; set; }
        public int?      TimeoutHours   { get; set; }
    }

    public class ApprovalActionRequest
    {
        public string? Comment { get; set; }
    }
}
