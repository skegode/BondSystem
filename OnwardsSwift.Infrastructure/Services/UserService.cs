using Dapper;
using OnwardsSwift.Core.DTOs;
using OnwardsSwift.Core.Interfaces;
using OnwardsSwift.Infrastructure.Data; 

namespace OnwardsSwift.Infrastructure.Services
{
    public class UserService : IUserService
    {
        private readonly DapperContext _ctx;

        public UserService(DapperContext ctx)
        {
            _ctx = ctx;
        }

        public async Task<List<SystemUserDto>> GetAllActiveUsersAsync()
        {
            using var conn = _ctx.Create();
            const string sql = @"
                SELECT 
                    Id, 
                    FullName, 
                    Email, 
                    Phone, 
                    Role, 
                    IsActive, 
                    IsDeleted 
                FROM SystemUsers 
                WHERE IsActive = 1 AND IsDeleted = 0";

            var users = await conn.QueryAsync<SystemUserDto>(sql);
            return users.ToList();
        }

        public async Task<SystemUserDto> GetUserByIdAsync(string id)
        {
            using var conn = _ctx.Create();
            const string sql = @"
                SELECT 
                    Id, 
                    FullName, 
                    Email, 
                    Phone, 
                    Role, 
                    IsActive, 
                    IsDeleted 
                FROM SystemUsers 
                WHERE Id = @id";

            return await conn.QueryFirstOrDefaultAsync<SystemUserDto>(sql, new { id });
        }
    }
}