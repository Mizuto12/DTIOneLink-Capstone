using DTIOneLink.Data;
using DTIOneLink.Models;
using Microsoft.EntityFrameworkCore;

namespace DTIOneLink.Services
{
    // Creates an OPD (SuperAdmin) main task — a Direct Admin Task or a
    // Department Directive — with its assignment, subtasks, notifications,
    // and activity log. Shared by TasksController.Create and the repeating-
    // task job, so a repeated copy is given out exactly like a new task.
    // Callers validate the input first (type, department, responsible Admin).
    public class OpdTaskService
    {
        private readonly AppDbContext _context;
        private readonly TaskAssignmentService _taskAssignments;
        private readonly NotificationService _notifications;

        public OpdTaskService(AppDbContext context, TaskAssignmentService taskAssignments, NotificationService notifications)
        {
            _context = context;
            _taskAssignments = taskAssignments;
            _notifications = notifications;
        }

        public record NewOpdTask(
            string TaskName,
            string Description,
            DateTime DueDate,
            string Priority,
            string TaskType,
            string OwningDepartment,
            int? ResponsibleAdminUserId, // null for a Whole Office task
            IReadOnlyList<string> SubtaskNames,
            string? Recurrence);

        // activityNote: what the activity log says about how it was created
        // (e.g. "OPD created this Department Directive." or "Repeated
        // automatically from TASK-0012.").
        public async Task<(TaskItem MainTask, int SubtaskCount)> CreateAsync(
            NewOpdTask spec, int? createdByUserId, string activityNote)
        {
            var mainTask = new TaskItem
            {
                TaskName = spec.TaskName,
                DueDate = spec.DueDate,
                Priority = spec.Priority,
                Description = spec.Description,
                CreatedByUserId = createdByUserId,
                TaskLevel = TaskLevels.Main,
                TaskType = spec.TaskType,
                OwningDepartment = spec.OwningDepartment,
                ResponsibleAdminUserId = spec.ResponsibleAdminUserId,
                ParentTaskId = null,
                Recurrence = spec.Recurrence
            };

            _context.TaskItems.Add(mainTask);
            await _context.SaveChangesAsync(); // need mainTask.Id before attaching assignments/subtasks

            if (mainTask.TaskType == TaskTypes.WholeOffice)
            {
                // Everyone currently active in the office except the OPD
                // (Super Admin) gets their own assignment, so each person
                // works and submits it through their normal task board.
                var staff = await _context.Users
                    .Where(u => u.IsActive && (u.Role == "Admin" || u.Role == "Employee"))
                    .OrderBy(u => u.FullName)
                    .Select(u => u.Id)
                    .ToListAsync();

                if (staff.Count > 0)
                {
                    _taskAssignments.AssignEmployees(mainTask, staff, createdByUserId);
                    await _context.SaveChangesAsync();
                }

                foreach (var userId in staff)
                {
                    await _notifications.NotifyTaskAssignedAsync(userId, mainTask.Id, mainTask.TaskName);
                }

                if (createdByUserId.HasValue)
                {
                    TaskActivityLogger.Log(_context, mainTask.Id, createdByUserId.Value, "created", activityNote);
                    TaskActivityLogger.Log(_context, mainTask.Id, createdByUserId.Value, TaskActivityType.Assigned,
                        $"Given to the whole office ({staff.Count} people).");
                    await _context.SaveChangesAsync();
                }

                return (mainTask, 0);
            }

            if (mainTask.TaskType == TaskTypes.DirectAdmin)
            {
                var responsibleAdminId = spec.ResponsibleAdminUserId!.Value;
                // Assigned straight to the Responsible Admin via the
                // existing assignment system — mirrors AssigneeId for
                // legacy compatibility and gives the Admin a normal
                // TaskAssignment row to work through Employee/Update
                // and Employee/SubmitProof, same as any employee task.
                // No subtasks are ever attached to a Direct Admin Task.
                _taskAssignments.AssignEmployees(mainTask, new List<int> { responsibleAdminId }, createdByUserId);
                await _context.SaveChangesAsync();

                var responsibleAdmin = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == responsibleAdminId);
                var adminName = responsibleAdmin?.FullName ?? "the responsible Admin";

                await _notifications.NotifyTaskAssignedAsync(responsibleAdminId, mainTask.Id, mainTask.TaskName);

                if (createdByUserId.HasValue)
                {
                    TaskActivityLogger.Log(_context, mainTask.Id, createdByUserId.Value, "created", activityNote);
                    TaskActivityLogger.Log(_context, mainTask.Id, createdByUserId.Value, TaskActivityType.Assigned,
                        $"Directly assigned to {adminName}.");
                    await _context.SaveChangesAsync();
                }

                return (mainTask, 0);
            }

            // Department Directive: no TaskAssignment rows created here —
            // the receiving Admin assigns employees afterward. Each non-blank
            // subtask name becomes its own employee-level task parented to
            // this Main Task, left unassigned on purpose.
            var subtaskNames = spec.SubtaskNames
                .Select(n => n?.Trim())
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList();

            foreach (var subtaskName in subtaskNames)
            {
                _context.TaskItems.Add(new TaskItem
                {
                    TaskName = subtaskName!,
                    DueDate = mainTask.DueDate,
                    Priority = mainTask.Priority,
                    Description = $"Subtask of: {mainTask.TaskName}",
                    CreatedByUserId = createdByUserId,
                    TaskLevel = TaskLevels.Subtask,
                    // Inherited from the parent Main Task, not left null —
                    // OwningDepartment must be populated on every task now,
                    // this is the security boundary Review checks against.
                    OwningDepartment = mainTask.OwningDepartment,
                    ResponsibleAdminUserId = null,
                    ParentTaskId = mainTask.Id
                });
            }

            await _context.SaveChangesAsync();

            if (createdByUserId.HasValue)
            {
                TaskActivityLogger.Log(_context, mainTask.Id, createdByUserId.Value, "created", activityNote);
                await _context.SaveChangesAsync();
            }

            await _notifications.NotifyAdminDirectiveReceivedAsync(spec.ResponsibleAdminUserId!.Value, mainTask.Id, mainTask.TaskName);

            return (mainTask, subtaskNames.Count);
        }
    }
}
