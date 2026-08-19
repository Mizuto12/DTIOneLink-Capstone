using Microsoft.Data.SqlClient;

namespace DTIOneLink.Services
{
    /// <summary>
    /// Inject this service into any controller to get a ready-to-use
    /// SqlConnection without ever hardcoding the connection string.
    ///
    /// Register in Program.cs:
    ///     builder.Services.AddSingleton&lt;DatabaseHelper&gt;();
    ///
    /// Inject into any controller:
    ///     public MyController(DatabaseHelper db) { _db = db; }
    ///
    /// Use:
    ///     using var conn = _db.GetConnection();
    ///     await conn.OpenAsync();
    /// </summary>
    public class DatabaseHelper
    {
        private readonly string _connectionString;

        public DatabaseHelper(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException(
                    "Connection string 'DefaultConnection' not found in appsettings.json.");
        }

        /// <summary>
        /// Returns a new (closed) SqlConnection using the configured
        /// connection string. Call OpenAsync() on it before use.
        /// </summary>
        public SqlConnection GetConnection()
        {
            return new SqlConnection(_connectionString);
        }
    }
}
