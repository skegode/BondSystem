using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Configuration;

namespace OnwardsSwift.Infrastructure.Data
{
    /// <summary>
    /// Single connection factory for all Dapper queries.
    /// Dapper maps ONLY the columns named in each SELECT — extra DB columns are silently ignored.
    /// Register as Singleton in DI.
    /// </summary>
    public class DapperContext
    {
        private readonly string _connStr;

        public DapperContext(IConfiguration config)
        {
            _connStr = config.GetConnectionString("DefaultConnection")
                       ?? throw new InvalidOperationException("DefaultConnection missing from config.");
        }

        /// <summary>Returns a new (closed) SqlConnection — Dapper opens it automatically.</summary>
        public SqlConnection Create() => new(_connStr);

        /// <summary>Alias for Create() — backward compatibility.</summary>
        public SqlConnection CreateConnection() => new(_connStr);

        public async Task<bool> CanConnectAsync()
        {
            try { using var c = Create(); await c.OpenAsync(); return true; }
            catch { return false; }
        }
    }
}
