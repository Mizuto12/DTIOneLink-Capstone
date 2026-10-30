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
            var today = TimeZoneHelper.PhilippineToday;

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
                .Include(t => t.Assignments)
                .FirstOrDefaultAsync(t => t.Id == taskId, stoppingToken);

            if (previous == null)
            {
                // Deleted between the query that found it due and now —
                // nothing to log, there's no task left to warn about.
                return;
            }

            var isWholeOffice = previous.TaskType == TaskTypes.WholeOffice;
            if (previous.Recurrence == null || !TaskRecurrence.IsValid(previous.Recurrence)
                || previous.TaskType == null || previous.OwningDepartment == null
                || (previous.ResponsibleAdminUserId == null && !isWholeOffice))
            {
                // Previously silent: a task with a missing/invalid TaskType,
                // OwningDepartment, Recurrence or ResponsibleAdminUserId was
                // found due every single hourly run, forever, with no record
                // anywhere that anything was wrong or that it was never sent
                // out. Logging here doesn't fix the data, but it means this
                // is at least visible instead of invisible.
                _logger.LogWarning(
                    "Repeating task {TaskId} was not sent out: its data is incomplete or invalid for repeating " +
                    "(Recurrence={Recurrence}, TaskType={TaskType}, OwningDepartment={OwningDepartment}, " +
                    "ResponsibleAdminUserId={ResponsibleAdminUserId}). It will keep being skipped every run until " +
                    "this is corrected or its repeat is stopped.",
                    previous.Id, previous.Recurrence, previous.TaskType, previous.OwningDepartment, previous.ResponsibleAdminUserId);
                return;
            }

            // Same rule as creating a task: the responsible Admin must still be
            // an active Admin. If not, keep waiting (and say why in the log)
            // until the OPD fixes it or stops the repeat. (A Whole Office
            // copy has no Responsible Admin; it simply goes to everyone who
            // is active when the copy is sent out.)
            //
            // A Direct Admin Task may have been given to more than one Admin
            // (TasksController.Create's DirectAdminUserIds); co-assignees can
            // be from a different department than OwningDepartment (that's
            // the point of picking more than one), so — unlike the
            // single-recipient TaskType below — only "still an active Admin"
            // is checked, not department. Order is preserved primary-first
            // so a surviving ResponsibleAdminUserId stays the primary on the
            // new copy; if it alone dropped out, whichever surviving
            // co-assignee comes first becomes the new primary.
            var isDirectAdmin = previous.TaskType == TaskTypes.DirectAdmin;
            List<int> directAdminAssigneeIds = new();
            bool adminStillValid;
            if (isWholeOffice)
            {
                adminStillValid = true;
            }
            else if (isDirectAdmin)
            {
                var previousAssigneeIds = previous.Assignments
                    .OrderByDescending(a => a.IsPrimaryAssignee)
                    .Select(a => a.UserId)
                    .Distinct()
                    .ToList();
                var sourceIds = previousAssigneeIds.Count > 0
                    ? previousAssigneeIds
                    : new List<int> { previous.ResponsibleAdminUserId!.Value };
                var activeIds = (await db.Users
                    .Where(u => sourceIds.Contains(u.Id) && u.IsActive && u.Role == "Admin")
                    .Select(u => u.Id)
                    .ToListAsync(stoppingToken)).ToHashSet();
                directAdminAssigneeIds = sourceIds.Where(activeIds.Contains).ToList();
                adminStillValid = directAdminAssigneeIds.Count > 0;
            }
            else
            {
                adminStillValid = await db.Users.AnyAsync(u =>
                    u.Id == previous.ResponsibleAdminUserId!.Value && u.IsActive &&
                    u.Role == "Admin" && u.Department == previous.OwningDepartment, stoppingToken);
            }
            if (!adminStillValid)
            {
                _logger.LogWarning(
                    "Repeating task {TaskId} was not sent out: no originally-assigned Admin is still active.",
                    previous.Id);
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
                    isDirectAdmin ? directAdminAssigneeIds[0] : previous.ResponsibleAdminUserId,
                    subtaskNames,
                    frequency,
                    DirectAdminAssigneeIds: isDirectAdmin ? directAdminAssigneeIds : null),
                previous.CreatedByUserId,
                $"Repeated automatically ({TaskRecurrence.Label(frequency).ToLowerInvariant()}) from TASK-{previous.Id:D4}.");

            await transaction.CommitAsync(stoppingToken);

            _logger.LogInformation("Repeating task {PreviousId} sent out as TASK-{NewId:D4}, due {Due:yyyy-MM-dd}.",
                previous.Id, copy.Id, copy.DueDate);
        }
    }
}
