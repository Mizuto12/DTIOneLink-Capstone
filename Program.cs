using DTIOneLink.Data;
using DTIOneLink.Security;
using DTIOneLink.Controllers;
using DTIOneLink.Services;
using DTIOneLink.Services.Email;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── MVC ────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

// ── Database (EF Core) ──────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── DatabaseHelper — registered ONCE here, injectable anywhere
builder.Services.AddSingleton<DatabaseHelper>();
builder.Services.AddScoped<NotificationService>();
builder.Services.AddScoped<TaskAssignmentService>();
builder.Services.AddScoped<OpdTaskService>();
builder.Services.AddHostedService<RecurringTaskService>();
builder.Services.AddHostedService<TaskReminderService>();
builder.Services.AddScoped<RecordRetentionReminder>();
builder.Services.AddHostedService<RecordRetentionReminderService>();

// ── Live updates (SignalR) ──────────────────────────────────
// Open pages listen on /hubs/live; LiveChangeBroadcaster pushes a change
// when the data's fingerprint moves (see live-refresh.js).
builder.Services.AddSignalR();
builder.Services.AddSingleton<LiveVersionService>();
builder.Services.AddHostedService<LiveChangeBroadcaster>();

// ── Email (Brevo) and emailed codes ─────────────────────────
// Brevo:ApiKey and Brevo:SenderEmail come from `dotnet user-secrets` locally
// and from GitHub secrets in production — never from files in the repo.
builder.Services.Configure<BrevoOptions>(builder.Configuration.GetSection("Brevo"));
if (builder.Configuration.GetSection("Brevo").Get<BrevoOptions>()?.IsConfigured == true)
{
    builder.Services.AddHttpClient<IEmailSender, BrevoEmailSender>(c => c.Timeout = TimeSpan.FromSeconds(20));
}
else
{
    builder.Services.AddSingleton<IEmailSender, UnconfiguredEmailSender>();
}
builder.Services.AddSingleton<OtpEmailQueue>();
builder.Services.AddSingleton<OneTimeCodeHasher>();
builder.Services.AddScoped<OneTimeCodeService>();
builder.Services.AddHostedService<EmailDispatchService>();
builder.Services.AddAuthRateLimit();

builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "X-CSRF-TOKEN";
});

// ── Session (needed to persist login state) ─────────────────
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
    // Always in production (real traffic is HTTPS); local dev runs on
    // plain http://localhost, where "Always" would silently drop the
    // cookie and make login loop forever.
    options.Cookie.SecurePolicy = builder.Environment.IsDevelopment()
        ? CookieSecurePolicy.SameAsRequest
        : CookieSecurePolicy.Always;
});

var app = builder.Build();

// Brings the database up to date on every start, so a deploy that adds
// columns or tables needs no manual database step. The hosted database is
// only reachable from the host itself, so this is where it has to happen.
// If it fails, the error (and the migration it stopped at) is written to
// App_Data/database-update-error.txt (not served to browsers), readable from
// the host's file manager, and SchemaRepair adds the newest columns and tables
// directly so the site keeps working. The file is removed after a clean update.
//
// Always the plain MigrateAsync(): it only moves forward. Migrating to a named
// target would roll back any later migration already applied.
using (var scope = app.Services.CreateScope())
{
    var errorFile = Path.Combine(app.Environment.ContentRootPath, "App_Data", "database-update-error.txt");
    var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;
    try
    {
        await database.MigrateAsync();
        File.Delete(errorFile);
    }
    catch (Exception ex)
    {
        string failed;
        try { failed = (await database.GetPendingMigrationsAsync()).FirstOrDefault() ?? "(unknown)"; }
        catch { failed = "(unknown)"; }

        app.Logger.LogError(ex, "Could not apply database migration {Migration}.", failed);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(errorFile)!);
            await File.WriteAllTextAsync(errorFile,
                $"{DateTime.UtcNow:u}\nFailed migration: {failed}\n\n{ex}");
        }
        catch { /* logging above is enough if the folder is read-only */ }

        try
        {
            await SchemaRepair.EnsureLatestSchemaAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>());
            app.Logger.LogInformation("Added any missing columns and tables directly after the migration failed.");
        }
        catch (Exception repairEx)
        {
            app.Logger.LogError(repairEx, "Could not add the missing columns and tables directly.");
            try { await File.AppendAllTextAsync(errorFile, $"\n\nDirect repair also failed:\n{repairEx}"); } catch { }
        }
    }
}

