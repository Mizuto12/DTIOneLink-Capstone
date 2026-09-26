using DTIOneLink.Data;
using DTIOneLink.Models;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Services
{
    // Sends out the next copy of each repeating OPD task. When a repeating
    // task's due date arrives, a new copy is created — due one interval
    // later — through OpdTaskService, so it is given out exactly like a new
    // task: a Direct Admin Task is assigned to the responsible Admin; a
    // Department Directive goes to the department (with the same subtask
    // names, unassigned) for its Admin to assign employees. The repeat
    // setting then moves to the new copy. Runs at startup, then hourly.
    public class RecurringTaskService : BackgroundService
    {
        private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(1);

        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ILogger<RecurringTaskService> _logger;

        public RecurringTaskService(IServiceScopeFactory scopeFactory, ILogger<RecurringTaskService> logger)
        {
            _scopeFactory = scopeFactory;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await RunAsync(stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "RecurringTaskService: repeating-task check failed");
                }

                await Task.Delay(CheckInterval, stoppingToken);
            }
        }

        private async Task RunAsync(CancellationToken stoppingToken)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Same "today" as TaskWorkflow.IsOverdue.
            var today = DateTime.UtcNow.Date;

            var dueIds = await db.TaskItems
                .Where(t => t.TaskLevel == TaskLevels.Main && t.Recurrence != null && t.DueDate <= today)
                .Select(t => t.Id)
                .ToListAsync(stoppingToken);

            foreach (var id in dueIds)
            {
                // One scope per copy, so a failure on one task can't leave
                // half-saved changes that the next one would then save.
                using var taskScope = _scopeFactory.CreateScope();
                try
                {
                    await RepeatAsync(taskScope.ServiceProvider, id, today, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _logger.LogError(ex, "RecurringTaskService: could not repeat task {TaskId}", id);
                }
            }
        }

        private async Task RepeatAsync(IServiceProvider services, int taskId, DateTime today, CancellationToken stoppingToken)
        {
            var db = services.GetRequiredService<AppDbContext>();
            var opdTasks = services.GetRequiredService<OpdTaskService>();

            var previous = await db.TaskItems
                .Include(t => t.Subtasks)
                .FirstOrDefaultAsync(t => t.Id == taskId, stoppingToken);

            var isWholeOffice = previous?.TaskType == TaskTypes.WholeOffice;
            if (previous?.Recurrence == null || !TaskRecurrence.IsValid(previous.Recurrence)
                || previous.TaskType == null || previous.OwningDepartment == null
                || (previous.ResponsibleAdminUserId == null && !isWholeOffice))
            {
                return;
            }

            // Same rule as creating a task: the responsible Admin must still be
            // an active Admin of that department. If not, keep waiting (and
            // say why in the log) until the OPD fixes it or stops the repeat.
            // (A Whole Office copy has no Responsible Admin; it simply goes to
            // everyone who is active when the copy is sent out.)
            var adminStillValid = isWholeOffice || await db.Users.AnyAsync(u =>
                u.Id == previous.ResponsibleAdminUserId!.Value && u.IsActive &&
                u.Role == "Admin" && u.Department == previous.OwningDepartment, stoppingToken);
            if (!adminStillValid)
            {
                _logger.LogWarning(
                    "Repeating task {TaskId} was not sent out: its responsible Admin is no longer an active Admin of {Department}.",
                    previous.Id, previous.OwningDepartment);
                return;
            }

            var frequency = previous.Recurrence;
            var subtaskNames = TaskTypes.IsAssignedByOpd(previous.TaskType)
                ? new List<string>()
                : previous.Subtasks.OrderBy(s => s.Id).Select(s => s.TaskName).ToList();

            await using var transaction = await db.Database.BeginTransactionAsync(stoppingToken);

            // The repeat setting moves to the new copy, so only the latest
            // copy ever repeats.
            previous.Recurrence = null;
            await db.SaveChangesAsync(stoppingToken);

            var (copy, _) = await opdTasks.CreateAsync(
                new OpdTaskService.NewOpdTask(
                    previous.TaskName,
                    previous.Description,
                    TaskRecurrence.NextDueDate(previous.DueDate, frequency, today),
                    previous.Priority,
                    previous.TaskType,
                    previous.OwningDepartment,
                    previous.ResponsibleAdminUserId,
                    subtaskNames,
                    frequency),
                previous.CreatedByUserId,
                $"Repeated automatically ({TaskRecurrence.Label(frequency).ToLowerInvariant()}) from TASK-{previous.Id:D4}.");

            await transaction.CommitAsync(stoppingToken);

            _logger.LogInformation("Repeating task {PreviousId} sent out as TASK-{NewId:D4}, due {Due:yyyy-MM-dd}.",
                previous.Id, copy.Id, copy.DueDate);
        }
    }
}
