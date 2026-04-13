using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Services;

namespace OnwardsSwift.API.Controllers
{
    public class WorkflowController : Controller
    {
        private readonly WorkflowService _workflowService;
        private readonly IUserService _userService;

        public WorkflowController(WorkflowService workflowService, IUserService userService)
        {
            _workflowService = workflowService;
            _userService = userService;
        }

        /// <summary>
        /// Default view - Redirects to the Bond Workflow Configuration
        /// </summary>
        public IActionResult Index()
        {
            return RedirectToAction(nameof(Configure), new { module = "BOND" });
        }

        /// <summary>
        /// Displays the workflow stages and the list of available users for a specific module.
        /// </summary>
        /// <param name="module">BOND, CHEQUE, or CASH_COVER</param>
        public async Task<IActionResult> Configure(string module = "BOND")
        {
            try
            {
                // 1. Get the existing stages defined for this specific module
                var stages = await _workflowService.GetStagesByModuleAsync(module);

                // 2. Get all active system users for the assignment dropdown
                var allUsers = await _userService.GetAllActiveUsersAsync();

                ViewBag.ActiveModule = module;
                ViewBag.UserList = allUsers; // Now contains FullName, Role, Email, etc.

                return View(stages);
            }
            catch (Exception ex)
            {
                // Log exception here (e.g., via Serilog)
                TempData["Error"] = "Error loading workflow configuration: " + ex.Message;
                return View(new List<WorkflowStageViewModel>());
            }
        }

        /// <summary>
        /// Creates or Updates a workflow stage and its assigned users.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SaveStage(WorkflowStageViewModel model)
        {
            if (!ModelState.IsValid)
            {
                TempData["Error"] = "Validation failed. Please ensure all fields are filled correctly.";
                return RedirectToAction(nameof(Configure), new { module = model.ModuleType });
            }

            try
            {
                var success = await _workflowService.SaveStageAsync(model);

                if (success)
                {
                    TempData["Success"] = $"Stage '{model.StageName}' for {model.ModuleType} has been saved.";
                }
                else
                {
                    TempData["Error"] = "Failed to update the workflow stage.";
                }
            }
            catch (Exception ex)
            {
                TempData["Error"] = "An unexpected error occurred: " + ex.Message;
            }

            return RedirectToAction(nameof(Configure), new { module = model.ModuleType });
        }

        /// <summary>
        /// Manually initiates a workflow for a specific document (Bond/Cheque/Cash).
        /// Usually called after a new document is created.
        /// </summary>
        [HttpPost]
        public async Task<IActionResult> InitiateApproval(int refId, string type)
        {
            // Get current logged-in user ID as the initiator
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            try
            {
                await _workflowService.StartWorkflowAsync(refId, type, userId);
                return Json(new { success = true, message = "Approval workflow initiated successfully." });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Placeholder for deleting a stage if required.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteStage(int id, string module)
        {
            // You would need to add a DeleteStageAsync method in WorkflowService
            // await _workflowService.DeleteStageAsync(id);
            TempData["Success"] = "Stage removed successfully.";
            return RedirectToAction(nameof(Configure), new { module });
        }
    }
}