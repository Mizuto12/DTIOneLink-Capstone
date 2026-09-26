using DTIOneLink.Data;
using DTIOneLink.Services;
using DTIOneLink.Models;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Services
{
    public class TaskReminderService : BackgroundService
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<TaskReminderService> _logger;

        // How far ahead "due soon" looks, and how often this check re-runs.
        private static readonly TimeSpan DueSoonWindow = TimeSpan.FromDays(2);
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

        public TaskReminderService(IServiceScopeFactory scopeFactory, ILogger<TaskReminderService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // BackgroundService is a singleton, but AppDbContext/NotificationService
            // are scoped — same reason NotificationService is AddScoped in Program.cs.
            // Each run gets its own scope.
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunCheckAsync(stoppingToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "TaskReminderService: reminder check failed");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }
        }

        private async Task RunCheckAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var notifications = scope.ServiceProvider.GetRequiredService<NotificationService>();

            var today = DateTime.UtcNow.Date;
            var dueSoonCutoff = today.Add(DueSoonWindow);

            // Due-soon/overdue is now evaluated per assignee, not per task —
            // one employee on a shared task can be done while another is
            // still overdue, and each needs their own notification driven by
            // their own TaskAssignment.Status.
            var incompleteAssignments = await db.TaskAssignments
                .Include(a => a.Task).ThenInclude(t => t!.ParentTask)
                .Include(a => a.User)
                .Where(a => a.Status != TaskWorkflow.Completed && a.Task!.Status != TaskWorkflow.Completed)
                .ToListAsync(stoppingToken);

            // No early return here: Department Directive Main Tasks have no
            // assignments, and are checked separately below.

            // Admin/Supervisor only ever receive overdue copies for their OWN
            // department. SuperAdmin (OPD) is office-wide, so still gets all.
            var departmentAdmins = await db.Users
                .Where(u => u.IsActive && (u.Role == "Admin" || u.Role == "Supervisor"))
                .Select(u => new { u.Id, u.Department })
                .ToListAsync(stoppingToken);

            var superAdminIds = await db.Users
                .Where(u => u.IsActive && u.Role == "SuperAdmin")
                .Select(u => u.Id)
                .ToListAsync(stoppingToken);

            // The admin "overdue" copy is a task-level notice, not a
            // per-assignee one — send it at most once per task per run even
            // if several assignees on that task are overdue. NotifyTaskOverdueAsync
            // already de-dupes across runs via ExistsAsync; this just avoids
            // redundant calls within a single run.
            var notifiedAdminForTask = new HashSet<int>();

            // Department Directive Main Tasks have no TaskAssignment rows, so
            // the per-assignment loop below never sees them. Direct Admin Tasks
            // are excluded — their Admin is a normal assignee and is covered
            // by the loop below.
            var openDirectives = await db.TaskItems
                .Where(t => t.TaskLevel == TaskLevels.Main
                         && t.TaskType != TaskTypes.DirectAdmin
                         && t.TaskType != TaskTypes.WholeOffice
                         && t.Status != TaskWorkflow.Completed
                         && t.OwningDepartment != null)
                .ToListAsync(stoppingToken);

            foreach (var directive in openDirectives)
            {
                if (!TaskWorkflow.IsOverdue(directive.Status, directive.DueDate)) continue;

                var directiveRecipients = departmentAdmins
                    .Where(a => string.Equals(a.Department, directive.OwningDepartment, StringComparison.OrdinalIgnoreCase))
                    .Select(a => a.Id)
                    .Concat(NotificationService.ResolveOpdRecipients(directive, superAdminIds))
                    .Distinct();

                foreach (var adminId in directiveRecipients)
                {
                    await notifications.NotifyTaskOverdueAsync(adminId, directive.Id, directive.TaskName, directive.DueDate, isAdminCopy: true);
                }
            }

            foreach (var assignment in incompleteAssignments)
            {
                var task = assignment.Task!;

                // Reuses TaskWorkflow.IsOverdue (same calculation already used for
                // display everywhere else) rather than reimplementing the date logic.
                if (TaskWorkflow.IsOverdue(assignment.Status, task.DueDate))
                {
                    await notifications.NotifyTaskOverdueAsync(assignment.UserId, task.Id, task.TaskName, task.DueDate);

                    if (notifiedAdminForTask.Add(task.Id))
                    {
                        var taskDepartment = NotificationService.ResolveEffectiveDepartment(task, assignment.User?.Department);

                        var adminRecipients = departmentAdmins
                            .Where(a => taskDepartment != null &&
                                        string.Equals(a.Department, taskDepartment, StringComparison.OrdinalIgnoreCase))
                            .Select(a => a.Id)
                            .Concat(NotificationService.ResolveOpdRecipients(task, superAdminIds))
                            .Distinct();

                        foreach (var adminId in adminRecipients)
                        {
                            await notifications.NotifyTaskOverdueAsync(adminId, task.Id, task.TaskName, task.DueDate, isAdminCopy: true);
                        }
                    }
                }
                else if (task.DueDate.Date <= dueSoonCutoff)
                {
                    await notifications.NotifyTaskDueSoonAsync(assignment.UserId, task.Id, task.TaskName, task.DueDate);
                }
            }
        }
    }
}