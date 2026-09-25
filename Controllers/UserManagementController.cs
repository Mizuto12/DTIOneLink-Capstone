using DTIOneLink.Filters;
using DTIOneLink.Models;
using DTIOneLink.Security;
using DTIOneLink.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
namespace DTIOneLink.Controllers;
[RequirePermission(Permissions.ManageUserAccounts)]

public class UserManagementController(DatabaseHelper db, ILogger<UserManagementController> logger) : Controller
{
    private const string DefaultPassword = "dtionelink2026";

    // The only values ChangeStanding accepts (same as the Create form).
    public static readonly string[] Divisions =
    {
        "Office of the Provincial Director",
        "Business Development Division",
        "Consumer Protection Division",
        "Financial and Administrative Unit",
    };

    public static readonly string[] Roles = { "SuperAdmin", "Admin", "Employee" };

    public async Task<IActionResult> Index()
    {
        var users = new List<UserItem>();
        const string sql = "SELECT Id, FullName, Username, Email, Department, Role, IsActive FROM dbo.Users ORDER BY FullName";

        using var conn = db.GetConnection();
        await conn.OpenAsync();
        using var cmd = new SqlCommand(sql, conn);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            users.Add(new UserItem
            {
                Id = reader.GetInt32(0),
                FullName = reader.GetString(1),
                Username = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Email = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Department = reader.IsDBNull(4) ? "" : reader.GetString(4),
                Role = reader.GetString(5),
                Status = reader.GetBoolean(6) ? "active" : "disabled"
            });
        }

