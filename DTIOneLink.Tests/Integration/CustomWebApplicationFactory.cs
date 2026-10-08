using DTIOneLink.Data;
using DTIOneLink.Services;
using DTIOneLink.Services.Email;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace DTIOneLink.Tests.Integration;

// Boots the REAL app (Program.cs, the real middleware pipeline: routing,
// session, antiforgery, rate limiting, the RequireLogin/RequirePermission
// filters) in-process, with substitutions so tests never touch a real
// database or send real email:
//
//   - AppDbContext is backed by an EF Core SQLite *in-memory* database, one
//     per factory instance, kept alive for the factory's lifetime via one
//     open SqliteConnection (SQLite's in-memory mode drops the database the
//     moment its last connection closes). SQLite is a real relational
//     provider, unlike EF's InMemory provider - that distinction matters
//     here because AccountController.SignIn/Logout and several services use
//     ExecuteUpdate/ExecuteUpdateAsync, which only relational providers can
//     translate. Every HTTP request within one test shares this database,
//     the same way the real app shares one database across requests.
//   - Program.cs's own startup migration (targets SQL Server specifically)
//     is skipped for the "Testing" environment; this factory creates the
//     schema fresh from the current EF model instead (see CreateHost).
//   - The raw-ADO "DefaultConnection" string is pointed at an unreachable
//     address, so any controller that still talks to SQL Server directly
//     (RecordsController, parts of ReportsController/UserManagementController)
//     fails fast and safely instead of silently hitting a real database.
//
// Background hosted services that poll or email on a timer are removed -
// they have nothing to do against a fake database and would only add log
// noise and slow test shutdown.
public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public CustomWebApplicationFactory()
    {
        _connection.Open();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] =
                    "Server=127.0.0.1,1;Database=unreachable;Connect Timeout=1;TrustServerCertificate=True;",
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll(typeof(DbContextOptions<AppDbContext>));
            services.AddDbContext<AppDbContext>(options => options.UseSqlite(_connection));

            RemoveHostedService<RecurringTaskService>(services);
            RemoveHostedService<TaskReminderService>(services);
            RemoveHostedService<RecordRetentionReminderService>(services);
            RemoveHostedService<EmailDispatchService>(services);
            RemoveHostedService<LiveChangeBroadcaster>(services);
        });
    }

    protected override IHost CreateHost(IHostBuilder builder)
    {
        var host = base.CreateHost(builder);

        using var scope = host.Services.CreateScope();
        scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreated();

        return host;
    }

    private static void RemoveHostedService<T>(IServiceCollection services) where T : class
    {
        var descriptors = services
            .Where(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(T))
            .ToList();
        foreach (var descriptor in descriptors)
        {
            services.Remove(descriptor);
        }
    }

    // A fresh scoped AppDbContext over this factory's one shared SQLite
    // connection, for seeding rows before a test makes its HTTP calls.
    public AppDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;
        return new AppDbContext(options);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing)
        {
            _connection.Dispose();
        }
    }
}
