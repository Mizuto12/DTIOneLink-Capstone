namespace DTIOneLink.Security
{
    public static class RolePermissions
    {
        private static readonly Dictionary<string, HashSet<string>> Map =
            new(StringComparer.OrdinalIgnoreCase)
        {
            // SuperAdmin: account/role oversight, office-wide summaries, and
            // office-wide task management (create/view/edit/assign across
            // every department). Still no ManageRecords, no
            // ViewConfidentialRecords, and no plain ManageTasks — office-wide
            // task access comes exclusively from ManageOfficeWideTasks, kept
            // separate from the department-scoped permission Admin/Supervisor
            // use, so confidentiality and department scoping are preserved
            // by NOT granting those, not by filtering later.
            ["SuperAdmin"] = new(StringComparer.OrdinalIgnoreCase)
            {
                Permissions.ManageUserAccounts,
                Permissions.ViewOfficeWideSummaries,
                Permissions.ManageOfficeWideTasks,
            },

            ["Admin"] = new(StringComparer.OrdinalIgnoreCase)
            {
                Permissions.ManageTasks,
                Permissions.ManageRecords,
                Permissions.ViewConfidentialRecords,
                Permissions.ManageUserAccounts
            },

            ["Employee"] = new(StringComparer.OrdinalIgnoreCase)
            {
                Permissions.ManageTasks, // scoped to own tasks by controller query logic
            },
        };

        public static bool Has(string? role, string permission)
        {
            if (string.IsNullOrWhiteSpace(role)) return false;
            return Map.TryGetValue(role, out var perms) && perms.Contains(permission);
        }
    }
}