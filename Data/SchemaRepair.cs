using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Data
{
    // Fallback for when the startup migration fails on the hosted database
    // (e.g. its migration history doesn't match its tables). Adds whatever the
    // AddTaskAssignmentReassignedFrom and AddAccountSecurityAndEmail
    // migrations would have, matching them exactly. Every step checks first,
    // so it is safe to run on a database that already has some or all of it.
    public static class SchemaRepair
    {
        public static async Task EnsureLatestSchemaAsync(AppDbContext db)
        {
            // AddTaskAssignmentReassignedFrom
            await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH(N'dbo.TaskAssignments', N'ReassignedFromUserId') IS NULL
    ALTER TABLE dbo.TaskAssignments ADD ReassignedFromUserId int NULL;");

            await db.Database.ExecuteSqlRawAsync(@"
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_TaskAssignments_ReassignedFromUserId'
               AND object_id = OBJECT_ID(N'dbo.TaskAssignments'))
    CREATE INDEX IX_TaskAssignments_ReassignedFromUserId ON dbo.TaskAssignments (ReassignedFromUserId);
IF OBJECT_ID(N'dbo.FK_TaskAssignments_Users_ReassignedFromUserId', N'F') IS NULL
    ALTER TABLE dbo.TaskAssignments ADD CONSTRAINT FK_TaskAssignments_Users_ReassignedFromUserId
        FOREIGN KEY (ReassignedFromUserId) REFERENCES dbo.Users (Id);");

            // AddAccountSecurityAndEmail
            await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH(N'dbo.Users', N'EmailConfirmed') IS NULL
    ALTER TABLE dbo.Users ADD EmailConfirmed bit NOT NULL CONSTRAINT DF_Users_EmailConfirmed DEFAULT CAST(0 AS bit);
IF COL_LENGTH(N'dbo.Users', N'FailedLoginCount') IS NULL
    ALTER TABLE dbo.Users ADD FailedLoginCount int NOT NULL CONSTRAINT DF_Users_FailedLoginCount DEFAULT 0;
IF COL_LENGTH(N'dbo.Users', N'LockoutEndUtc') IS NULL
    ALTER TABLE dbo.Users ADD LockoutEndUtc datetime2 NULL;
IF COL_LENGTH(N'dbo.Users', N'MustChangePassword') IS NULL
    ALTER TABLE dbo.Users ADD MustChangePassword bit NOT NULL CONSTRAINT DF_Users_MustChangePassword DEFAULT CAST(0 AS bit);
IF COL_LENGTH(N'dbo.Users', N'SecurityStamp') IS NULL
    ALTER TABLE dbo.Users ADD SecurityStamp nvarchar(64) NOT NULL CONSTRAINT DF_Users_SecurityStamp DEFAULT N'';");

            await db.Database.ExecuteSqlRawAsync(@"
IF OBJECT_ID(N'dbo.EmailOutbox', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.EmailOutbox (
        Id               int IDENTITY(1,1) NOT NULL CONSTRAINT PK_EmailOutbox PRIMARY KEY,
        ToEmail          nvarchar(256) NOT NULL,
        ToName           nvarchar(200) NOT NULL,
        Subject          nvarchar(200) NOT NULL,
        TextBody         nvarchar(max) NOT NULL,
        HtmlBody         nvarchar(max) NOT NULL,
        CreatedAtUtc     datetime2     NOT NULL,
        NextAttemptAtUtc datetime2     NOT NULL,
        Attempts         int           NOT NULL,
        SentAtUtc        datetime2     NULL,
        LastError        nvarchar(500) NULL
    );
    CREATE INDEX IX_EmailOutbox_SentAtUtc_NextAttemptAtUtc ON dbo.EmailOutbox (SentAtUtc, NextAttemptAtUtc);
END

IF OBJECT_ID(N'dbo.OneTimeCodes', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.OneTimeCodes (
        Id             int IDENTITY(1,1) NOT NULL CONSTRAINT PK_OneTimeCodes PRIMARY KEY,
        UserId         int          NOT NULL,
        Purpose        int          NOT NULL,
        CodeHash       nvarchar(64) NOT NULL,
        CreatedAtUtc   datetime2    NOT NULL,
        ExpiresAtUtc   datetime2    NOT NULL,
        FailedAttempts int          NOT NULL,
        ConsumedAtUtc  datetime2    NULL,
        CONSTRAINT FK_OneTimeCodes_Users_UserId
            FOREIGN KEY (UserId) REFERENCES dbo.Users (Id) ON DELETE CASCADE
    );
    CREATE INDEX IX_OneTimeCodes_UserId_Purpose_CreatedAtUtc ON dbo.OneTimeCodes (UserId, Purpose, CreatedAtUtc);
END");

            // AddUserLastLogin
            await db.Database.ExecuteSqlRawAsync(@"
IF COL_LENGTH(N'dbo.Users', N'LastLoginAtUtc') IS NULL
    ALTER TABLE dbo.Users ADD LastLoginAtUtc datetime2 NULL;
IF COL_LENGTH(N'dbo.Users', N'LastLogoutAtUtc') IS NULL
    ALTER TABLE dbo.Users ADD LastLogoutAtUtc datetime2 NULL;
IF COL_LENGTH(N'dbo.Users', N'LastSeenAtUtc') IS NULL
    ALTER TABLE dbo.Users ADD LastSeenAtUtc datetime2 NULL;");
        }
    }
}
