using DTIOneLink.Models;
using DTIOneLink.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;

namespace DTIOneLink.Controllers;

public class UserManagementController(DatabaseHelper db) : Controller
{
    private const string DefaultPassword = "dtionelink2026";

    public async Task<IActionResult> Index()
    {
        var users = new List<UserItem>();
        const string sql = "SELECT Id, FullName, Email, Department, Role, IsActive FROM dbo.Users ORDER BY FullName";

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
                Email = reader.IsDBNull(2) ? "" : reader.GetString(2),
                Department = reader.IsDBNull(3) ? "" : reader.GetString(3),
                Role = reader.GetString(4),
                Status = reader.GetBoolean(5) ? "active" : "disabled"
            });
        }

        return View(users);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(UserItem user)
    {
        if (!ModelState.IsValid)
        {
            TempData["ErrorMessage"] = "Please complete all account fields correctly.";
            return RedirectToAction(nameof(Index));
        }

        var email = user.Email.Trim();
        var passwordHash = new PasswordHasher<object>().HashPassword(null!, DefaultPassword);
        const string sql = @"INSERT INTO dbo.Users
            (Username, PasswordHash, Role, FullName, IsActive, Email, Department, CreatedAt)
            VALUES (@Username, @PasswordHash, @Role, @FullName, 1, @Email, @Department, @CreatedAt)";

        try
        {
            using var conn = db.GetConnection();
            await conn.OpenAsync();
            using var cmd = new SqlCommand(sql, conn);
            cmd.Parameters.AddWithValue("@Username", email);
            cmd.Parameters.AddWithValue("@PasswordHash", passwordHash);
            cmd.Parameters.AddWithValue("@Role", user.Role);
            cmd.Parameters.AddWithValue("@FullName", user.FullName.Trim());
            cmd.Parameters.AddWithValue("@Email", email);
            cmd.Parameters.AddWithValue("@Department", user.Department);
            cmd.Parameters.AddWithValue("@CreatedAt", DateTime.Now);
            await cmd.ExecuteNonQueryAsync();
            TempData["SuccessMessage"] = "Account created. Sign in with the email and default password: dtionelink2026.";
        }
        catch (SqlException ex) when (ex.Number is 2601 or 2627)
        {
            TempData["ErrorMessage"] = "An account with that email already exists.";
        }

        return RedirectToAction(nameof(Index));
    }
}