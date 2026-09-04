namespace DTIOneLink.Security
{
    public static class RolePermissions
    {
        private static readonly Dictionary<string, HashSet<string>> Map =
            new(StringComparer.OrdinalIgnoreCase)
        {
            // SuperAdmin: account/role oversight + office-wide summaries only.
            // No ManageRecords, no ViewConfidentialRecords — confidentiality
            // is preserved by NOT granting these, not by filtering later.
            ["SuperAdmin"] = new(StringComparer.OrdinalIgnoreCase)
            {
                Permissions.ManageUserAccounts,
                Permissions.ViewOfficeWideSummaries,
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