using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Infrastructure;
using DTIOneLink.Data;

#nullable disable

namespace DTIOneLink.Migrations
{
    // Backs ASP.NET Core sessions with SQL Server (Program.cs:
    // AddDistributedSqlServerCache) instead of the in-memory cache, so a
    // login session survives the host recycling the app's process — see the
    // comment there for why that was silently signing people out and
    // orphaning their Time Log "Still signed in" rows.
    //
    // Schema is the one Microsoft.Extensions.Caching.SqlServer requires
    // (same shape the `dotnet sql-cache create` tool generates); not in the
    // EF model, so this is a guarded raw-SQL migration like AddRecordMasterlists.
    [DbContext(typeof(AppDbContext))]
    [Migration("20261009120000_AddSessionCache")]
    /// <inheritdoc />
    public partial class AddSessionCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.SessionCache', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SessionCache (
        Id                         NVARCHAR(449)       NOT NULL,
        Value                      VARBINARY(MAX)      NOT NULL,
        ExpiresAtTime              DATETIMEOFFSET(7)   NOT NULL,
        SlidingExpirationInSeconds BIGINT               NULL,
        AbsoluteExpiration         DATETIMEOFFSET(7)    NULL,
        CONSTRAINT PK_SessionCache PRIMARY KEY CLUSTERED (Id ASC)
    );
    CREATE NONCLUSTERED INDEX IX_SessionCache_ExpiresAtTime ON dbo.SessionCache (ExpiresAtTime);
END");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.SessionCache', N'U') IS NOT NULL
    DROP TABLE dbo.SessionCache;");
        }
    }
}
