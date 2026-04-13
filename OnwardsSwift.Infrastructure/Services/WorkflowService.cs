using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper; 
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Infrastructure.Data; 

namespace OnwardsSwift.Infrastructure.Services
{
    public class WorkflowService
    {
        private readonly DapperContext _ctx;

        public WorkflowService(DapperContext ctx)
        {
            _ctx = ctx;
        }

        public async Task<List<WorkflowStageViewModel>> GetStagesByModuleAsync(string moduleType)
        {
            using var conn = _ctx.Create();
            const string sql = @"
        SELECT 
            s.*, 
            u.Id,       -- Changed from 'u.Id as UserId' to match DTO property 'Id'
            u.FullName, 
            u.Role      -- Added this so it shows in the UI badges
        FROM WorkflowStages s
        LEFT JOIN WorkflowApprovers wa ON s.Id = wa.StageId
        LEFT JOIN SystemUsers u ON wa.UserId = u.Id
        WHERE s.ModuleType = @moduleType
        ORDER BY s.SequenceOrder";

            var stageDictionary = new Dictionary<int, WorkflowStageViewModel>();

            await conn.QueryAsync<WorkflowStageViewModel, UserSelectDto, WorkflowStageViewModel>(
                sql,
                (stage, user) =>
                {
                    if (!stageDictionary.TryGetValue(stage.Id, out var stageEntry))
                    {
                        stageEntry = stage;
                        stageEntry.Approvers = new List<UserSelectDto>();
                        stageDictionary.Add(stageEntry.Id, stageEntry);
                    }

                    // If a stage has no approvers, 'user' will be an object with null properties
                    if (user != null && !string.IsNullOrEmpty(user.Id))
                    {
                        stageEntry.Approvers.Add(user);
                    }
                    return stageEntry;
                },
                new { moduleType },
                splitOn: "Id" // Split where the User columns start
            );

            return stageDictionary.Values.OrderBy(x => x.SequenceOrder).ToList();
        }

        public async Task<bool> SaveStageAsync(WorkflowStageViewModel model)
        {
            using var conn = _ctx.Create();
            conn.Open();
            using var trans = conn.BeginTransaction();

            try
            {
                if (model.Id == 0) // New Stage
                {
                    const string stageSql = @"INSERT INTO WorkflowStages (ModuleType, StageName, SequenceOrder, IsFinalStage) 
                                         VALUES (@ModuleType, @StageName, @SequenceOrder, @IsFinalStage);
                                         SELECT CAST(SCOPE_IDENTITY() as int);";
                    model.Id = await conn.ExecuteScalarAsync<int>(stageSql, model, trans);
                }
                else // Update Existing
                {
                    const string stageSql = @"UPDATE WorkflowStages SET StageName = @StageName, 
                                         SequenceOrder = @SequenceOrder, IsFinalStage = @IsFinalStage 
                                         WHERE Id = @Id";
                    await conn.ExecuteAsync(stageSql, model, trans);

                    // Clear existing approvers to refresh them
                    await conn.ExecuteAsync("DELETE FROM WorkflowApprovers WHERE StageId = @Id", new { Id = model.Id }, trans);
                }

                // Insert new Approver assignments
                if (model.UserIds != null && model.UserIds.Any())
                {
                    const string approverSql = "INSERT INTO WorkflowApprovers (StageId, UserId) VALUES (@StageId, @UserId)";
                    var approvers = model.UserIds.Select(uid => new { StageId = model.Id, UserId = uid });
                    await conn.ExecuteAsync(approverSql, approvers, trans);
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

        public async Task StartWorkflowAsync(int referenceId, string moduleType, string initiatorId)
        {
            using var conn = _ctx.Create();

            // 1. Find the first stage (SequenceOrder = 1) for this module
            var firstStageId = await conn.ExecuteScalarAsync<int>(
                "SELECT TOP 1 Id FROM WorkflowStages WHERE ModuleType = @moduleType ORDER BY SequenceOrder ASC",
                new { moduleType });

            if (firstStageId == 0) throw new Exception("No workflow stages defined for this module.");

            // 2. Create the Instance
            const string sql = @"INSERT INTO ApprovalInstances (ReferenceId, ModuleType, CurrentStageId, Status, InitiatorId)
                                 VALUES (@referenceId, @moduleType, @firstStageId, 'PENDING', @initiatorId)";

            await conn.ExecuteAsync(sql, new { referenceId, moduleType, firstStageId, initiatorId });
        }
    }
}