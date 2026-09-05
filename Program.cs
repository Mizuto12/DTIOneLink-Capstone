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
builder.Services.AddHostedService<TaskReminderService>();

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

app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

app.Run();