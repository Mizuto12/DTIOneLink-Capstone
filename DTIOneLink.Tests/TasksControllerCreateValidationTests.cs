using DTIOneLink.Controllers;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTIOneLink.Tests;

// Regression coverage for two 2026-10-29 audit findings on TasksController.Create:
// Priority had no server-side allow-list check (any non-empty string was
// accepted and persisted — confirmed live with "ultra-mega-urgent"), and
// DueDate had no past-date check at all, for any role.
public class TasksControllerCreateValidationTests
{
    private static (AppDbContext Context, User Admin) SeedAdmin()
    {
        var context = InMemoryDbContextFactory.Create();
        var admin = new User
        {
            Username = "admin@dti.gov.ph",
            Email = "admin@dti.gov.ph",
            FullName = "Test Admin",
            Role = "Admin",
            Department = TargetDepartments.BDD,
            IsActive = true,
            EmailConfirmed = true,
            SecurityStamp = "stamp",
        };
        context.Users.Add(admin);
        context.SaveChanges();
        return (context, admin);
    }

    private static TasksController BuildController(AppDbContext context)
    {
        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        httpContext.Session.SetString("UserRole", "SuperAdmin");
        httpContext.Session.SetInt32("UserId", 99);

        var config = new ConfigurationBuilder().Build();
        var notifications = new NotificationService(context, config, NullLogger<NotificationService>.Instance);
        var taskAssignments = new TaskAssignmentService(context);
        var opdTasks = new OpdTaskService(context, taskAssignments, notifications);

        return new TasksController(context, notifications, taskAssignments, opdTasks, NullLogger<TasksController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(httpContext, new FakeTempDataProvider()),
        };
    }

    private static TaskCreateViewModel ValidModel(User admin) => new()
    {
        TaskName = "Quarterly Report",
        Description = "Prepare the quarterly report.",
        TaskType = TaskTypes.DirectAdmin,
        OwningDepartment = TargetDepartments.BDD,
        ResponsibleAdminUserId = admin.Id,
        DirectAdminUserIds = new List<int> { admin.Id },
        DueDate = DateTime.UtcNow.AddDays(5),
        Priority = "medium",
    };

    [Fact]
    public async Task Create_InvalidPriority_IsRejectedAndNothingSaved()
    {
        var (context, admin) = SeedAdmin();
        var controller = BuildController(context);
        var model = ValidModel(admin);
        model.Priority = "ultra-mega-urgent";

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(await context.TaskItems.AnyAsync());
    }

    [Fact]
    public async Task Create_PastDueDate_IsRejectedAndNothingSaved()
    {
        var (context, admin) = SeedAdmin();
        var controller = BuildController(context);
        var model = ValidModel(admin);
        model.DueDate = DateTime.UtcNow.AddDays(-5);

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(await context.TaskItems.AnyAsync());
    }

