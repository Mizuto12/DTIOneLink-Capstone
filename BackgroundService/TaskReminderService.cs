using DTIOneLink.Data;
using DTIOneLink.Services;
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

            // Every TaskItem has a required AssigneeId — no unassigned state to guard against.
            var incompleteTasks = await db.TaskItems
                .Where(t => t.Status != TaskWorkflow.Completed)
                .ToListAsync(stoppingToken);

            if (incompleteTasks.Count == 0) return;
            
            var admins = await db.Users
                .Where(u => u.IsActive && (u.Role == "Admin" || u.Role == "Supervisor" || u.Role == "SuperAdmin"))
                .Select(u => u.Id)
                .ToListAsync(stoppingToken);

            foreach (var task in incompleteTasks)
            {
                // Reuses TaskWorkflow.IsOverdue (same calculation already used for
                // display everywhere else) rather than reimplementing the date logic.
                if (TaskWorkflow.IsOverdue(task.Status, task.DueDate))
                {
                    await notifications.NotifyTaskOverdueAsync(task.AssigneeId, task.Id, task.TaskName, task.DueDate);

                    foreach (var adminId in admins)
                    {
                        await notifications.NotifyTaskOverdueAsync(adminId, task.Id, task.TaskName, task.DueDate, isAdminCopy: true);
                    }
                }
                else if (task.DueDate.Date <= dueSoonCutoff)
                {
                    await notifications.NotifyTaskDueSoonAsync(task.AssigneeId, task.Id, task.TaskName, task.DueDate);
                }
            }
        }
    }
}