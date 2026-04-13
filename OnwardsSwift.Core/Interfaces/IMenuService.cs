using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OnwardsSwift.Core.DTOs;

namespace OnwardsSwift.Core.Interfaces
{
    public interface IMenuService
    {
        Task<List<NavMenu>> GetUserMenu(string userId);
    }
}
