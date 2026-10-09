using DTIOneLink.Data;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Tests.TestDoubles;

// One fresh, isolated in-memory AppDbContext per call (unique database name
// per call so tests never see each other's seeded data, even when xUnit
// runs them in parallel).
public static class InMemoryDbContextFactory
{
    public static AppDbContext Create()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning))
            .Options;

        return new AppDbContext(options);
    }
}
