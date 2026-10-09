using DTIOneLink.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTIOneLink.Tests;

// RecordRetentionReminder reads/writes Records with raw SqlConnection (not
// EF, no InMemory provider available), so its SQL-dependent logic (the
// due-soon/overdue date boundary, the recalculation pass) can't be exercised
// without a live SQL Server. Only its no-connection-string guard clause is
// pure C# and safe to test here without a live dependency.
public class RecordRetentionReminderTests
{
    [Fact]
    public async Task SendDueRemindersAsync_NoConnectionStringConfigured_ReturnsZeroWithoutTouchingTheDatabase()
    {
        var config = new ConfigurationBuilder().Build(); // no ConnectionStrings:DefaultConnection
        var db = DTIOneLink.Tests.TestDoubles.InMemoryDbContextFactory.Create();
        var notifications = new NotificationService(db, config);
        var reminder = new RecordRetentionReminder(config, notifications, NullLogger<RecordRetentionReminder>.Instance);

        var count = await reminder.SendDueRemindersAsync(ownerUserId: null);

        Assert.Equal(0, count);
    }

    [Fact]
    public async Task SendDueRemindersAsync_BlankConnectionString_ReturnsZero()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = "   " })
            .Build();
        var db = DTIOneLink.Tests.TestDoubles.InMemoryDbContextFactory.Create();
        var notifications = new NotificationService(db, config);
        var reminder = new RecordRetentionReminder(config, notifications, NullLogger<RecordRetentionReminder>.Instance);

        var count = await reminder.SendDueRemindersAsync(ownerUserId: 1);

        Assert.Equal(0, count);
    }
}
