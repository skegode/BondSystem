using System;
using System.Collections.Generic;

namespace OnwardsSwift.Core.DTOs
{
    public class WorkflowStageViewModel
    {
        public int    Id            { get; set; }
        public string ModuleType    { get; set; } = string.Empty;
        public string StageName     { get; set; } = string.Empty;
        public int    SequenceOrder { get; set; }
        public bool   IsFinalStage  { get; set; }

        // Whether approvers at this stage can return the application
        public bool CanReturn         { get; set; }
        // SequenceOrder to return to; null = back to applicant (re-opens as draft)
        public int? ReturnToStepOrder { get; set; }

        // Selected user IDs posted from the multi-select / drag-drop
        public List<string> UserIds { get; set; } = new();

        // Populated on GET for display
        public List<UserSelectDto>            Approvers         { get; set; } = new();
        public List<WorkflowStageDocumentDto> RequiredDocuments { get; set; } = new();
    }

    public class UserSelectDto
    {
        public string Id { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Role { get; set; }
        public bool IsActive { get; set; }

        // Helper to show "Name (Role)" in dropdowns
        public string DisplayName => $"{FullName} ({Role})";
    }
}