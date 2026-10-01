using DTIOneLink.Data;
using DTIOneLink.Security;
using DTIOneLink.Controllers;
using DTIOneLink.Services;
using DTIOneLink.Services.Email;
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
});

var app = builder.Build();

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
}

app.UseStaticFiles();
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
            .Select(u => new { u.Role, u.Department, u.IsActive, u.SecurityStamp })
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
        }
    }

    await next();
});

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();