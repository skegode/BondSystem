using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using OnwardsSwift.API.MobileLocal.Configuration;

namespace OnwardsSwift.API.MobileLocal.Services;

public class LocalSqliteContext
{
    private readonly string _dbPath;

    public LocalSqliteContext(IWebHostEnvironment env, IOptions<LocalApiOptions> options)
    {
        var configuredPath = options.Value.SqlitePath;
        _dbPath = Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.Combine(env.ContentRootPath, configuredPath);
    }

    public string DatabasePath => _dbPath;

    public SqliteConnection CreateConnection()
    {
        var directory = Path.GetDirectoryName(_dbPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        return new SqliteConnection($"Data Source={_dbPath};Cache=Shared");
    }
}
