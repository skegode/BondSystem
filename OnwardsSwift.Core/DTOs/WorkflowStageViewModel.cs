using System;
using System.Collections.Generic;

namespace OnwardsSwift.Core.DTOs
{
    public class WorkflowStageViewModel
    {
        public int Id { get; set; }
        public string ModuleType { get; set; }
        public string StageName { get; set; }
        public int SequenceOrder { get; set; }
        public bool IsFinalStage { get; set; }

        // To hold the selected User IDs from the multiselect dropdown for POSTing
        public List<string> UserIds { get; set; } = new();

        // Updated to use the more detailed DTO for the table display
        public List<UserSelectDto> Approvers { get; set; } = new();
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