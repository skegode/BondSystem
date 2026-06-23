using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces; // Assuming IMenuService is defined here

namespace OnwardsSwift.Infrastructure.Components
{
    public class SidebarViewComponent : ViewComponent
    {
        private readonly IMenuService _menuService;
        private readonly Microsoft.Extensions.Logging.ILogger<SidebarViewComponent> _logger;

        public SidebarViewComponent(IMenuService menuService, Microsoft.Extensions.Logging.ILogger<SidebarViewComponent> logger)
        {
            _menuService = menuService;
            _logger = logger;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            try
            {
                var claimsIdentity = UserClaimsPrincipal.Identity as ClaimsIdentity;
                var userId = claimsIdentity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
                var menus = await _menuService.GetUserMenu(userId);
                return View(menus);
            }
            catch(Exception ex)
            {
                // Log the error and return an empty menu so the layout still renders
                _logger.LogWarning(ex, "Failed to load sidebar menu. Rendering empty sidebar.");
                return View(new List<OnwardsSwift.Core.DTOs.NavMenu>());
            }
        }
    }
}