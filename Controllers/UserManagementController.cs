using DTIOneLink.Filters;
using DTIOneLink.Models;
using DTIOneLink.Security;
using DTIOneLink.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
namespace DTIOneLink.Controllers;
[RequirePermission(Permissions.ManageUserAccounts)]

public class UserManagementController(DatabaseHelper db, OneTimeCodeService codes, ILogger<UserManagementController> logger) : Controller
{
    // The only values ChangeStanding accepts (same as the Create form).
    public static readonly string[] Divisions =
    {
        "Office of the Provincial Director",
        "Business Development Division",
        "Consumer Protection Division",
        "Financial and Administrative Unit",
    };

    public static readonly string[] Roles = { "SuperAdmin", "Admin", "Employee" };

    // Explicit project rule, same as the password rule in AccountController:
    // no whitespace anywhere and no emoji/non-ASCII. [EmailAddress] alone
    // doesn't guarantee this — .NET's implementation is lenient enough that
    // some whitespace/Unicode can still pass it. '@' and '.' both fall
    // inside this printable-ASCII range already, so real addresses are
    // unaffected.
    private static bool IsAsciiPrintableNoWhitespace(string value) =>
        value.Length > 0 && value.All(c => c is >= '!' and <= '~');

    public async Task<IActionResult> Index()
    {
        var users = new List<UserItem>();
        const string sql = "SELECT Id, FullName, Username, Email, Department, Role, IsActive, LastLoginAtUtc, LastLogoutAtUtc, LastSeenAtUtc, ActivatedAtUtc FROM dbo.Users ORDER BY FullName";

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
                Status = reader.GetBoolean(6) ? "active" : "disabled",
                LastLoginAtUtc = reader.IsDBNull(7) ? null : reader.GetDateTime(7),
                LastLogoutAtUtc = reader.IsDBNull(8) ? null : reader.GetDateTime(8),
                LastSeenAtUtc = reader.IsDBNull(9) ? null : reader.GetDateTime(9),
                ActivatedAtUtc = reader.IsDBNull(10) ? null : reader.GetDateTime(10)
            });
        }
        await reader.CloseAsync();

        // Unfinished tasks per person (same rule as CountOpenWorkAsync below),
        // so the Change Role pop-up can warn before saving. Includes a
        // repeating task's latest copy even when it's already Completed —
        // see CountOpenWorkAsync for why that still counts as open work.
        const string openWorkSql = @"
            SELECT u.Id, COUNT(t.Id)
            FROM dbo.Users u
            JOIN dbo.TaskItems t ON
                (t.Status <> 'completed'
                 AND (t.ResponsibleAdminUserId = u.Id
                      OR EXISTS (SELECT 1 FROM dbo.TaskAssignments a
                                 WHERE a.TaskId = t.Id AND a.UserId = u.Id AND a.Status <> 'completed')))
                OR (t.Recurrence IS NOT NULL AND t.ResponsibleAdminUserId = u.Id)
            GROUP BY u.Id";
        var openTasks = new Dictionary<int, int>();
        using (var openCmd = new SqlCommand(openWorkSql, conn))
        using (var openReader = await openCmd.ExecuteReaderAsync())
        {
            while (await openReader.ReadAsync())
            {
                openTasks[openReader.GetInt32(0)] = openReader.GetInt32(1);
            }
        }
        ViewData["OpenTasks"] = openTasks;

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
        if (!IsAsciiPrintableNoWhitespace(email))
        {
            TempData["ErrorMessage"] = "Email cannot contain spaces or emoji.";
            return RedirectToAction(nameof(Index));
        }

        var actorRole = HttpContext.Session.GetString("UserRole");
        var actorIsSuperAdmin = string.Equals(actorRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase);

        // Only the OPD (SuperAdmin) may hand out SuperAdmin or Admin rights.
        // A plain Admin reaches this same action and could otherwise grant
        // themselves (or anyone) office-wide access by posting Role=SuperAdmin.
        if (role != "Employee" && !actorIsSuperAdmin)
        {
            TempData["ErrorMessage"] = "Only the OPD can create Admin or SuperAdmin accounts.";
            return RedirectToAction(nameof(Index));
        }

        // An Admin only manages their own division (same scope CanSetActive
        // already enforces for status/role changes), so they can only add
        // employees into it — not post an arbitrary Division value.
        if (!actorIsSuperAdmin
            && !string.Equals(department, HttpContext.Session.GetString("UserDepartment"), StringComparison.OrdinalIgnoreCase))
        {
            TempData["ErrorMessage"] = "You can only add employees to your own division.";
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

        // No real password is set yet — a random value nobody is ever told,
        // so the account literally cannot be signed into (defense in depth
        // alongside the ActivatedAtUtc gate in AccountController.Login).
        // They become the owner of the account by clicking the emailed
        // activation link and choosing their own password.
        var unusablePassword = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var passwordHash = new PasswordHasher<object>().HashPassword(null!, unusablePassword);
        const string sql = @"INSERT INTO dbo.Users
            (Username, PasswordHash, Role, FullName, IsActive, Email, Department, CreatedAt,
             EmailConfirmed, MustChangePassword, SecurityStamp, ActivatedAtUtc)
            OUTPUT INSERTED.Id
            VALUES (@Username, @PasswordHash, @Role, @FullName, 1, @Email, @Department, @CreatedAt,
             0, 1, @SecurityStamp, NULL)";

        try
        {
            int newUserId;
            using (var cmd = new SqlCommand(sql, conn))
            {
                cmd.Parameters.AddWithValue("@Username", email);
                cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
                cmd.Parameters.AddWithValue("@Role", role);
                cmd.Parameters.AddWithValue("@FullName", fullName);
                cmd.Parameters.AddWithValue("@Email", email);
                cmd.Parameters.AddWithValue("@Department", department);
                cmd.Parameters.AddWithValue("@CreatedAt", DateTime.UtcNow);
                cmd.Parameters.AddWithValue("@SecurityStamp", Guid.NewGuid().ToString("N"));
                newUserId = (int)(await cmd.ExecuteScalarAsync())!;
            }

            await codes.IssueLinkAsync(new User { Id = newUserId, Email = email, FullName = fullName },
                OneTimeCodePurpose.AccountActivation,
                token => Url.Action("Activate", "Account", new { uid = newUserId, token }, Request.Scheme)!);

            // Saved: clear the kept form values.
            TempData.Remove("FormEmail");
            TempData.Remove("FormFullName");
            TempData.Remove("FormDepartment");
            TempData.Remove("FormRole");
            TempData["SuccessMessage"] = $"Account for {fullName} created. We emailed them an activation link to {email} — it's valid for 24 hours and lets them choose their own password.";
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            TempData["ErrorMessage"] = "An account with that email already exists.";
        }

        return RedirectToAction(nameof(Index));
    }

    // Admin-triggered resend: issues a fresh activation link, which
    // invalidates any earlier unused one for the same account (same
    // cancel-the-previous-one rule OneTimeCodeService already applies to
    // every other code/link it issues) — only one activation link is ever
    // live at a time.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResendActivation(int id)
    {
        var actorRole = HttpContext.Session.GetString("UserRole");
        var actorDepartment = HttpContext.Session.GetString("UserDepartment");
        var actorId = HttpContext.Session.GetInt32("UserId");

        using var conn = db.GetConnection();
        await conn.OpenAsync();

        DateTime? activatedAtUtc;
        string fullName, role, department, email;
        using (var find = new SqlCommand(
            "SELECT FullName, Role, Department, Email, ActivatedAtUtc FROM dbo.Users WHERE Id = @Id", conn))
        {
            find.Parameters.AddWithValue("@Id", id);
            using var reader = await find.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                TempData["DirectoryError"] = "That account no longer exists.";
                return RedirectToAction(nameof(Index));
            }
            fullName = reader.GetString(0);
            role = reader.GetString(1);
            department = reader.IsDBNull(2) ? "" : reader.GetString(2);
            email = reader.IsDBNull(3) ? "" : reader.GetString(3);
            activatedAtUtc = reader.IsDBNull(4) ? null : reader.GetDateTime(4);
        }

        if (!CanSetActive(actorRole, actorDepartment, actorId, id, role, department))
        {
            TempData["DirectoryError"] = "You can only resend an activation link for employees of your own division.";
            return RedirectToAction(nameof(Index));
        }
        if (activatedAtUtc != null)
        {
            TempData["DirectoryMessage"] = $"{fullName}'s account is already active — there's no activation link to resend.";
            return RedirectToAction(nameof(Index));
        }

        var issued = await codes.IssueLinkAsync(new User { Id = id, Email = email, FullName = fullName },
            OneTimeCodePurpose.AccountActivation,
            token => Url.Action("Activate", "Account", new { uid = id, token }, Request.Scheme)!);

        logger.LogInformation("Activation link resent for user {UserId} by {ActorRole} {ActorId}.", id, actorRole, actorId);

        TempData["HighlightUserId"] = id;
        TempData["DirectoryMessage"] = issued.Status == CodeIssueStatus.Sent
            ? $"A new activation link was emailed to {fullName}. The old link no longer works."
            : $"Please wait before resending {fullName}'s activation link — one was sent too recently.";
        return RedirectToAction(nameof(Index));
    }

    // Fallback for a pending account whose activation link genuinely can't
    // reach them (wrong address, broken email, nothing Resend Link can fix):
    // an Admin hands out a one-time temporary password through a channel
    // the system doesn't control — in person or by phone — the same way
    // ResetPassword below already does for an existing account's forgotten
    // password. Security properties this preserves, deliberately:
    //   - Same authorization scope as every other sensitive account action
    //     (CanSetActive): an Admin can only do this for their own division.
    //   - The password is cryptographically random, shown once on screen,
    //     and never logged — only the fact that it was issued is.
    //   - Any outstanding activation link is invalidated in the same
    //     action, so at most one "become this account" credential is ever
    //     live at a time.
    //   - MustChangePassword and EmailConfirmed are untouched (both stay as
    //     they were — false/true respectively, same as account creation),
    //     so the temporary password still has to pass through VerifyEmail
    //     and SetPassword on first use exactly like the old shared-default-
    //     password flow did. It cannot grant normal access by itself.
    //   - Only usable while the account is still pending: once activated,
    //     this isn't the right tool — ResetPassword is.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> IssueActivationFallbackPassword(int id)
    {
        var actorRole = HttpContext.Session.GetString("UserRole");
        var actorDepartment = HttpContext.Session.GetString("UserDepartment");
        var actorId = HttpContext.Session.GetInt32("UserId");

        using var conn = db.GetConnection();
        await conn.OpenAsync();

        DateTime? activatedAtUtc;
        string fullName, role, department;
        using (var find = new SqlCommand(
            "SELECT FullName, Role, Department, ActivatedAtUtc FROM dbo.Users WHERE Id = @Id", conn))
        {
            find.Parameters.AddWithValue("@Id", id);
            using var reader = await find.ExecuteReaderAsync();
            if (!await reader.ReadAsync())
            {
                TempData["DirectoryError"] = "That account no longer exists.";
                return RedirectToAction(nameof(Index));
            }
            fullName = reader.GetString(0);
            role = reader.GetString(1);
            department = reader.IsDBNull(2) ? "" : reader.GetString(2);
            activatedAtUtc = reader.IsDBNull(3) ? null : reader.GetDateTime(3);
        }

        if (!CanSetActive(actorRole, actorDepartment, actorId, id, role, department))
        {
            TempData["DirectoryError"] = "You can only issue a temporary password for employees of your own division.";
            return RedirectToAction(nameof(Index));
        }
        if (activatedAtUtc != null)
        {
            TempData["DirectoryMessage"] = $"{fullName}'s account is already active — use Reset Password instead.";
            return RedirectToAction(nameof(Index));
        }

        var temporaryPassword = NewTemporaryPassword();
        const string sql = @"UPDATE dbo.Users
            SET PasswordHash = @Hash, ActivatedAtUtc = @ActivatedAtUtc, FailedLoginCount = 0,
                LockoutEndUtc = NULL, SecurityStamp = @Stamp
            WHERE Id = @Id";
        using (var update = new SqlCommand(sql, conn))
        {
            update.Parameters.AddWithValue("@Hash", new PasswordHasher<object>().HashPassword(null!, temporaryPassword));
            update.Parameters.AddWithValue("@ActivatedAtUtc", DateTime.UtcNow);
            update.Parameters.AddWithValue("@Stamp", Guid.NewGuid().ToString("N"));
            update.Parameters.AddWithValue("@Id", id);
            await update.ExecuteNonQueryAsync();
        }

        // Only one path to sign in as this account should ever be live —
        // the emailed link, if one is still outstanding, no longer is.
        await codes.InvalidateAsync(id, OneTimeCodePurpose.AccountActivation);

        // Audit event: who, which account, when — deliberately never the
        // password value itself.
        logger.LogInformation("Temporary activation password issued for pending account {UserId} by {ActorRole} {ActorId}.",
            id, actorRole, actorId);

        TempData["HighlightUserId"] = id;
        TempData["TempPasswordFor"] = fullName;
        TempData["TempPassword"] = temporaryPassword;
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

        // Their current work belongs to their current role/division, so it
        // must be finished or reassigned first.
        var (openWork, hasLiveRecurrence) = await CountOpenWorkAsync(conn, id);
        if (openWork > 0)
        {
            TempData["DirectoryError"] = hasLiveRecurrence
                ? $"{fullName}'s role or division can't be changed yet. They're still the Responsible Admin for a repeating task — reassign or stop the repeat first, even if its latest copy shows Completed."
                : $"{fullName}'s role or division can't be changed yet. They still have {openWork} unfinished task(s). Reassign or finish them first.";
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

        TempData["DirectoryMessage"] = $"{fullName} is now {RoleLabel(newRole)} in {newDepartment}.";
        return RedirectToAction(nameof(Index));
    }

    // Unfinished tasks a person is still on: assigned and not yet completed,
    // a directive they lead as Responsible Admin, OR a repeating task's
    // latest copy that still names them Responsible Admin — counted even if
    // that copy itself is already Completed, since the repeat only rolls to
    // its next copy when the DUE DATE arrives (RecurringTaskService), not
    // when the copy is completed early. Without this, an Admin could be
    // deactivated or reassigned while still "armed" to receive the next
    // copy, and RecurringTaskService would then silently skip it forever
    // (it only logs a warning — nobody is watching server logs here) the
    // next time it came due. Same rule as Index.
    private static async Task<(int Count, bool HasLiveRecurrence)> CountOpenWorkAsync(SqlConnection conn, int userId)
    {
        const string openWorkSql = @"
            SELECT
                (SELECT COUNT(*) FROM dbo.TaskItems t
                 WHERE (t.Status <> 'completed'
                        AND (t.ResponsibleAdminUserId = @Id
                             OR EXISTS (SELECT 1 FROM dbo.TaskAssignments a
                                        WHERE a.TaskId = t.Id AND a.UserId = @Id AND a.Status <> 'completed')))
                    OR (t.Recurrence IS NOT NULL AND t.ResponsibleAdminUserId = @Id)),
                CAST(CASE WHEN EXISTS (
                    SELECT 1 FROM dbo.TaskItems t2
                    WHERE t2.Recurrence IS NOT NULL AND t2.ResponsibleAdminUserId = @Id
                ) THEN 1 ELSE 0 END AS BIT)";
        using var countCmd = new SqlCommand(openWorkSql, conn);
        countCmd.Parameters.AddWithValue("@Id", userId);
        using var reader = await countCmd.ExecuteReaderAsync();
        await reader.ReadAsync();
        return (reader.GetInt32(0), reader.GetBoolean(1));
    }

    // Who may deactivate/reactivate whom. Super Admin: anyone but
    // themselves. Admin (Division Chief): only Employees of their own
    // division. Used by the view (buttons) and SetActive (enforcement).
    public static bool CanSetActive(string? actorRole, string? actorDepartment, int? actorId,
        int targetId, string? targetRole, string? targetDepartment)
    {
        if (actorId == null || actorId == targetId) return false;
        if (string.Equals(actorRole, "SuperAdmin", StringComparison.OrdinalIgnoreCase)) return true;
        return string.Equals(actorRole, "Admin", StringComparison.OrdinalIgnoreCase)
            && string.Equals(targetRole, "Employee", StringComparison.OrdinalIgnoreCase)
            && !string.IsNullOrWhiteSpace(actorDepartment)
            && string.Equals(targetDepartment, actorDepartment, StringComparison.OrdinalIgnoreCase);
    }

    // Deactivate (active = false) or reactivate (active = true) an account.
    // A deactivated person can't sign in, and anyone already signed in is
    // signed out on their next page (see Program.cs). Nothing is deleted:
    // their tasks, records and history stay.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetActive(int id, bool active)
    {
        var actorRole = HttpContext.Session.GetString("UserRole");
        var actorDepartment = HttpContext.Session.GetString("UserDepartment");
        var actorId = HttpContext.Session.GetInt32("UserId");

        using var conn = db.GetConnection();
        await conn.OpenAsync();

        string fullName, targetRole, targetDepartment;
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
            targetRole = reader.GetString(1);
            targetDepartment = reader.IsDBNull(2) ? "" : reader.GetString(2);
        }

        if (!CanSetActive(actorRole, actorDepartment, actorId, id, targetRole, targetDepartment))
        {
            TempData["DirectoryError"] = actorId == id
                ? "You can't deactivate your own account."
                : "You can only deactivate or reactivate employees of your own division.";
            return RedirectToAction(nameof(Index));
        }

        // Reactivating is always fine; deactivating would strand their work.
        if (!active)
        {
            var (openWork, hasLiveRecurrence) = await CountOpenWorkAsync(conn, id);
            if (openWork > 0)
            {
                TempData["DirectoryError"] = hasLiveRecurrence
                    ? $"{fullName}'s account can't be deactivated yet. They're still the Responsible Admin for a repeating task — reassign or stop the repeat first, even if its latest copy shows Completed."
                    : $"{fullName}'s account can't be deactivated yet. They still have {openWork} unfinished task(s). Reassign or finish them first.";
                // Sent straight to their open work (e.g. an AWOL employee) so
                // the admin doesn't have to hunt for which tasks to reassign.
                TempData["DirectoryErrorTaskLink"] = Url.Action("Index", "Tasks", new { employeeId = id });
                TempData["DirectoryErrorTaskLinkLabel"] = $"View {fullName}'s tasks";
                return RedirectToAction(nameof(Index));
            }
        }

        using (var update = new SqlCommand("UPDATE dbo.Users SET IsActive = @Active WHERE Id = @Id", conn))
        {
            update.Parameters.AddWithValue("@Active", active);
            update.Parameters.AddWithValue("@Id", id);
            await update.ExecuteNonQueryAsync();
        }

        logger.LogInformation("User {UserId} {Action} by {ActorRole} {ActorId}.",
            id, active ? "reactivated" : "deactivated", actorRole, actorId);

        TempData["HighlightUserId"] = id;
        TempData["DirectoryMessage"] = active
            ? $"{fullName}'s account is active again. They can sign in with their usual password."
            : $"{fullName}'s account is deactivated. They can no longer sign in. Their past tasks and records are kept.";
        return RedirectToAction(nameof(Index));
    }

    // For someone who forgot their password and can't use Forgot Password
    // (email never confirmed, or email not arriving). Sets a random one-time
    // temporary password, shown once to the admin to hand over in person —
    // never the shared default, which everyone knows. The person must choose
    // their own password when they next sign in, and any session they have
    // open is signed out. Same who-may-manage-whom rule as deactivation.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(int id)
    {
        var actorRole = HttpContext.Session.GetString("UserRole");
        var actorDepartment = HttpContext.Session.GetString("UserDepartment");
        var actorId = HttpContext.Session.GetInt32("UserId");

        using var conn = db.GetConnection();
        await conn.OpenAsync();

        var target = await LoadAccountAsync(conn, id);
        if (target == null)
        {
            TempData["DirectoryError"] = "That account no longer exists.";
            return RedirectToAction(nameof(Index));
        }
        if (!CanSetActive(actorRole, actorDepartment, actorId, id, target.Role, target.Department))
        {
            TempData["DirectoryError"] = actorId == id
                ? "To change your own password, sign out and use Forgot Password on the sign-in page."
                : "You can only reset the password of employees of your own division.";
            return RedirectToAction(nameof(Index));
        }

        var temporaryPassword = NewTemporaryPassword();
        const string sql = @"UPDATE dbo.Users
            SET PasswordHash = @Hash, MustChangePassword = 1, FailedLoginCount = 0,
                LockoutEndUtc = NULL, SecurityStamp = @Stamp
            WHERE Id = @Id";
        using (var update = new SqlCommand(sql, conn))
        {
            update.Parameters.AddWithValue("@Hash", new PasswordHasher<object>().HashPassword(null!, temporaryPassword));
            update.Parameters.AddWithValue("@Stamp", Guid.NewGuid().ToString("N"));
            update.Parameters.AddWithValue("@Id", id);
            await update.ExecuteNonQueryAsync();
        }

        logger.LogInformation("Password of user {UserId} reset to a temporary password by {ActorRole} {ActorId}.",
            id, actorRole, actorId);

        TempData["HighlightUserId"] = id;
        TempData["TempPasswordFor"] = target.FullName;
        TempData["TempPassword"] = temporaryPassword;
        return RedirectToAction(nameof(Index));
    }

    // Corrects a mistyped email. The new address must be confirmed again with
    // a code at the person's next sign-in before any code or notice is sent
    // to it, and they are signed out now. Their login name follows the email
    // when it was the same as the old email.
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditEmail(int id, string? email)
    {
        var actorRole = HttpContext.Session.GetString("UserRole");
        var actorDepartment = HttpContext.Session.GetString("UserDepartment");
        var actorId = HttpContext.Session.GetInt32("UserId");
        var newEmail = (email ?? "").Trim();

        using var conn = db.GetConnection();
        await conn.OpenAsync();

        var target = await LoadAccountAsync(conn, id);
        if (target == null)
        {
            TempData["DirectoryError"] = "That account no longer exists.";
            return RedirectToAction(nameof(Index));
        }
        if (!CanSetActive(actorRole, actorDepartment, actorId, id, target.Role, target.Department))
        {
            TempData["DirectoryError"] = actorId == id
                ? "You can't change your own email here."
                : "You can only change the email of employees of your own division.";
            return RedirectToAction(nameof(Index));
        }
        if (newEmail.Length == 0 || newEmail.Length > 256 || !IsAsciiPrintableNoWhitespace(newEmail)
            || !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(newEmail))
        {
            TempData["DirectoryError"] = "Please enter a valid email address — no spaces or emoji.";
            return RedirectToAction(nameof(Index));
        }
        if (string.Equals(newEmail, target.Email, StringComparison.OrdinalIgnoreCase))
        {
            TempData["DirectoryMessage"] = $"{target.FullName}'s email is already {newEmail}. Nothing was changed.";
            return RedirectToAction(nameof(Index));
        }

        using (var dup = new SqlCommand(
            "SELECT TOP 1 FullName FROM dbo.Users WHERE Id <> @Id AND (Email = @Email OR Username = @Email)", conn))
        {
            dup.Parameters.AddWithValue("@Id", id);
            dup.Parameters.AddWithValue("@Email", newEmail);
            if (await dup.ExecuteScalarAsync() is string owner)
            {
                TempData["DirectoryError"] = $"This email is already used by {owner}.";
                return RedirectToAction(nameof(Index));
            }
        }

        const string sql = @"UPDATE dbo.Users
            SET Email = @Email,
                Username = CASE WHEN Username = @OldEmail THEN @Email ELSE Username END,
                EmailConfirmed = 0, SecurityStamp = @Stamp
            WHERE Id = @Id;
            UPDATE dbo.OneTimeCodes SET ConsumedAtUtc = SYSUTCDATETIME()
            WHERE UserId = @Id AND ConsumedAtUtc IS NULL;";
        using (var update = new SqlCommand(sql, conn))
        {
            update.Parameters.AddWithValue("@Email", newEmail);
            update.Parameters.AddWithValue("@OldEmail", target.Email);
            update.Parameters.AddWithValue("@Stamp", Guid.NewGuid().ToString("N"));
            update.Parameters.AddWithValue("@Id", id);
            await update.ExecuteNonQueryAsync();
        }

        logger.LogInformation("Email of user {UserId} changed by {ActorRole} {ActorId}.", id, actorRole, actorId);

        TempData["HighlightUserId"] = id;
        TempData["DirectoryMessage"] = $"{target.FullName}'s email is now {newEmail}. They log in with it and confirm it with a code at their next sign-in.";
        return RedirectToAction(nameof(Index));
    }

    private record AccountInfo(string FullName, string Role, string Department, string Email);

    private static async Task<AccountInfo?> LoadAccountAsync(SqlConnection conn, int id)
    {
        using var find = new SqlCommand("SELECT FullName, Role, Department, Email FROM dbo.Users WHERE Id = @Id", conn);
        find.Parameters.AddWithValue("@Id", id);
        using var reader = await find.ExecuteReaderAsync();
        if (!await reader.ReadAsync()) return null;
        return new AccountInfo(
            reader.GetString(0),
            reader.GetString(1),
            reader.IsDBNull(2) ? "" : reader.GetString(2),
            reader.IsDBNull(3) ? "" : reader.GetString(3));
    }

    // 12 random characters in groups of four, e.g. "Kp7m-Qx4r-9tWz".
    // Look-alike characters (0/O, 1/l/I) are left out so it can be read aloud
    // or copied from paper without mistakes.
    private static string NewTemporaryPassword()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnpqrstuvwxyz23456789";
        var chars = new char[14];
        for (var i = 0; i < chars.Length; i++)
        {
            chars[i] = i is 4 or 9 ? '-' : alphabet[System.Security.Cryptography.RandomNumberGenerator.GetInt32(alphabet.Length)];
        }
        return new string(chars);
    }

    public static string RoleLabel(string? role) => role switch
    {
        "SuperAdmin" => "Super Admin",
        "Admin" => "Admin",
        _ => "Employee"
    };
}
