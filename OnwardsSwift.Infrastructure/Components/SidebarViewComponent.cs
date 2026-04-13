using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces; // Assuming IMenuService is defined here

namespace OnwardsSwift.Infrastructure.Components
{
    public class SidebarViewComponent : ViewComponent
    {
        private readonly IMenuService _menuService;

        public SidebarViewComponent(IMenuService menuService)
        {
            _menuService = menuService;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
          
            var claimsIdentity = UserClaimsPrincipal.Identity as ClaimsIdentity;

            var userId = claimsIdentity?.FindFirst(ClaimTypes.NameIdentifier)?.Value;

            var menus = await _menuService.GetUserMenu(userId);

            return View(menus);
        }
    }
}