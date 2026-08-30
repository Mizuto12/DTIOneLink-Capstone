using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using DTIOneLink.Models;
using System.Collections.Generic;

namespace DTIOneLink.Controllers
{
    public class RecordsController : Controller
    {
        private readonly string _connectionString;

        public RecordsController(IConfiguration configuration)
        {
            _connectionString = configuration.GetConnectionString("DefaultConnection")
                ?? throw new InvalidOperationException("Connection string 'DefaultConnection' is not configured.");
        }

        [HttpGet]   
        public IActionResult Index()
        {
            var role = HttpContext.Session.GetString("UserRole");

            if (role != "Admin" && role != "Employee")
            {
                return RedirectToAction("Login", "Account");
            }

            return View();
        }

        // GET: /Records/GetAll — loads existing rows when the page first renders
        [HttpGet]
        public IActionResult GetAll()
        {
            var results = new List<RecordEntry>();

            using (var conn = new SqlConnection(_connectionString))
            {
                conn.Open();
                var cmd = new SqlCommand(
                    @"SELECT RecordId, Code, Title, Medium, Location, PeriodCovered,
                             FilingSystem, AccessControl, RetentionPeriod
                      FROM Records
                      ORDER BY RecordId DESC", conn);

                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        results.Add(new RecordEntry
                        {
                            RecordId = reader.GetInt32(0),
                            Code = reader.GetString(1),
                            Title = reader.GetString(2),
                            Medium = reader.GetString(3),
                            Location = reader.GetString(4),
                            PeriodCovered = reader.GetString(5),
                            FilingSystem = reader.GetString(6),
                            AccessControl = reader.GetString(7),
                            RetentionPeriod = reader.GetString(8)
                        });
                    }
                }
            }

            return Json(results);
        }

        // POST: /Records/Save — called by the form's fetch()
        [HttpPost]
        public IActionResult Save([FromBody] RecordEntry entry)
        {
            var role = HttpContext.Session.GetString("UserRole");
            if (role != "Admin" && role != "Employee")
            {
                return Unauthorized();
            }

            if (string.IsNullOrWhiteSpace(entry.Code) || string.IsNullOrWhiteSpace(entry.Title))
            {
                return BadRequest(new { message = "Code and Title are required." });
            }

            try
            {
                using (var conn = new SqlConnection(_connectionString))
                {
                    conn.Open();
                    var cmd = new SqlCommand(
                        @"INSERT INTO Records
                            (Code, Title, Medium, Location, PeriodCovered, FilingSystem, AccessControl, RetentionPeriod, CreatedAt)
                          OUTPUT INSERTED.RecordId
                          VALUES
                            (@Code, @Title, @Medium, @Location, @PeriodCovered, @FilingSystem, @AccessControl, @RetentionPeriod, SYSUTCDATETIME())",
                        conn);

                    cmd.Parameters.AddWithValue("@Code", entry.Code);
                    cmd.Parameters.AddWithValue("@Title", entry.Title);
                    cmd.Parameters.AddWithValue("@Medium", entry.Medium);
                    cmd.Parameters.AddWithValue("@Location", entry.Location);
                    cmd.Parameters.AddWithValue("@PeriodCovered", entry.PeriodCovered);
                    cmd.Parameters.AddWithValue("@FilingSystem", entry.FilingSystem);
                    cmd.Parameters.AddWithValue("@AccessControl", entry.AccessControl);
                    cmd.Parameters.AddWithValue("@RetentionPeriod", entry.RetentionPeriod);

                    var newId = (int)cmd.ExecuteScalar();
                    entry.RecordId = newId;
                }

                return Json(entry);
            }
            catch (SqlException ex) when (ex.Number == 2627 || ex.Number == 2601)
            {
                // Unique constraint violation on Code
                return Conflict(new { message = "That Code already exists." });
            }
        }
    }
}