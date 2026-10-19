namespace DTIOneLink.Security
{
    public static class RolePermissions
    {
        private static readonly Dictionary<string, HashSet<string>> Map =
            new(StringComparer.OrdinalIgnoreCase)
        {
            // SuperAdmin: account/role oversight, office-wide summaries, and
            // office-wide task management (create/view/edit/assign across
            // every department). Still no plain ManageTasks — office-wide
            // task access comes exclusively from ManageOfficeWideTasks, kept
            // separate from the department-scoped permission Admin/Supervisor
            // use, so department scoping there is preserved by NOT granting
            // ManageTasks, not by filtering later.
            //
            // SuperAdmin DOES get ViewConfidentialRecords + ApproveRecords
            // (but deliberately NOT ManageRecords, which stays Admin-only):
            // the PD approves records office-wide, including Confidential
            // ones, before they're submitted to the records officer (Admin)
            // for disposal validation. Approving and disposing are kept as
            // separate permissions/roles on purpose, mirroring the real
            // "PD approves, then it's submitted to the records officer" flow
            // — a deliberate change to open approval visibility over the
            // narrower confidentiality boundary this used to enforce.
            ["SuperAdmin"] = new(StringComparer.OrdinalIgnoreCase)
            {
                Permissions.ManageUserAccounts,
                Permissions.ViewOfficeWideSummaries,
                Permissions.ManageOfficeWideTasks,
                Permissions.ViewConfidentialRecords,
                Permissions.ApproveRecords,
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