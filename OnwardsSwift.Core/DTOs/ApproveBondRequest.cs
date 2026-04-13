using System.ComponentModel.DataAnnotations;

namespace OnwardsSwift.Core.DTOs
{
    public class ApproveBondRequest
    {
        [Required]
        public int BondId { get; set; }

        /// <summary>
        /// True = Approved (Status 1), False = Rejected (Status 2)
        /// </summary>
        [Required]
        public bool IsApproved { get; set; }

        /// <summary>
        /// Maps to [StatusNotes] in the Bonds table. 
        /// Required especially if the bond is being rejected.
        /// </summary>
        [MaxLength(1000)]
        public string? Remarks { get; set; }

        /// <summary>
        /// Optional: If the manager wants to assign an internal tracking 
        /// or bank reference number during the approval stage.
        /// </summary>
        public string? InternalReference { get; set; }
    }
}