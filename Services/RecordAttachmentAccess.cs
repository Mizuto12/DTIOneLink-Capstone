namespace DTIOneLink.Services
{
    // Who may upload/delete an attachment on a record, and (same rule,
    // shared) who may edit or dispose of the record itself — only the
    // record's own creator, for every role, matching VisibilityClause.
    // Extracted into its own class so it's unit-testable without a live
    // SQL Server connection, the same reason RecordDisposalWorkflow exists
    // separately from RecordsController instead of staying inline as
    // private methods.
    public static class RecordAttachmentAccess
    {
        public static bool CanManage(int? recordCreatedByUserId, int userId) =>
            recordCreatedByUserId == userId;
    }
}
