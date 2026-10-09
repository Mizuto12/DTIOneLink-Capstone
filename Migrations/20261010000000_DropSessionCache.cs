using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DTIOneLink.Migrations
{
    // Reverses AddSessionCache: the SQL-backed session store it enabled
    // (Program.cs: AddDistributedSqlServerCache) forced Microsoft.Data.SqlClient
    // >= 6.1.1, which needs System.Configuration.ConfigurationManager 9.0.x —
    // a version the production host's Web Deploy could not actually get onto
    // the server (confirmed via a temporary diagnostics endpoint: three
    // redeploys, including one with Web Deploy retry flags, left the old
    // 5.x/6.0.9 DLLs in place), breaking every database connection including
    // login. Program.cs is back to AddDistributedMemoryCache(), so this table
    // is unused; drop it rather than leave dead weight behind.
    [Migration("20261010000000_DropSessionCache")]
    public partial class DropSessionCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
IF OBJECT_ID(N'dbo.SessionCache', N'U') IS NOT NULL
    DROP TABLE dbo.SessionCache;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
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
    }
}
