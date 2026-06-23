using Dapper;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Infrastructure.Data;

namespace OnwardsSwift.Infrastructure.Services
{
    public class WorkflowService
    {
        private readonly DapperContext _ctx;

        public WorkflowService(DapperContext ctx) => _ctx = ctx;

        // ═══════════════════════════════════════════════════════
        // STAGE CONFIGURATION
        // ═══════════════════════════════════════════════════════

        public async Task<List<WorkflowStageViewModel>> GetStagesByModuleAsync(string moduleType)
        {
            using var conn = _ctx.Create();

            var stages = (await conn.QueryAsync<WorkflowStageViewModel>(
                "SELECT * FROM WorkflowStages WHERE ModuleType = @moduleType ORDER BY SequenceOrder",
                new { moduleType })).ToList();

            if (!stages.Any()) return stages;

            var approvers = await conn.QueryAsync<dynamic>(@"
                SELECT wa.StageId, u.Id, u.FullName, u.Role
                FROM WorkflowApprovers wa
                INNER JOIN SystemUsers u ON wa.UserId = CAST(u.Id AS NVARCHAR(50))
                INNER JOIN WorkflowStages s ON wa.StageId = s.Id
                WHERE s.ModuleType = @moduleType", new { moduleType });

            var docs = await conn.QueryAsync<WorkflowStageDocumentDto>(@"
                SELECT d.* FROM WorkflowStageDocuments d
                INNER JOIN WorkflowStages s ON d.StageId = s.Id
                WHERE s.ModuleType = @moduleType", new { moduleType });

            foreach (var stage in stages)
            {
                stage.Approvers = approvers
                    .Where(a => (int)a.StageId == stage.Id)
                    .Select(a => new UserSelectDto
                    {
                        Id       = ((int)a.Id).ToString(),
                        FullName = (string)a.FullName,
                        Role     = a.Role?.ToString() ?? ""
                    }).ToList();

                stage.RequiredDocuments = docs
                    .Where(d => d.StageId == stage.Id)
                    .ToList();
            }

            return stages;
        }

        public async Task<bool> SaveStageAsync(WorkflowStageViewModel model)
        {
            using var conn = _ctx.Create();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                if (model.Id == 0)
                {
                    model.Id = await conn.ExecuteScalarAsync<int>(@"
                        INSERT INTO WorkflowStages
                            (ModuleType, StageName, SequenceOrder, IsFinalStage, CanReturn, ReturnToStepOrder)
                        VALUES
                            (@ModuleType, @StageName, @SequenceOrder, @IsFinalStage, @CanReturn, @ReturnToStepOrder);
                        SELECT CAST(SCOPE_IDENTITY() AS INT);", model, trans);
                }
                else
                {
                    await conn.ExecuteAsync(@"
                        UPDATE WorkflowStages
                        SET StageName = @StageName, SequenceOrder = @SequenceOrder,
                            IsFinalStage = @IsFinalStage, CanReturn = @CanReturn,
                            ReturnToStepOrder = @ReturnToStepOrder
                        WHERE Id = @Id", model, trans);

                    await conn.ExecuteAsync(
                        "DELETE FROM WorkflowApprovers WHERE StageId = @Id",
                        new { model.Id }, trans);
                }

                if (model.UserIds?.Any() == true)
                {
                    var rows = model.UserIds.Select(uid => new { StageId = model.Id, UserId = uid });
                    await conn.ExecuteAsync(
                        "INSERT INTO WorkflowApprovers (StageId, UserId) VALUES (@StageId, @UserId)",
                        rows, trans);
                }

                trans.Commit();
                return true;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        public async Task DeleteStageAsync(int id)
        {
            using var conn = _ctx.Create();
            await conn.ExecuteAsync("DELETE FROM WorkflowStages WHERE Id = @id", new { id });
        }

        public async Task AddStageDocumentAsync(int stageId, string documentName, bool isRequired)
        {
            using var conn = _ctx.Create();
            await conn.ExecuteAsync(@"
                INSERT INTO WorkflowStageDocuments (StageId, DocumentName, IsRequired)
                VALUES (@stageId, @documentName, @isRequired)",
                new { stageId, documentName, isRequired });
        }

        public async Task RemoveStageDocumentAsync(int docId)
        {
            using var conn = _ctx.Create();
            await conn.ExecuteAsync("DELETE FROM WorkflowStageDocuments WHERE Id = @docId", new { docId });
        }

        // ═══════════════════════════════════════════════════════
        // WORKFLOW INSTANCE MANAGEMENT
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Called when a Bond or Cheque is submitted.
        /// Creates a FacilityApprovalInstance at the first stage.
        /// Silently does nothing if no stages are configured.
        /// </summary>
        public async Task StartWorkflowAsync(int referenceId, string moduleType, int initiatorId)
        {
            using var conn = _ctx.Create();

            var exists = await conn.ExecuteScalarAsync<int>(
                "SELECT COUNT(1) FROM FacilityApprovalInstances WHERE ReferenceId = @referenceId AND ModuleType = @moduleType",
                new { referenceId, moduleType });

            if (exists > 0)
                return;

            var firstStage = await conn.QueryFirstOrDefaultAsync<dynamic>(
                "SELECT TOP 1 Id FROM WorkflowStages WHERE ModuleType = @moduleType ORDER BY SequenceOrder ASC",
                new { moduleType });

            await conn.ExecuteAsync(@"
                INSERT INTO FacilityApprovalInstances (ReferenceId, ModuleType, CurrentStageId, Status)
                VALUES (@referenceId, @moduleType, @stageId, 'PENDING')",
                new { referenceId, moduleType, stageId = firstStage != null ? (int?)firstStage.Id : null });
        }

        /// <summary>
        /// Loads the full workflow state for a Bond or Cheque review page.
        /// Returns null if no workflow instance exists.
        /// </summary>
        public async Task<ApprovalInstanceDto?> GetInstanceAsync(int referenceId, string moduleType)
        {
            using var conn = _ctx.Create();

            var inst = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                SELECT i.Id, i.ReferenceId, i.ModuleType, i.CurrentStageId, i.Status,
                       s.StageName, s.SequenceOrder, s.IsFinalStage, s.CanReturn, s.ReturnToStepOrder
                FROM FacilityApprovalInstances i
                LEFT JOIN WorkflowStages s ON i.CurrentStageId = s.Id
                WHERE i.ReferenceId = @referenceId AND i.ModuleType = @moduleType",
                new { referenceId, moduleType });

            if (inst == null) return null;

            int instanceId = (int)inst.Id;
            int? stageId   = inst.CurrentStageId != null ? (int?)inst.CurrentStageId : null;

            IEnumerable<UserSelectDto> approvers = Enumerable.Empty<UserSelectDto>();
            IEnumerable<WorkflowStageDocumentDto> requiredDocs = Enumerable.Empty<WorkflowStageDocumentDto>();

            if (stageId.HasValue)
            {
                approvers = await conn.QueryAsync<UserSelectDto>(@"
                    SELECT CAST(u.Id AS NVARCHAR(50)) AS Id, u.FullName, u.Role
                    FROM WorkflowApprovers wa
                    INNER JOIN SystemUsers u ON wa.UserId = CAST(u.Id AS NVARCHAR(50))
                    WHERE wa.StageId = @stageId", new { stageId });

                requiredDocs = await conn.QueryAsync<WorkflowStageDocumentDto>(
                    "SELECT * FROM WorkflowStageDocuments WHERE StageId = @stageId ORDER BY Id",
                    new { stageId });
            }

            var uploadedDocs = await conn.QueryAsync<ApprovalDocumentDto>(@"
                SELECT Id, DocumentName, FilePath, UploadedByName, UploadedAt
                FROM ApprovalDocuments
                WHERE ReferenceId = @referenceId AND ModuleType = @moduleType
                ORDER BY UploadedAt DESC",
                new { referenceId, moduleType });

            var history = await conn.QueryAsync<WorkflowActionHistoryDto>(@"
                SELECT StageName, ActionType, ActionByName, Comment, ActionAt
                FROM FacilityApprovalActions
                WHERE InstanceId = @instanceId
                ORDER BY ActionAt ASC",
                new { instanceId });

            return new ApprovalInstanceDto
            {
                Id                   = instanceId,
                ReferenceId          = referenceId,
                ModuleType           = moduleType,
                CurrentStageId       = stageId,
                CurrentStageName     = inst.StageName?.ToString() ?? string.Empty,
                CurrentSequenceOrder = inst.SequenceOrder != null ? (int)inst.SequenceOrder : 0,
                IsFinalStage         = inst.IsFinalStage  != null && (bool)inst.IsFinalStage,
                CanReturn            = inst.CanReturn      != null && (bool)inst.CanReturn,
                ReturnToStepOrder    = inst.ReturnToStepOrder != null ? (int?)inst.ReturnToStepOrder : null,
                Status               = inst.Status?.ToString() ?? "PENDING",
                CurrentStageApprovers = approvers.ToList(),
                RequiredDocuments     = requiredDocs.ToList(),
                UploadedDocuments     = uploadedDocs.ToList(),
                History               = history.ToList()
            };
        }

        // ═══════════════════════════════════════════════════════
        // PENDING QUEUES
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Returns items at stages where the given user is assigned.
        /// </summary>
        public async Task<List<ApprovalQueueItem>> GetPendingForUserAsync(int userId, string moduleType)
        {
            using var conn = _ctx.Create();
            return moduleType == "BOND"
                ? (await conn.QueryAsync<ApprovalQueueItem>(@"
                    SELECT i.Id AS InstanceId, i.ReferenceId, i.ModuleType,
                           b.TenderNumber AS ReferenceLabel,
                           c.CompanyName  AS ClientName,
                           b.Amount,
                           s.StageName    AS CurrentStageName,
                           s.Id           AS CurrentStageId,
                           b.CreatedAt    AS SubmittedAt
                    FROM FacilityApprovalInstances i
                    INNER JOIN WorkflowStages s ON i.CurrentStageId = s.Id
                    INNER JOIN WorkflowApprovers wa ON wa.StageId = s.Id
                        AND wa.UserId = CAST(@userId AS NVARCHAR(50))
                    INNER JOIN Bonds b   ON b.Id = i.ReferenceId
                    INNER JOIN Clients c ON c.Id = b.ClientId
                    WHERE i.ModuleType = 'BOND' AND i.Status = 'PENDING'
                    ORDER BY i.InitiatedAt ASC", new { userId })).ToList()
                : (await conn.QueryAsync<ApprovalQueueItem>(@"
                    SELECT i.Id AS InstanceId, i.ReferenceId, i.ModuleType,
                           cd.ChequeNumber AS ReferenceLabel,
                           c.CompanyName   AS ClientName,
                           cd.ChequeAmount AS Amount,
                           s.StageName     AS CurrentStageName,
                           s.Id            AS CurrentStageId,
                           cd.CreatedAt    AS SubmittedAt
                    FROM FacilityApprovalInstances i
                    INNER JOIN WorkflowStages s ON i.CurrentStageId = s.Id
                    INNER JOIN WorkflowApprovers wa ON wa.StageId = s.Id
                        AND wa.UserId = CAST(@userId AS NVARCHAR(50))
                    INNER JOIN ChequeDiscounting cd ON cd.Id = i.ReferenceId
                    INNER JOIN Clients c ON c.Id = cd.ClientId
                    WHERE i.ModuleType = 'CHEQUE' AND i.Status = 'PENDING'
                    ORDER BY i.InitiatedAt ASC", new { userId })).ToList();
        }

        /// <summary>
        /// Admin view — returns all pending items regardless of approver assignment.
        /// </summary>
        public async Task<List<ApprovalQueueItem>> GetAllPendingAsync(string moduleType)
        {
            using var conn = _ctx.Create();
            return moduleType == "BOND"
                ? (await conn.QueryAsync<ApprovalQueueItem>(@"
                    SELECT * FROM (
                        SELECT i.Id AS InstanceId, i.ReferenceId, i.ModuleType,
                               b.TenderNumber AS ReferenceLabel,
                               c.CompanyName  AS ClientName,
                               b.Amount,
                               ISNULL(s.StageName, 'No Stage') AS CurrentStageName,
                               ISNULL(s.Id, 0)                 AS CurrentStageId,
                               b.CreatedAt    AS SubmittedAt
                        FROM FacilityApprovalInstances i
                        LEFT JOIN WorkflowStages s ON i.CurrentStageId = s.Id
                        INNER JOIN Bonds b   ON b.Id = i.ReferenceId
                        INNER JOIN Clients c ON c.Id = b.ClientId
                        WHERE i.ModuleType = 'BOND' AND i.Status = 'PENDING'

                        UNION ALL

                        SELECT 0 AS InstanceId,
                               b.Id AS ReferenceId,
                               'BOND' AS ModuleType,
                               b.TenderNumber AS ReferenceLabel,
                               c.CompanyName  AS ClientName,
                               b.Amount,
                               'Awaiting Workflow Setup' AS CurrentStageName,
                               0 AS CurrentStageId,
                               b.CreatedAt AS SubmittedAt
                        FROM Bonds b
                        INNER JOIN Clients c ON c.Id = b.ClientId
                        WHERE ISNULL(b.isApproved, 0) = 0
                          AND NOT EXISTS (
                              SELECT 1
                              FROM FacilityApprovalInstances i
                              WHERE i.ReferenceId = b.Id
                                AND i.ModuleType = 'BOND'
                          )
                    ) q
                    ORDER BY q.SubmittedAt ASC")).ToList()
                : (await conn.QueryAsync<ApprovalQueueItem>(@"
                    SELECT i.Id AS InstanceId, i.ReferenceId, i.ModuleType,
                           cd.ChequeNumber AS ReferenceLabel,
                           c.CompanyName   AS ClientName,
                           cd.ChequeAmount AS Amount,
                           ISNULL(s.StageName, 'No Stage') AS CurrentStageName,
                           ISNULL(s.Id, 0)                 AS CurrentStageId,
                           cd.CreatedAt    AS SubmittedAt
                    FROM FacilityApprovalInstances i
                    LEFT JOIN WorkflowStages s ON i.CurrentStageId = s.Id
                    INNER JOIN ChequeDiscounting cd ON cd.Id = i.ReferenceId
                    INNER JOIN Clients c ON c.Id = cd.ClientId
                    WHERE i.ModuleType = 'CHEQUE' AND i.Status = 'PENDING'
                    ORDER BY i.InitiatedAt ASC")).ToList();
        }

        // ═══════════════════════════════════════════════════════
        // ACTION PROCESSING
        // ═══════════════════════════════════════════════════════

        /// <summary>
        /// Processes APPROVED / REJECTED / RETURNED for a workflow instance.
        /// Returns a result that tells the controller what to do next
        /// (e.g. trigger financial posting on final approval of a bond).
        /// </summary>
        public async Task<WorkflowActionResult> ProcessActionAsync(
            int instanceId, int userId, string userName,
            string actionType, string? comment)
        {
            using var conn = _ctx.Create();
            conn.Open();
            using var trans = conn.BeginTransaction();
            try
            {
                var inst = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                    SELECT i.Id, i.ReferenceId, i.ModuleType, i.CurrentStageId,
                           s.SequenceOrder, s.IsFinalStage, s.CanReturn,
                           s.ReturnToStepOrder, s.StageName
                    FROM FacilityApprovalInstances i
                    LEFT JOIN WorkflowStages s ON i.CurrentStageId = s.Id
                    WHERE i.Id = @instanceId",
                    new { instanceId }, trans);

                if (inst == null)
                    return new WorkflowActionResult { Success = false, Message = "Approval instance not found." };

                string module       = (string)inst.ModuleType;
                int    refId        = (int)inst.ReferenceId;
                int    curStageId   = inst.CurrentStageId != null ? (int)inst.CurrentStageId : 0;
                int    curSeq       = inst.SequenceOrder  != null ? (int)inst.SequenceOrder  : 0;
                string stageName    = inst.StageName?.ToString() ?? "Unknown";
                bool   isFinal      = inst.IsFinalStage   != null && (bool)inst.IsFinalStage;
                bool   canReturn    = inst.CanReturn       != null && (bool)inst.CanReturn;
                int?   returnToSeq  = inst.ReturnToStepOrder != null ? (int?)inst.ReturnToStepOrder : null;

                // Record the action in history
                await conn.ExecuteAsync(@"
                    INSERT INTO FacilityApprovalActions
                        (InstanceId, StageId, StageName, ActionType, ActionById, ActionByName, Comment)
                    VALUES
                        (@instanceId, @curStageId, @stageName, @actionType, @userId, @userName, @comment)",
                    new { instanceId, curStageId, stageName, actionType, userId, userName, comment }, trans);

                WorkflowActionResult result;

                switch (actionType)
                {
                    case "REJECTED":
                        await conn.ExecuteAsync(
                            "UPDATE FacilityApprovalInstances SET Status='REJECTED', CompletedAt=GETDATE() WHERE Id=@instanceId",
                            new { instanceId }, trans);

                        if (module == "BOND")
                            await conn.ExecuteAsync(
                                "UPDATE Bonds SET isApproved=2, StatusNotes=@c WHERE Id=@id",
                                new { c = comment, id = refId }, trans);
                        else
                            await conn.ExecuteAsync(
                                "UPDATE ChequeDiscounting SET Status='Rejected' WHERE Id=@id",
                                new { id = refId }, trans);

                        result = new WorkflowActionResult { Success = true, IsRejected = true, ReferenceId = refId, ModuleType = module };
                        break;

                    case "RETURNED":
                        if (!canReturn)
                            return new WorkflowActionResult { Success = false, Message = "Return not permitted for this stage." };

                        if (returnToSeq == null || returnToSeq == 0)
                        {
                            // Back to applicant — clear current stage, mark as RETURNED
                            await conn.ExecuteAsync(
                                "UPDATE FacilityApprovalInstances SET CurrentStageId=NULL, Status='RETURNED' WHERE Id=@instanceId",
                                new { instanceId }, trans);

                            if (module == "BOND")
                                await conn.ExecuteAsync(
                                    "UPDATE Bonds SET isApproved=NULL, StatusNotes=@c WHERE Id=@id",
                                    new { c = comment, id = refId }, trans);
                            else
                                await conn.ExecuteAsync(
                                    "UPDATE ChequeDiscounting SET Status='Returned' WHERE Id=@id",
                                    new { id = refId }, trans);

                            result = new WorkflowActionResult
                            {
                                Success       = true,
                                Advanced      = true,
                                NextStageName = "Applicant",
                                ReferenceId   = refId,
                                ModuleType    = module
                            };
                        }
                        else
                        {
                            var target = await conn.QueryFirstOrDefaultAsync<dynamic>(
                                "SELECT Id, StageName FROM WorkflowStages WHERE ModuleType=@module AND SequenceOrder=@seq",
                                new { module, seq = returnToSeq }, trans);

                            if (target == null)
                                return new WorkflowActionResult { Success = false, Message = "Target return stage not found." };

                            await conn.ExecuteAsync(
                                "UPDATE FacilityApprovalInstances SET CurrentStageId=@sid, Status='PENDING' WHERE Id=@instanceId",
                                new { sid = (int)target.Id, instanceId }, trans);

                            result = new WorkflowActionResult
                            {
                                Success       = true,
                                Advanced      = true,
                                NextStageName = (string)target.StageName,
                                ReferenceId   = refId,
                                ModuleType    = module
                            };
                        }
                        break;

                    default: // APPROVED
                        if (isFinal)
                        {
                            await conn.ExecuteAsync(
                                "UPDATE FacilityApprovalInstances SET Status='APPROVED', CompletedAt=GETDATE() WHERE Id=@instanceId",
                                new { instanceId }, trans);

                            // For cheques, mark approved here.
                            // For bonds, the caller (ApprovalsController.FinalApproveBond) handles financial posting.
                            if (module == "CHEQUE")
                                await conn.ExecuteAsync(
                                    "UPDATE ChequeDiscounting SET Status='Approved' WHERE Id=@id",
                                    new { id = refId }, trans);

                            result = new WorkflowActionResult { Success = true, IsComplete = true, ReferenceId = refId, ModuleType = module };
                        }
                        else
                        {
                            var next = await conn.QueryFirstOrDefaultAsync<dynamic>(@"
                                SELECT Id, StageName FROM WorkflowStages
                                WHERE ModuleType = @module AND SequenceOrder > @curSeq
                                ORDER BY SequenceOrder ASC",
                                new { module, curSeq }, trans);

                            if (next == null)
                            {
                                // No next stage — treat as final
                                await conn.ExecuteAsync(
                                    "UPDATE FacilityApprovalInstances SET Status='APPROVED', CompletedAt=GETDATE() WHERE Id=@instanceId",
                                    new { instanceId }, trans);

                                if (module == "CHEQUE")
                                    await conn.ExecuteAsync(
                                        "UPDATE ChequeDiscounting SET Status='Approved' WHERE Id=@id",
                                        new { id = refId }, trans);

                                result = new WorkflowActionResult { Success = true, IsComplete = true, ReferenceId = refId, ModuleType = module };
                            }
                            else
                            {
                                await conn.ExecuteAsync(
                                    "UPDATE FacilityApprovalInstances SET CurrentStageId=@sid WHERE Id=@instanceId",
                                    new { sid = (int)next.Id, instanceId }, trans);

                                result = new WorkflowActionResult
                                {
                                    Success       = true,
                                    Advanced      = true,
                                    NextStageName = (string)next.StageName,
                                    ReferenceId   = refId,
                                    ModuleType    = module
                                };
                            }
                        }
                        break;
                }

                trans.Commit();
                return result;
            }
            catch
            {
                trans.Rollback();
                throw;
            }
        }

        // ═══════════════════════════════════════════════════════
        // DOCUMENT UPLOAD
        // ═══════════════════════════════════════════════════════

        public async Task RecordUploadedDocumentAsync(
            int referenceId, string moduleType,
            string documentName, string filePath,
            int uploadedById, string uploadedByName)
        {
            using var conn = _ctx.Create();
            await conn.ExecuteAsync(@"
                INSERT INTO ApprovalDocuments
                    (ReferenceId, ModuleType, DocumentName, FilePath, UploadedById, UploadedByName)
                VALUES
                    (@referenceId, @moduleType, @documentName, @filePath, @uploadedById, @uploadedByName)",
                new { referenceId, moduleType, documentName, filePath, uploadedById, uploadedByName });
        }

        // ═══════════════════════════════════════════════════════
        // HELPERS
        // ═══════════════════════════════════════════════════════

        public async Task<bool> IsUserApproverForCurrentStageAsync(int referenceId, string moduleType, int userId)
        {
            using var conn = _ctx.Create();
            var count = await conn.ExecuteScalarAsync<int>(@"
                SELECT COUNT(1)
                FROM FacilityApprovalInstances i
                INNER JOIN WorkflowApprovers wa ON wa.StageId = i.CurrentStageId
                WHERE i.ReferenceId = @referenceId AND i.ModuleType = @moduleType
                  AND wa.UserId = CAST(@userId AS NVARCHAR(50))",
                new { referenceId, moduleType, userId });
            return count > 0;
        }
    }
}
