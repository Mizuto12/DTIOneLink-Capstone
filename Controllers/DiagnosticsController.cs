using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using DTIOneLink.Data;
using System.Diagnostics;

namespace DTIOneLink.Controllers
{
    // TEMPORARY — added to diagnose the 2026-10-09 production login outage
    // (every request touching Session throwing). Gated by a secret token so
    // it can't be browsed by anyone who doesn't have the link. Remove once
    // the outage is confirmed fixed.
    public class DiagnosticsController(IConfiguration configuration, AppDbContext db) : Controller
    {
        [HttpGet]
        public async Task<IActionResult> Status(string token)
        {
            // Read from config (Diagnostics:Token), never hardcoded — the
            // deploy workflow writes it into appsettings.Production.json from
            // a GitHub secret, so the actual value never enters git history.
            // Unconfigured (e.g. local dev) means this route is always 404.
            var expected = configuration["Diagnostics:Token"];
            if (string.IsNullOrEmpty(expected) || token != expected)
            {
                return NotFound();
            }

            var result = new Dictionary<string, object?>();

            try
            {
                var baseDir = AppContext.BaseDirectory;
                result["baseDirectory"] = baseDir;

                var allCopies = Directory.GetFiles(baseDir, "System.Configuration.ConfigurationManager.dll", SearchOption.AllDirectories);
                result["configurationManagerDllCopies"] = allCopies.Select(p => new
                {
                    path = p[baseDir.Length..],
                    fileVersion = FileVersionInfo.GetVersionInfo(p).FileVersion,
                    productVersion = FileVersionInfo.GetVersionInfo(p).ProductVersion
                }).ToArray();

                var sqlClientCopies = Directory.GetFiles(baseDir, "Microsoft.Data.SqlClient.dll", SearchOption.AllDirectories);
                result["sqlClientDllCopies"] = sqlClientCopies.Select(p => new
                {
                    path = p[baseDir.Length..],
                    fileVersion = FileVersionInfo.GetVersionInfo(p).FileVersion
                }).ToArray();
            }
            catch (Exception ex)
            {
                result["fileCheckError"] = ex.GetType().Name + ": " + ex.Message;
            }

            try
            {
                var connectionString = configuration.GetConnectionString("DefaultConnection");
                result["connectionStringConfigured"] = !string.IsNullOrWhiteSpace(connectionString);

                using var conn = new SqlConnection(connectionString);
                await conn.OpenAsync();
                result["dbReachable"] = true;

                foreach (var table in new[] { "SessionCache", "RecordMasterlists", "Records", "TimeLogs" })
                {
                    using var cmd = new SqlCommand($"SELECT CASE WHEN OBJECT_ID(N'dbo.{table}', N'U') IS NULL THEN 0 ELSE 1 END", conn);
                    result[$"table_{table}_exists"] = (int)(await cmd.ExecuteScalarAsync() ?? 0) == 1;
                }
            }
            catch (Exception ex)
            {
                result["dbReachable"] = false;
                result["dbError"] = ex.GetType().Name + ": " + ex.Message;
            }

            try
            {
                result["pendingMigrations"] = (await db.Database.GetPendingMigrationsAsync()).ToArray();
                result["appliedMigrations"] = (await db.Database.GetAppliedMigrationsAsync()).ToArray();
            }
            catch (Exception ex)
            {
                result["migrationCheckError"] = ex.GetType().Name + ": " + ex.Message;
            }

            try
            {
                var errorFile = Path.Combine(
                    (HttpContext.RequestServices.GetRequiredService<IWebHostEnvironment>()).ContentRootPath,
                    "App_Data", "database-update-error.txt");
                result["databaseUpdateErrorFileExists"] = System.IO.File.Exists(errorFile);
                if (System.IO.File.Exists(errorFile))
                {
                    var content = await System.IO.File.ReadAllTextAsync(errorFile);
                    result["databaseUpdateErrorFile"] = content.Length > 4000 ? content[..4000] + "... (truncated)" : content;
                }
            }
            catch (Exception ex)
            {
                result["errorFileReadError"] = ex.GetType().Name + ": " + ex.Message;
            }

            return Json(result);
        }
    }
}
