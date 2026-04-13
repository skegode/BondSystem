using System.ComponentModel.DataAnnotations;

namespace OnwardsSwift.Core.Enums
{
    public enum FacilityType
    {
        [Display(Name = "Bid Bond")]
        BidBond = 1,

        [Display(Name = "Performance Bond")]
        PerformanceBond = 2, 

        [Display(Name = "Advance Payment")]
        AdvancePayment = 3,
    }
    public enum InstitutionType
    {
        [Display(Name = "Commercial Bank")]

        Bank = 1,

        [Display(Name = "Insurance Company")]

        InsuranceCompany = 2
    }

    public enum CommissionType
    {
        Percentage = 1,      
        FixedAmount = 2       
    }

    public enum FacilityStatus
    {
        Draft = 0,
        Pending = 1,
        UnderReview = 2,
        Approved = 3,
        Disbursed = 4,
        Rejected = 5,
        Expired = 6,
        Settled = 7
    }

    public enum ClientType
    {
        Individual = 1,
        SME = 2,
        Corporate = 3,
        Government = 4
    }

    public enum UserRole
    {
        Admin = 1,
        RelationshipManager = 2,
        CreditOfficer = 3,
        Client = 4,
        Auditor = 5
    }

    public enum DocumentType
    {
        TenderDocument = 1,
        Invoice = 2,
        ChequeImage = 3,
        KRAPin = 4,
        BusinessCertificate = 5,
        NationalID = 6,
        BankStatement = 7,
        ContractAward = 8,
        LPO = 9
    }

    public enum NotificationType
    {
        ApplicationReceived = 1,
        UnderReview = 2,
        Approved = 3,
        Disbursed = 4,
        Rejected = 5,
        PaymentDue = 6,
        Settled = 7
    }
}
