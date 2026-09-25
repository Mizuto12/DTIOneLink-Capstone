using DTIOneLink.Data;
using DTIOneLink.Services;
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

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();

// Must come before UseAuthorization, and before any endpoint that reads session
app.UseSession();

// Keeps the signed-in user's role/division in step with the database, so a
// promotion, demotion or division change made by the OPD applies on the
// user's next page load (not only after they sign out). A deactivated or
// deleted account is signed out.
app.Use(async (context, next) =>
{
    var userId = context.Session.GetInt32("UserId");
    if (userId != null)
    {
        var db = context.RequestServices.GetRequiredService<AppDbContext>();
        var current = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId.Value)
            .Select(u => new { u.Role, u.Department, u.IsActive })
            .FirstOrDefaultAsync();

        if (current == null || !current.IsActive)
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