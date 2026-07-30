using DTIOneLink.Data;
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

// Create the database and its tables from the model if they don't exist yet.
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    try
    {
        db.Database.EnsureCreated();
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to create the database at startup. Check the DtiLagunaDb connection string.");
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
