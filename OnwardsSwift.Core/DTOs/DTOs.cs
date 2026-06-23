using OnwardsSwift.Core.Enums;
using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
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
        [Required, Compare("NewPassword")] public string ConfirmPassword { get; set; } = string.Empty;
    }

    public class EmailSendResult
    {
        public bool Accepted { get; set; }
        public string? FailureReason { get; set; }
        public string? ProviderMessageId { get; set; }
        public int Attempts { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.UtcNow;
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
        public string? Email { get; set; }

        [Required, Phone]
        public string Phone { get; set; } = string.Empty;

        public string? PhoneAlt { get; set; }

        public ClientType ClientType { get; set; }

        // --- Location ---
        public string PhysicalAddress { get; set; } = string.Empty;

        public string PostalAddress { get; set; } = string.Empty;

        // --- Corporate Specific Fields ---
        public string BusinessRegNumber { get; set; } = string.Empty;

        public bool IprsVerified { get; set; }
        public string? IprsReference { get; set; }

        // --- KYC File Uploads (Not mapped to DB directly) ---
        public IFormFile? IdFrontFile { get; set; }
        public IFormFile? IdBackFile { get; set; }
        public IFormFile? PassportPhotoFile { get; set; }
        public IFormFile? RegCertFile { get; set; }

        // On edit, allow explicit removal of an existing document path.
        public bool RemoveIdFrontFile { get; set; }
        public bool RemoveIdBackFile { get; set; }
        public bool RemovePassportPhotoFile { get; set; }
        public bool RemoveRegCertFile { get; set; }
    }

    public class ClientResponse
    {
        public int     Id              { get; set; }
        public string   CompanyName     { get; set; } = string.Empty;
        public string   KraPin          { get; set; } = string.Empty;
        public string   ContactPerson   { get; set; } = string.Empty;
        public string   Email           { get; set; } = string.Empty;
        public string   Phone           { get; set; } = string.Empty;
        public string?  PhoneAlt        { get; set; }
        public string   ClientType      { get; set; } = string.Empty;
        public string   PhysicalAddress { get; set; } = string.Empty;
        public string?  PostalAddress   { get; set; }
        public string?  BusinessRegNumber { get; set; }
        public string?  IdNumber        { get; set; }
        public int?     Gender          { get; set; }
        public int?     Category        { get; set; }
        public string?  KycIdFrontPath  { get; set; }
        public string?  KycIdBackPath   { get; set; }
        public string?  KycPassportPhotoPath { get; set; }
        public string?  KycRegCertPath  { get; set; }
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

    public class BondRequest
    {
        public int? Id { get; set; }

        public string PrincipalName { get; set; } = string.Empty;
        public string PrincipalEmail { get; set; } = string.Empty;
        public string PrincipalPhone { get; set; } = string.Empty;
        public string PrincipalAddress { get; set; } = string.Empty;

        public string BeneficiaryName { get; set; } = string.Empty;
        public string BeneficiaryAddress { get; set; } = string.Empty;

        public string GuaranteeAmount { get; set; } = string.Empty;
        public string GuaranteeAmountWords { get; set; } = string.Empty;

        public List<BondType> BondTypes { get; set; } = new();
        public string OtherBondType { get; set; } = string.Empty;

        public string TenderReference { get; set; } = string.Empty;

        public DateTime? EffectiveDate { get; set; }
        public DateTime? ExpiryDate { get; set; }

        public string Signatory1Name { get; set; } = string.Empty;
        public string Signatory1SignaturePath { get; set; } = string.Empty;
        public string Signatory2Name { get; set; } = string.Empty;
        public string Signatory2SignaturePath { get; set; } = string.Empty;

        public List<Attachment> Attachments { get; set; } = new();
        public string Status { get; set; } = string.Empty;
        public string? StatusNote { get; set; }
    }

    public class BondIndemnity
    {
        public int? Id { get; set; }
        public int RequestId { get; set; }
        public DateTime IndemnityDate { get; set; }
        public string AuthorizedSignatoryName { get; set; } = string.Empty;
        public string AuthorizedSignatorySignaturePath { get; set; } = string.Empty;
        public string CompanySealPath { get; set; } = string.Empty;
        public string IndemnityText { get; set; } = string.Empty;
    }

    public class Attachment
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
    }

    public class BondRequestStatusUpdate
    {
        [Required] public string Status { get; set; } = string.Empty;
        public string? Note { get; set; }
    }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BondType
    {
        BidTender,
        Performance,
        AdvancePaymentGuarantee,
        Retention,
        Other
    }

    // View model for Onboarding wizard server view
    public class OnboardingWizardViewModel
    {
        public CreateClientRequest NewClient { get; set; } = new();
        public PagedResult<ClientResponse> ExistingClients { get; set; } = new();
        public ChequeEncashmentViewModel ChequeEncashment { get; set; } = new();
        public OfficialUseViewModel OfficialUse { get; set; } = new();
    }

    // View model for Cheque Encashment form (Step 2)
    public class ChequeEncashmentViewModel
    {
        public int? Id { get; set; }
        public int? ClientId { get; set; }
        public string ApplicantName { get; set; } = string.Empty;
        public string IdNumber { get; set; } = string.Empty;
        public string PostalAddress { get; set; } = string.Empty;
        public string Phone { get; set; } = string.Empty;

        // Applicant category drives the disbursement method: Individual -> M-Pesa, Company -> Bank
        public string Category { get; set; } = "Individual";
        public string PaymentMethod { get; set; } = "MPESA";
        public string? DisburseBank { get; set; }
        public string? DisburseAccount { get; set; }

        // Structured cheques list for model binding
        public List<ChequeItem> Cheques { get; set; } = new();

        // File uploads for attachments (multiple)
        public IFormFile[]? Attachments { get; set; }
        public List<AttachmentItem> ExistingAttachments { get; set; } = new();

        public string Purpose { get; set; } = string.Empty;
        public bool TermsAccepted { get; set; }
        
        // Declaration fields
        public string DeclarantName { get; set; } = string.Empty;
        public string DeclarantRole { get; set; } = string.Empty;
        public string DeclarantDate { get; set; } = string.Empty;
    }

    public class ChequeItem
    {
        public string? Number { get; set; }
        public decimal? Amount { get; set; }
        public string? Dated { get; set; }
        public string? Drawer { get; set; }
        public string? Bank { get; set; }
        public string? Branch { get; set; }
        public string? Payee { get; set; }
    }

    // View model for Official Use (Step 3)
    public class OfficialUseViewModel
    {
        public int? RequestId { get; set; }

        // Checked by
        public string CheckedBy { get; set; } = string.Empty;
        public string CheckedSignature { get; set; } = string.Empty;
        public IFormFile? CheckedSignatureFile { get; set; }
        public string CheckedDate { get; set; } = string.Empty;

        // Drawer's verification
        public string ConfirmedWith { get; set; } = string.Empty;
        public string Designation { get; set; } = string.Empty;
        public string BuildingStreet { get; set; } = string.Empty;
        public string DrawerStatus { get; set; } = string.Empty;
        public string ReasonForPayment { get; set; } = string.Empty;

        // Account confirmation
        public string AccountConfirmedBy { get; set; } = string.Empty;
        public string AccountStatus { get; set; } = string.Empty;

        // Approval process
        public string? HeadOfTradeFinance { get; set; }
        public string? HeadOfTradeSignature { get; set; }
        public IFormFile? HeadOfTradeSignatureFile { get; set; }
        public string? HeadOfTradeDate { get; set; }

        public string? InChargeFinance { get; set; }
        public string? InChargeFinanceSignature { get; set; }
        public IFormFile? InChargeFinanceSignatureFile { get; set; }
        public string? InChargeFinanceDate { get; set; }

        public string? CEO { get; set; }
        public string? CEOSignature { get; set; }
        public IFormFile? CEOSignatureFile { get; set; }
        public string? CEODate { get; set; }

        // Payment process
        public string? PaidByName { get; set; }
        public string? PaidBySignature { get; set; }
        public IFormFile? PaidBySignatureFile { get; set; }
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
        public decimal   CommissionPercent { get; set; }
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
        [Range(0, 100)] public decimal CommissionPercent { get; set; }
    }

    public class UpdateUserRequest
    {
        [Required] public string   FullName   { get; set; } = string.Empty;
        public string?   Phone      { get; set; }
        public UserRole  Role       { get; set; }
        public string?   Department { get; set; }
        [Range(0, 100)] public decimal CommissionPercent { get; set; }
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