        return View(users);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserItem user)
    {
        var email = (user.Email ?? "").Trim();
        // Collapse double spaces so "Juan  Dela Cruz" matches "Juan Dela Cruz".
        var fullName = string.Join(' ', (user.FullName ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries));

        // Keep what was typed so the form doesn't come back empty.
        TempData["FormEmail"] = email;
        TempData["FormFullName"] = fullName;
        TempData["FormDepartment"] = user.Department;
        TempData["FormRole"] = user.Role;

        var role = Roles.FirstOrDefault(r => string.Equals(r, user.Role?.Trim(), StringComparison.OrdinalIgnoreCase));
        var department = Divisions.FirstOrDefault(d => string.Equals(d, user.Department?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (!ModelState.IsValid || role == null || department == null || fullName.Length == 0)
        {
            TempData["ErrorMessage"] = "Please complete all account fields correctly.";
            return RedirectToAction(nameof(Index));
        }

        using var conn = db.GetConnection();
        await conn.OpenAsync();

        // The Users table has no unique rule (older data already has
        // duplicates), so check here before adding. Comparison is
        // case-insensitive (database collation).
        const string duplicateSql = @"
            SELECT TOP 1 FullName, Email, 'email' AS Kind FROM dbo.Users
             WHERE Email = @Email OR Username = @Email
            UNION ALL
            SELECT TOP 1 FullName, Email, 'name' FROM dbo.Users
             WHERE LTRIM(RTRIM(FullName)) = @FullName";
        string? emailOwner = null, nameOwnerEmail = null;
        using (var dup = new SqlCommand(duplicateSql, conn))
        {
            dup.Parameters.AddWithValue("@Email", email);
            dup.Parameters.AddWithValue("@FullName", fullName);
            using var reader = await dup.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                if (reader.GetString(2) == "email") emailOwner = reader.GetString(0);
                else nameOwnerEmail = reader.IsDBNull(1) ? "" : reader.GetString(1);
            }
        }

        if (emailOwner != null || nameOwnerEmail != null)
        {
            if (emailOwner != null)
                TempData["EmailError"] = $"This email is already used by {emailOwner}.";
            if (nameOwnerEmail != null)
                TempData["NameError"] = $"A user named {fullName} already exists ({nameOwnerEmail}).";
            TempData["ErrorMessage"] = "This account already exists. Please check the highlighted field.";
            return RedirectToAction(nameof(Index));
        }

        var passwordHash = new PasswordHasher<object>().HashPassword(null!, DefaultPassword);
        const string sql = @"INSERT INTO dbo.Users
            (Username, PasswordHash, Role, FullName, IsActive, Email, Department, CreatedAt)
            VALUES (@Username, @PasswordHash, @Role, @FullName, 1, @Email, @Department, @CreatedAt)";

        try
        {
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Username", email);
            cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
            cmd.Parameters.AddWithValue("@Role", role);
            cmd.Parameters.AddWithValue("@FullName", fullName);
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@Department", department);
            cmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
            await cmd.ExecuteNonQueryAsync();

            // Saved: clear the kept form values.
            TempData.Remove("FormEmail");
            TempData.Remove("FormFullName");
            TempData.Remove("FormDepartment");
            TempData.Remove("FormRole");
            TempData["SuccessMessage"] = $"Account for {fullName} created. They sign in with their email and the default password: dtionelink2026.";
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            TempData["ErrorMessage"] = "An account with that email already exists.";
        }

        return RedirectToAction(nameof(Index));
    }

    // OPD (SuperAdmin) only: promote/demote a user and/or move them to
    // another division. Admins can open this page but not use this action.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangeStanding(int id, string? role, string? department)
    {
        if (!string.Equals(HttpContext.Session.GetString("UserRole"), "SuperAdmin", StringComparison.OrdinalIgnoreCase))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        var newRole = Roles.FirstOrDefault(r => string.Equals(r, role?.Trim(), StringComparison.OrdinalIgnoreCase));
        var newDepartment = Divisions.FirstOrDefault(d => string.Equals(d, department?.Trim(), StringComparison.OrdinalIgnoreCase));
        if (newRole == null || newDepartment == null)
        {
            TempData["DirectoryError"] = "Please choose a valid role and division.";
            return RedirectToAction(nameof(Index));
        }

        // Prevents the OPD from locking themselves out by accident.
        if (HttpContext.Session.GetInt32("UserId") == id)
        {
            TempData["DirectoryError"] = "You can't change your own role or division.";
            return RedirectToAction(nameof(Index));
        }

        using var conn = db.GetConnection();
        await conn.OpenAsync();

        string fullName, oldRole, oldDepartment;
        using (var find = new SqlCommand("SELECT FullName, Role, Department FROM dbo.Users WHERE Id = @Id", conn))
        {
            find.Parameters.AddWithValue("@Id", id);
            using var reader = await find.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                TempData["DirectoryError"] = "That account no longer exists.";
                return RedirectToAction(nameof(Index));
            }
            fullName = reader.GetString(0);
            oldRole = reader.GetString(1);
            oldDepartment = reader.IsDBNull(2) ? "" : reader.GetString(2);
        }

        if (string.Equals(oldRole, newRole, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(oldDepartment, newDepartment, StringComparison.OrdinalIgnoreCase))
        {
            TempData["DirectoryMessage"] = $"No changes made to {fullName}.";
            return RedirectToAction(nameof(Index));
        }

        using (var update = new SqlCommand("UPDATE dbo.Users SET Role = @Role, Department = @Department WHERE Id = @Id", conn))
        {
            update.Parameters.AddWithValue("@Role", newRole);
            update.Parameters.AddWithValue("@Department", newDepartment);
            update.Parameters.AddWithValue("@Id", id);
            await update.ExecuteNonQueryAsync();
        }

        logger.LogInformation("User {UserId} changed from {OldRole}/{OldDepartment} to {NewRole}/{NewDepartment} by SuperAdmin {ActorId}.",
            id, oldRole, oldDepartment, newRole, newDepartment, HttpContext.Session.GetInt32("UserId"));

        // Existing tasks are left as they are; just tell the OPD if some
        // unfinished work may need to be reassigned.
        const string openWorkSql = @"
            SELECT COUNT(*) FROM dbo.TaskItems t
            WHERE t.Status <> 'completed'
              AND (t.ResponsibleAdminUserId = @Id
                   OR EXISTS (SELECT 1 FROM dbo.TaskAssignments a
                              WHERE a.TaskId = t.Id AND a.UserId = @Id AND a.Status <> 'completed'))";
        using var countCmd = new SqlCommand(openWorkSql, conn);
        countCmd.Parameters.AddWithValue("@Id", id);
        var openWork = Convert.ToInt32(await countCmd.ExecuteScalarAsync());

        TempData["DirectoryMessage"] = $"{fullName} is now {RoleLabel(newRole)} in {newDepartment}."
            + (openWork > 0
                ? $" They still have {openWork} unfinished task(s) from before — reassign them if needed."
                : "");
        return RedirectToAction(nameof(Index));
    }

    public static string RoleLabel(string? role) => role switch
    {
        "SuperAdmin" => "Super Admin",
        "Admin" => "Admin",
        _ => "Employee"
    };
}
