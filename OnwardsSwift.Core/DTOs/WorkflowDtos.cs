namespace OnwardsSwift.Core.DTOs
{
    // ── Per-stage document requirement ────────────────────────────
    public class WorkflowStageDocumentDto
    {
        public int    Id           { get; set; }
        public int    StageId      { get; set; }
        public string DocumentName { get; set; } = string.Empty;
        public bool   IsRequired   { get; set; }
    }

    // ── Item shown in the pending-approvals queue ─────────────────
    public class ApprovalQueueItem
    {
        public int      InstanceId        { get; set; }
        public int      ReferenceId       { get; set; }
        public string   ModuleType        { get; set; } = string.Empty;
        public string   ReferenceLabel    { get; set; } = string.Empty; // TenderNumber / ChequeNumber
        public string   ClientName        { get; set; } = string.Empty;
        public decimal  Amount            { get; set; }
        public string   CurrentStageName  { get; set; } = string.Empty;
        public int      CurrentStageId    { get; set; }
        public DateTime SubmittedAt       { get; set; }
        public bool     HasMissingDocs    { get; set; }
    }

    // ── Full workflow state for the review page ───────────────────
    public class ApprovalInstanceDto
    {
        public int      Id                   { get; set; }
        public int      ReferenceId          { get; set; }
        public string   ModuleType           { get; set; } = string.Empty;
        public int?     CurrentStageId       { get; set; }
        public string   CurrentStageName     { get; set; } = string.Empty;
        public int      CurrentSequenceOrder { get; set; }
        public bool     IsFinalStage         { get; set; }
        public bool     CanReturn            { get; set; }
        public int?     ReturnToStepOrder    { get; set; }
        public string   Status               { get; set; } = string.Empty;

        public List<UserSelectDto>            CurrentStageApprovers { get; set; } = new();
        public List<WorkflowStageDocumentDto> RequiredDocuments     { get; set; } = new();
        public List<ApprovalDocumentDto>      UploadedDocuments     { get; set; } = new();
        public List<WorkflowActionHistoryDto> History               { get; set; } = new();

        // Derived helpers used in views
        public bool IsComplete => Status == "APPROVED";
        public bool IsRejected => Status == "REJECTED";
        public bool IsActive   => Status == "PENDING";

        public List<string> MissingRequiredDocNames =>
            RequiredDocuments
                .Where(rd => rd.IsRequired &&
                             !UploadedDocuments.Any(ud =>
                                 ud.DocumentName.Equals(rd.DocumentName, StringComparison.OrdinalIgnoreCase)))
                .Select(rd => rd.DocumentName)
                .ToList();

        public bool HasMissingRequiredDocs => MissingRequiredDocNames.Any();
    }

    // ── Document uploaded during approval ─────────────────────────
    public class ApprovalDocumentDto
    {
        public int      Id             { get; set; }
        public string   DocumentName   { get; set; } = string.Empty;
        public string   FilePath       { get; set; } = string.Empty;
        public string   UploadedByName { get; set; } = string.Empty;
        public DateTime UploadedAt     { get; set; }
    }

    // ── One line in the approval history timeline ─────────────────
    public class WorkflowActionHistoryDto
    {
        public string   StageName    { get; set; } = string.Empty;
        public string   ActionType   { get; set; } = string.Empty;
        public string   ActionByName { get; set; } = string.Empty;
        public string?  Comment      { get; set; }
        public DateTime ActionAt     { get; set; }

        public string ActionLabel => ActionType switch
        {
            "APPROVED"     => "Approved",
            "REJECTED"     => "Rejected",
            "RETURNED"     => "Returned",
            "DOC_UPLOADED" => "Document Uploaded",
            _              => ActionType
        };

        public string ActionBadgeClass => ActionType switch
        {
            "APPROVED"     => "success",
            "REJECTED"     => "danger",
            "RETURNED"     => "warning",
            _              => "secondary"
        };
    }

    // ── Posted from the approve / reject / return form ────────────
    public class WorkflowActionRequest
    {
        public int     InstanceId { get; set; }
        public int     BondId     { get; set; }    // 0 when acting on a cheque
        public int     ChequeId   { get; set; }    // 0 when acting on a bond
        public string  ActionType { get; set; } = string.Empty; // APPROVED | REJECTED | RETURNED
        public string? Comment    { get; set; }
    }

    // ── Service return value from ProcessActionAsync ──────────────
    public class WorkflowActionResult
    {
        public bool    Success       { get; set; }
        public bool    IsComplete    { get; set; }  // workflow finished — approved
        public bool    IsRejected    { get; set; }  // workflow finished — rejected
        public bool    Advanced      { get; set; }  // moved to next / previous stage
        public string? NextStageName { get; set; }
        public string? Message       { get; set; }
        public int?    ReferenceId   { get; set; }
        public string? ModuleType    { get; set; }
    }

    // ── Review page view-model (bond details + workflow state) ────
    public class BondReviewViewModel
    {
        public BidBondResponse      Bond             { get; set; } = new();
        public ApprovalInstanceDto? Workflow         { get; set; }
        public bool                 IsCurrentApprover{ get; set; }
        public List<WorkflowStageViewModel> AllStages { get; set; } = new();
    }
}
