using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OnwardsSwift.Core.DTOs
{
    public class NavMenu
    {
        public int Id { get; set; }
        public int? ParentId { get; set; }
        public string Title { get; set; }
        public string Icon { get; set; }
        public string Controller { get; set; }
        public string Action { get; set; }
        public int SortOrder { get; set; }
        public bool IsActive { get; set; } = true;
        public string? AllowedUserIds { get; set; }

        // For the UI hierarchy
        public List<NavMenu> SubMenus { get; set; } = new();
    }
}
