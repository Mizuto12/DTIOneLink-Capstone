namespace DTIOneLink.Services
{
    // Who may upload/delete an attachment on a record, and (same rule,
    // shared) who may edit or dispose of the record itself — a records
    // manager, or the record's own creator. Extracted into its own class
    // so it's unit-testable without a live SQL Server connection, the
    // same reason RecordDisposalWorkflow exists separately from
    // RecordsController instead of staying inline as private methods.
    public static class RecordAttachmentAccess
    {
        public static bool CanManage(bool userCanManage, int? recordCreatedByUserId, int userId) =>
            userCanManage || recordCreatedByUserId == userId;
    }
}
