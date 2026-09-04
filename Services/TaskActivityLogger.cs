using DTIOneLink.Data;
using DTIOneLink.Models;

namespace DTIOneLink.Services
{
    public static class TaskActivityLogger
    {
        // Adds to the DbContext's change tracker only — does NOT call
        // SaveChangesAsync itself. Every call site already has its own
        // SaveChangesAsync for the primary change (task edit, progress
        // update, etc.); this rides along in the same transaction so the
        // activity row and the actual change are always saved together
        // or not at all.
    public static void Log(AppDbContext context, int taskId, int performedByUserId, string activityType, string details, int? relatedSubmissionId = null)
{
    context.TaskActivities.Add(new TaskActivity
    {
        TaskId = taskId,
        PerformedByUserId = performedByUserId,
        ActivityType = activityType,
        Details = details,
        OccurredAt = DateTime.UtcNow,
        RelatedSubmissionId = relatedSubmissionId
    });
}
    }
}