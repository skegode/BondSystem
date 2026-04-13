using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Dapper; 
using OnwardsSwift.Core.DTOs; 
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data; 

namespace OnwardsSwift.Infrastructure.Services
{
    public class MenuService : IMenuService
    {
        private readonly DapperContext _ctx;

        public MenuService(DapperContext ctx)
        {
            _ctx = ctx;
        }

        public async Task<List<NavMenu>> GetUserMenu(string userId)
        {
            using var conn = _ctx.Create();

            // Fetch all active menus from the DB
            const string sql = "SELECT * FROM SystemMenus WHERE IsActive = 1 ORDER BY SortOrder ASC";
            var allMenus = (await conn.QueryAsync<NavMenu>(sql)).ToList();

            // 1. Filter: Show if AllowedUserIds is null/empty OR contains the specific userId
            // We use .Trim() to handle cases where there might be spaces in the comma-separated string
            var authorized = allMenus.Where(m =>
                string.IsNullOrEmpty(m.AllowedUserIds) ||
                (userId != null && m.AllowedUserIds.Split(',').Select(s => s.Trim()).Contains(userId))
            ).ToList();

            // 2. Build Hierarchy: Group submenus under their parent items
            var hierarchy = authorized
                .Where(m => m.ParentId == null)
                .Select(parent => new NavMenu
                {
                    Id = parent.Id,
                    ParentId = parent.ParentId,
                    Title = parent.Title,
                    Icon = parent.Icon,
                    Controller = parent.Controller,
                    Action = parent.Action,
                    AllowedUserIds = parent.AllowedUserIds,
                    SortOrder = parent.SortOrder,
                    // Find all authorized children for this parent
                    SubMenus = authorized
                        .Where(child => child.ParentId == parent.Id)
                        .OrderBy(child => child.SortOrder)
                        .ToList()
                })
                .OrderBy(m => m.SortOrder)
                .ToList();

            return hierarchy;
        }
    }
}