// Codes are checked with a key that exists only in memory (OneTimeCodeHasher),
// so codes from before this start can never match. Mark them used, so the
// code page sends a fresh code instead of waiting on one that can't work.
using (var scope = app.Services.CreateScope())
{
    try
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var cancelled = await db.OneTimeCodes
            .Where(c => c.ConsumedAtUtc == null)
            .ExecuteUpdateAsync(s => s.SetProperty(c => c.ConsumedAtUtc, DateTime.UtcNow));
        if (cancelled > 0)
            app.Logger.LogInformation("Cancelled {Count} verification code(s) left from before the restart.", cancelled);
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Could not cancel verification codes left from before the restart.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
    app.UseHttpsRedirection();
}

// The server is in Germany (~0.25 s each way from the Philippines), so every
// file the browser has to re-check costs a round trip. Files linked with
// asp-append-version carry "?v=<hash>" that changes whenever the file does,
// so the browser may keep them for a year without asking again. Others
// (fonts, which fonts.css links without a version) are kept for a day, then
// re-checked. wwwroot holds only the app's own files, never uploads.
// (Compression is already done by the host's IIS.)
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = ctx =>
    {
        ctx.Context.Response.Headers.CacheControl = ctx.Context.Request.Query.ContainsKey("v")
            ? "public, max-age=31536000, immutable"
            : "public, max-age=86400";
    }
});
app.UseRouting();
app.UseRateLimiter();

// Must come before UseAuthorization, and before any endpoint that reads session
app.UseSession();

// Keeps the signed-in user's role/division in step with the database, so a
// promotion, demotion or division change made by the OPD applies on the
// user's next page load (not only after they sign out). A deactivated or
// deleted account is signed out, and so is any session started before the
// account's password or email last changed (security stamp no longer matches).
app.Use(async (context, next) =>
{
    var userId = context.Session.GetInt32("UserId");
    if (userId != null)
    {
        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var current = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId.Value)
            .Select(u => new { u.Role, u.Department, u.IsActive, u.SecurityStamp, u.LastSeenAtUtc })
            .FirstOrDefaultAsync();

        if (current == null || !current.IsActive
            || context.Session.GetString(AccountController.SecurityStampKey) != current.SecurityStamp)
        {
            context.Session.Clear();
        }
        else
        {
            if (context.Session.GetString("UserRole") != current.Role)
                context.Session.SetString("UserRole", current.Role);
            if (context.Session.GetString("UserDepartment") != (current.Department ?? string.Empty))
                context.Session.SetString("UserDepartment", current.Department ?? string.Empty);

            // "Signed in" status in User Management. Written at most once a
            // minute; an open tab checks notifications every minute, so it
            // stays fresh while the session is alive.
            var now = DateTime.UtcNow;
            if (current.LastSeenAtUtc == null || now - current.LastSeenAtUtc.Value > TimeSpan.FromMinutes(1))
            {
                await db.Users.Where(u => u.Id == userId.Value)
                    .ExecuteUpdateAsync(s => s.SetProperty(u => u.LastSeenAtUtc, now));
            }
        }
    }

    await next();
});

// Live updates are for signed-in users only: refuse the hub's connection
// request outright rather than accepting it and dropping it afterwards.
app.Use(async (context, next) =>
{
    if (context.Request.Path.StartsWithSegments("/hubs/live")
        && context.Session.GetInt32("UserId") == null)
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        return;
    }
    await next();
});

app.UseAuthorization();

// "/" opens the login page; "/Records", "/Tasks" etc. open that page's
// Index. (Before, every controller defaulted to a Login action, so
// "/Records" was a 404.)
app.MapControllerRoute(
    name: "login",
    pattern: "",
    defaults: new { controller = "Account", action = "Login" });

// "/Account" has no Index page; keep it opening the login page as before.
app.MapControllerRoute(
    name: "account",
    pattern: "Account",
    defaults: new { controller = "Account", action = "Login" });

app.MapControllerRoute(
    name: "default",
    pattern: "{controller}/{action=Index}/{id?}");

// After UseSession, so the hub can see who is signed in.
app.MapHub<LiveHub>("/hubs/live");

// For an uptime monitor (UptimeRobot) to open every few minutes. That keeps
// the host from putting the site to sleep when nobody is using it, so the
// hourly reminder, recurring-task and email jobs keep running. Answers "OK"
// only when the database can be reached; otherwise 503, so the monitor
// emails its owner. Public and reveals nothing else.
app.MapGet("/health", async (AppDbContext db, HttpContext context, CancellationToken cancellationToken) =>
{
    context.Response.Headers.CacheControl = "no-store";
    return await db.Database.CanConnectAsync(cancellationToken)
        ? Results.Text("OK")
        : Results.Text("Database unavailable", statusCode: StatusCodes.Status503ServiceUnavailable);
});

app.Run();