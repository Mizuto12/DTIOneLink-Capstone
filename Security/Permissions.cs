namespace DTIOneLink.Security
{
    // Central catalog of permission strings. Controllers/actions declare
    // which permission they require; RolePermissions decides who has it.
    public static class Permissions
    {
        public const string ManageUserAccounts = "ManageUserAccounts";
        public const string ViewOfficeWideSummaries = "ViewOfficeWideSummaries";
        public const string ManageTasks = "ManageTasks";
        public const string ManageRecords = "ManageRecords";

        // Deliberately separate from ManageRecords — holding this permission
        // is what actually unlocks confidential record content. Office-wide
        // oversight (SuperAdmin) does not imply this by default.
        public const string ViewConfidentialRecords = "ViewConfidentialRecords";
    }
}