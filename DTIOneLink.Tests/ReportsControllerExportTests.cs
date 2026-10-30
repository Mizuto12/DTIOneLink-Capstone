using ClosedXML.Excel;
using DTIOneLink.Controllers;
using DTIOneLink.Data;
using DTIOneLink.Models;
using DTIOneLink.Services;
using DTIOneLink.Tests.TestDoubles;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;

namespace DTIOneLink.Tests;

// Coverage for ReportsController.Export — the new server-side .xlsx
// download. Shares ReportsController.Data's scoping/period logic
// (BuildReportItemsAsync), so this focuses on what's new: auth gating,
// server-side re-application of category/search filters, and that a real
// downloadable file comes back. OwnRecordsAsync's raw-SQL call fails
// against the unreachable connection string below but is caught internally
// (RecordsController-style "tasks still load if Records can't be read"),
// so these tests exercise the task-side data without needing a real DB.
public class ReportsControllerExportTests
{
    private const string UnreachableConnectionString =
        "Server=127.0.0.1,1;Database=unreachable;Connect Timeout=1;TrustServerCertificate=True;";

    private static (AppDbContext Context, User Employee, TaskItem ActiveTask, TaskItem CompletedTask) Seed()
    {
        var context = InMemoryDbContextFactory.Create();
        var employee = new User { Username = "emp", PasswordHash = "x", FullName = "Employee One", Role = "Employee", Department = "BDD" };
        context.Users.Add(employee);
        context.SaveChanges();

        var activeTask = new TaskItem { TaskName = "Quarterly Report", Description = "d", DueDate = DateTime.UtcNow.AddDays(5), Status = TaskWorkflow.InProgress, Progress = 40 };
        var completedTask = new TaskItem { TaskName = "Inventory Count", Description = "d", DueDate = DateTime.UtcNow.AddDays(-2), Status = TaskWorkflow.Completed, Progress = 100 };
        context.TaskItems.AddRange(activeTask, completedTask);
        context.SaveChanges();

        context.TaskAssignments.Add(new TaskAssignment { TaskId = activeTask.Id, UserId = employee.Id, Status = TaskWorkflow.InProgress, Progress = 40 });
        context.TaskAssignments.Add(new TaskAssignment { TaskId = completedTask.Id, UserId = employee.Id, Status = TaskWorkflow.Completed, Progress = 100 });
        context.SaveChanges();

        return (context, employee, activeTask, completedTask);
    }

    private static ReportsController BuildController(AppDbContext context, string? role, int userId, string? fullName = "Employee One")
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:DefaultConnection"] = UnreachableConnectionString })
            .Build();

        var httpContext = new DefaultHttpContext();
        httpContext.Features.Set<ISessionFeature>(new FakeSessionFeature());
        if (role != null) httpContext.Session.SetString("UserRole", role);
        httpContext.Session.SetInt32("UserId", userId);
        if (fullName != null) httpContext.Session.SetString("FullName", fullName);

        return new ReportsController(context, new DatabaseHelper(config), NullLogger<ReportsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = httpContext },
        };
    }

    private static XLWorkbook OpenReturnedWorkbook(IActionResult result)
    {
        var file = Assert.IsType<FileContentResult>(result);
        Assert.Equal(ReportsExcel.ContentType, file.ContentType);
        return new XLWorkbook(new MemoryStream(file.FileContents));
    }

    [Fact]
    public async Task Export_UnauthorizedRole_Returns403()
    {
        var (context, employee, _, _) = Seed();
        var controller = BuildController(context, "Guest", employee.Id);

        var result = await controller.Export(null, null, null, null);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status403Forbidden, status.StatusCode);
    }

    [Fact]
    public async Task Export_NoSessionUser_Returns401()
    {
        var (context, _, _, _) = Seed();
        var controller = BuildController(context, "Employee", 0);
        controller.HttpContext.Session.Clear();
        controller.HttpContext.Session.SetString("UserRole", "Employee"); // role present, UserId not

        var result = await controller.Export(null, null, null, null);

        var status = Assert.IsType<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status401Unauthorized, status.StatusCode);
    }

    [Fact]
    public async Task Export_InvalidPeriod_Returns400()
    {
        var (context, employee, _, _) = Seed();
        var controller = BuildController(context, "Employee", employee.Id);

        var result = await controller.Export("not-a-date", null, null, null);

        var bad = Assert.IsType<BadRequestObjectResult>(result);
        Assert.NotNull(bad.Value);
    }

    [Fact]
    public async Task Export_NoFilters_IncludesBothTasks()
    {
        var (context, employee, _, _) = Seed();
        var controller = BuildController(context, "Employee", employee.Id);

        var result = await controller.Export(null, null, null, null);

        using var wb = OpenReturnedWorkbook(result);
        var ws = wb.Worksheet("Report Items");
        var titles = ws.RowsUsed().Skip(1).Select(r => r.Cell(4).GetString()).ToList();
        Assert.Contains("Quarterly Report", titles);
        Assert.Contains("Inventory Count", titles);
    }

    [Fact]
    public async Task Export_CategoryFilter_OnlyIncludesThatCategory()
    {
        var (context, employee, _, _) = Seed();
        var controller = BuildController(context, "Employee", employee.Id);

        var result = await controller.Export(null, null, "Completed Tasks", null);

        using var wb = OpenReturnedWorkbook(result);
        var ws = wb.Worksheet("Report Items");
        var rows = ws.RowsUsed().Skip(1).ToList();
        Assert.Single(rows);
        Assert.Equal("Inventory Count", rows[0].Cell(4).GetString());
        Assert.Equal("Completed Tasks", rows[0].Cell(2).GetString());
    }

    [Fact]
    public async Task Export_SearchFilter_OnlyIncludesMatchingTitle()
    {
        var (context, employee, _, _) = Seed();
        var controller = BuildController(context, "Employee", employee.Id);

        var result = await controller.Export(null, null, null, "quarterly");

        using var wb = OpenReturnedWorkbook(result);
        var ws = wb.Worksheet("Report Items");
        var rows = ws.RowsUsed().Skip(1).ToList();
        Assert.Single(rows);
        Assert.Equal("Quarterly Report", rows[0].Cell(4).GetString());
    }

    [Fact]
    public async Task Export_SearchFilterMatchesNothing_ProducesEmptyDetailSheetNotAnError()
    {
        var (context, employee, _, _) = Seed();
        var controller = BuildController(context, "Employee", employee.Id);

        var result = await controller.Export(null, null, null, "no-such-task-exists");

        using var wb = OpenReturnedWorkbook(result);
        var ws = wb.Worksheet("Report Items");
        Assert.Single(ws.RowsUsed()); // header only
    }

    [Fact]
    public async Task Export_FileNameFollowsExpectedPattern()
    {
        var (context, employee, _, _) = Seed();
        var controller = BuildController(context, "Employee", employee.Id);

        var result = await controller.Export(null, null, null, null);

        var file = Assert.IsType<FileContentResult>(result);
        Assert.Matches(@"^DTI_OneLink_Reports_\d{4}-\d{2}-\d{2}\.xlsx$", file.FileDownloadName);
    }
}
