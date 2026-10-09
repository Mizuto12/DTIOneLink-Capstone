using DTIOneLink.Security;

namespace DTIOneLink.Tests;

public class RolePermissionsTests
{
    [Theory]
    [InlineData("Admin", Permissions.ManageUserAccounts, true)]
    [InlineData("admin", Permissions.ManageUserAccounts, true)] // case-insensitive role lookup
    [InlineData("ADMIN", Permissions.ManageUserAccounts, true)]
    [InlineData("Admin", Permissions.ManageOfficeWideTasks, false)]
    [InlineData("Admin", Permissions.ViewOfficeWideSummaries, false)]
    public void Has_AdminRole(string role, string permission, bool expected)
    {
        Assert.Equal(expected, RolePermissions.Has(role, permission));
    }

    [Theory]
    [InlineData(Permissions.ManageUserAccounts, true)]
    [InlineData(Permissions.ViewOfficeWideSummaries, true)]
    [InlineData(Permissions.ManageOfficeWideTasks, true)]
    [InlineData(Permissions.ManageTasks, false)] // office-wide task access is its own permission, not plain ManageTasks
    [InlineData(Permissions.ManageRecords, false)]
    [InlineData(Permissions.ViewConfidentialRecords, false)]
    public void Has_SuperAdminRole(string permission, bool expected)
    {
        Assert.Equal(expected, RolePermissions.Has("SuperAdmin", permission));
    }

    [Theory]
    [InlineData(Permissions.ManageTasks, true)] // scoped to own tasks by controller logic, not a free pass
    [InlineData(Permissions.ManageUserAccounts, false)]
    [InlineData(Permissions.ManageOfficeWideTasks, false)]
    [InlineData(Permissions.ManageRecords, false)]
    [InlineData(Permissions.ViewConfidentialRecords, false)]
    public void Has_EmployeeRole_OnlyHoldsItsOwnScopedPermission(string permission, bool expected)
    {
        Assert.Equal(expected, RolePermissions.Has("Employee", permission));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("NotARealRole")]
    [InlineData("Supervisor")] // never an actual stored Role value (see UserManagementController.Roles)
    public void Has_UnknownOrMissingRole_NeverGrantsAnything(string? role)
    {
        Assert.False(RolePermissions.Has(role, Permissions.ManageUserAccounts));
        Assert.False(RolePermissions.Has(role, Permissions.ManageTasks));
    }
}
