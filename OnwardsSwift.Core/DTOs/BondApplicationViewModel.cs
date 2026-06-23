using Microsoft.AspNetCore.Http;
using System.ComponentModel.DataAnnotations;

namespace OnwardsSwift.Core.DTOs
{
    public class BondApplicationViewModel
    {
        public int? Id { get; set; }

        [Display(Name = "Principal / Applicant Name")]
        public string ApplicantName { get; set; } = string.Empty;

        public string ApplicantAddress { get; set; } = string.Empty;
        public string ApplicantCode { get; set; } = string.Empty;
        public string ApplicantTown { get; set; } = string.Empty;

        [Display(Name = "Beneficiary / Procuring Entity")]
        public string Procuring { get; set; } = string.Empty;

        public string ProcAddress { get; set; } = string.Empty;
        public string ProcCode { get; set; } = string.Empty;
        public string ProcTown { get; set; } = string.Empty;

        public string GuaranteeFigures { get; set; } = string.Empty;
        public string GuaranteeWords { get; set; } = string.Empty;

        public bool TypeBid { get; set; }
        public bool TypePerformance { get; set; }
        public bool TypeAdvance { get; set; }
        public bool TypeRetention { get; set; }
        public string TypeOther { get; set; } = string.Empty;

        public string TenderRef { get; set; } = string.Empty;

        [DataType(DataType.Date)]
        public DateTime? GuaranteeFrom { get; set; }

        [DataType(DataType.Date)]
        public DateTime? GuaranteeTo { get; set; }

        public string SigName1 { get; set; } = string.Empty;
        public string SigSignature1 { get; set; } = string.Empty;
        public IFormFile? SigSignature1File { get; set; }
        public string SigName2 { get; set; } = string.Empty;
        public string SigSignature2 { get; set; } = string.Empty;
        public IFormFile? SigSignature2File { get; set; }

        // Step 2: Counter Guarantee / Indemnity
        public string IndemnityDateDay { get; set; } = string.Empty;
        public string IndemnityDateMonth { get; set; } = string.Empty;
        public string IndemnityDateYear { get; set; } = string.Empty;
        public string IndemnityName1 { get; set; } = string.Empty;
        public string IndemnitySignature1 { get; set; } = string.Empty;
        public IFormFile? IndemnitySignature1File { get; set; }
        public string IndemnityName2 { get; set; } = string.Empty;
        public string IndemnitySignature2 { get; set; } = string.Empty;
        public IFormFile? IndemnitySignature2File { get; set; }
        public string CompanySealStamp { get; set; } = string.Empty;

        public IFormFile[]? Attachments { get; set; }
        public List<string> ExistingAttachments { get; set; } = new();
    }
}