    [Fact]
    public async Task Create_ValidPriorityAndFutureDueDate_Succeeds()
    {
        var (context, admin) = SeedAdmin();
        var controller = BuildController(context);
        var model = ValidModel(admin);

        var result = await controller.Create(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(await context.TaskItems.AnyAsync());
    }

    private static TaskCreateViewModel ValidDepartmentDirectiveModel(User admin) => new()
    {
        TaskName = "Department Directive",
        Description = "Covers everyone in the department.",
        TaskType = TaskTypes.DepartmentDirective,
        OwningDepartment = TargetDepartments.BDD,
        ResponsibleAdminUserId = admin.Id,
        DueDate = DateTime.UtcNow.AddDays(5),
        Priority = "medium",
    };

    // Validation-audit coverage (2026-10-30): the inline subtask-list
    // <input> on Create has no maxlength, and the server previously only
    // filtered out blank entries — an overlong name or a huge list both
    // went straight into the database.
    [Fact]
    public async Task Create_SubtaskNameOver200Characters_IsRejectedAndNothingSaved()
    {
        var (context, admin) = SeedAdmin();
        var controller = BuildController(context);
        var model = ValidDepartmentDirectiveModel(admin);
        model.SubtaskNames = new List<string> { new string('a', 201) };

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(await context.TaskItems.AnyAsync());
    }

    [Fact]
    public async Task Create_TooManySubtasks_IsRejectedAndNothingSaved()
    {
        var (context, admin) = SeedAdmin();
        var controller = BuildController(context);
        var model = ValidDepartmentDirectiveModel(admin);
        model.SubtaskNames = Enumerable.Range(1, 51).Select(i => $"Subtask {i}").ToList();

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(await context.TaskItems.AnyAsync());
    }

    [Fact]
    public async Task Create_SubtasksWithinLimits_Succeeds()
    {
        var (context, admin) = SeedAdmin();
        var controller = BuildController(context);
        var model = ValidDepartmentDirectiveModel(admin);
        model.SubtaskNames = new List<string> { "Subtask A", "Subtask B" };

        var result = await controller.Create(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal(2, await context.TaskItems.CountAsync(t => t.TaskLevel == TaskLevels.Subtask));
    }

    // Validation-audit coverage (2026-10-30): [Required]+[StringLength]
    // alone let a TaskName/Description made only of symbols or punctuation
    // through (e.g. "...", "---") — same gap already fixed for User
    // Management's FullName.
    [Theory]
    [InlineData("...")]
    [InlineData("---")]
    [InlineData("   .   ")]
    public async Task Create_TaskNameIsSymbolsOnly_IsRejectedAndNothingSaved(string taskName)
    {
        var (context, admin) = SeedAdmin();
        var controller = BuildController(context);
        var model = ValidModel(admin);
        model.TaskName = taskName;

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(await context.TaskItems.AnyAsync());
    }

    [Theory]
    [InlineData("...")]
    [InlineData("---")]
    public async Task Create_DescriptionIsSymbolsOnly_IsRejectedAndNothingSaved(string description)
    {
        var (context, admin) = SeedAdmin();
        var controller = BuildController(context);
        var model = ValidModel(admin);
        model.Description = description;

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(await context.TaskItems.AnyAsync());
    }

    [Fact]
    public async Task Create_TaskNameIsNumericOnly_PassesTheGuard()
    {
        // Letters OR digits count as real content here (unlike FullName,
        // which required a letter specifically) — a numeric-only task name
        // like "2026" is legitimate.
        var (context, admin) = SeedAdmin();
        var controller = BuildController(context);
        var model = ValidModel(admin);
        model.TaskName = "2026";

        var result = await controller.Create(model);

        Assert.IsType<RedirectToActionResult>(result);
        Assert.True(await context.TaskItems.AnyAsync(t => t.TaskName == "2026"));
    }

    // Production incident, 2026-10-30: a Whole Office task submitted with
    // Description left blank crashed with ArgumentNullException instead of
    // showing a validation error. ASP.NET Core's SimpleTypeModelBinder binds
    // an empty submitted form field to null, not "" — HasRealContent called
    // value.Any(...) on that null with no guard. model.Description = null!
    // here reproduces exactly what model binding actually produced in
    // production, not just an empty string a test might pick instead.
    [Fact]
    public async Task Create_WholeOfficeWithNullDescription_IsRejectedNotCrashed()
    {
        var (context, _) = SeedAdmin();
        var controller = BuildController(context);
        var model = new TaskCreateViewModel
        {
            TaskName = "Quarterly Report",
            Description = null!,
            TaskType = TaskTypes.WholeOffice,
            DueDate = DateTime.UtcNow.AddDays(5),
            Priority = "medium",
        };

        var result = await controller.Create(model);

        Assert.IsType<ViewResult>(result);
        Assert.False(controller.ModelState.IsValid);
        Assert.False(await context.TaskItems.AnyAsync());
    }
}
