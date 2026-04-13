using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using OnwardsSwift.Core.DTOs;

namespace OnwardsSwift.Core.Interfaces
{
    public interface IUserService
    {


        Task<List<SystemUserDto>> GetAllActiveUsersAsync();
        Task<SystemUserDto> GetUserByIdAsync(string id);
    }
}
