using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Services;

namespace OnwardsSwift.API.Controllers
{
    public class WorkflowController : AppController
    {
        private readonly WorkflowService _workflowService;
        private readonly IUserService    _userService;

        public WorkflowController(WorkflowService workflowService, IUserService userService)
        {
            _workflowService = workflowService;
            _userService     = userService;
        }

        public IActionResult Index() =>
            RedirectToAction(nameof(Configure), new { module = "BOND" });

        // ── Stage list ─────────────────────────────────────────

        public async Task<IActionResult> Configure(string module = "BOND")
        {
            try
            {
                var stages   = await _workflowService.GetStagesByModuleAsync(module);
                var allUsers = await _userService.GetAllActiveUsersAsync();

                ViewBag.ActiveModule = module;
                ViewBag.UserList     = allUsers;

                return View(stages);
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error loading workflow: " + ex.Message;
                return View(new List<WorkflowStageViewModel>());
            }
        }

        // ── Save stage (create or update) ─────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveStage(WorkflowStageViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Validation failed. Check all fields.";
                return RedirectToAction(nameof(Configure), new { module = model.ModuleType });
            }

            try
            {
                await _workflowService.SaveStageAsync(model);
                TempData["Success"] = $"Stage '{model.StageName}' saved.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error saving stage: " + ex.Message;
            }

            return RedirectToAction(nameof(Configure), new { module = model.ModuleType });
        }

        // ── Delete stage ───────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteStage(int id, string module)
        {
            try
            {
                await _workflowService.DeleteStageAsync(id);
                TempData["Success"] = "Stage removed.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error removing stage: " + ex.Message;
            }

            return RedirectToAction(nameof(Configure), new { module });
        }

        // ── Required document management ──────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> AddDocument(int stageId, string documentName, bool isRequired, string module)
        {
            if (string.IsNullOrWhiteSpace(documentName))
            {
                TempData["Error"] = "Document name is required.";
                return RedirectToAction(nameof(Configure), new { module });
            }

            try
            {
                await _workflowService.AddStageDocumentAsync(stageId, documentName.Trim(), isRequired);
                TempData["Success"] = $"Document '{documentName}' added.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error adding document: " + ex.Message;
            }

            return RedirectToAction(nameof(Configure), new { module });
        }

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> RemoveDocument(int docId, string module)
        {
            try
            {
                await _workflowService.RemoveStageDocumentAsync(docId);
                TempData["Success"] = "Document requirement removed.";
            }
            catch (Exception ex)
            {
                TempData["Error"] = "Error removing document: " + ex.Message;
            }

            return RedirectToAction(nameof(Configure), new { module });
        }
    }
}
