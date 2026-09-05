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
                .Include(a => a.Task)
                .Where(a => a.Status != TaskWorkflow.Completed && a.Task!.Status != TaskWorkflow.Completed)
                .ToListAsync(stoppingToken);

            if (incompleteAssignments.Count == 0) return;

            var admins = await db.Users
                .Where(u => u.IsActive && (u.Role == "Admin" || u.Role == "Supervisor" || u.Role == "SuperAdmin"))
                .Select(u => u.Id)
                .ToListAsync(stoppingToken);

            // The admin "overdue" copy is a task-level notice, not a
            // per-assignee one — send it at most once per task per run even
            // if several assignees on that task are overdue. NotifyTaskOverdueAsync
            // already de-dupes across runs via ExistsAsync; this just avoids
            // redundant calls within a single run.
            var notifiedAdminForTask = new HashSet<int>();

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
                        foreach (var adminId in admins)
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