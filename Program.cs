using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Security;
using DTIOneLink.Services.Email;
using DTIOneLink.Services.Outlook;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// Add MVC services
builder.Services.AddControllersWithViews();

builder.Services.AddDbContext<AppDbContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("DtiLagunaDb")));

// Session-backed login/OTP challenge state.
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(30);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// OTP email + Outlook profile sync (dev implementations until Graph credentials are configured).
builder.Services.AddScoped<IEmailSender, DevEmailSender>();
builder.Services.AddScoped<IOutlookProfileService, DevOutlookProfileService>();

var app = builder.Build();

// Apply any pending EF Core migrations so the Task/User tables exist, then seed a default admin.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        db.Database.Migrate();
        SeedDefaultAdmin(db, app.Configuration, logger);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to apply database migrations at startup. Check the DtiLagunaDb connection string.");
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
}

app.UseStaticFiles();
app.UseRouting();
app.UseSession();
app.UseAuthorization();

// Default route lands on the login page
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();

// Creates a default administrator with the default password when no users exist yet,
// so the very first login is possible. The account is flagged to change its password.
static void SeedDefaultAdmin(AppDbContext db, IConfiguration config, ILogger logger)
{
    if (db.UserItems.Any())
    {
        return;
    }

    var email = config.GetValue("Auth:SeedAdminEmail", "admin@dti.gov.ph")!;
    var defaultPassword = config.GetValue("Auth:DefaultPassword", "ChangeMe123!")!;

    db.UserItems.Add(new UserItem
    {
        FullName = "System Administrator",
        Email = email,
        Role = "Admin",
        Department = "Financial and Administrative Unit",
        Status = "active",
        PasswordHash = PasswordHasher.Hash(defaultPassword),
        MustChangePassword = true,
        CreatedAt = DateTime.UtcNow
    });

    db.SaveChanges();
    logger.LogWarning(
        "Seeded default admin account: {Email} (default password from Auth:DefaultPassword; change on first login).",
        email);
}
