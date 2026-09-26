using System;
using System.Linq;

namespace DTIOneLink.Services
{
    // Distinguishes a SuperAdmin-issued, department-wide instruction (Main)
    // from an ordinary assignable, employee-level task (Subtask). Both are
    // TaskItem rows in the same table — this just labels which kind a row is.
    public static class TaskLevels
    {
        public const string Main = "main";
        public const string Subtask = "subtask";

        public static string Normalize(string? level) =>
            string.Equals(level, Main, StringComparison.OrdinalIgnoreCase) ? Main : Subtask;
    }

    // The fixed set of departments OPD can target with a Main Task.
 public static class TargetDepartments
    {
        public const string BDD = "Business Development Division";
        // Must match Users.Department exactly (see User Management).
        public const string FAU = "Financial and Administrative Unit";
        public const string CPD = "Consumer Protection Division";
        public static readonly string[] All = { BDD, FAU, CPD };
        public static bool IsValid(string? department) =>
            !string.IsNullOrWhiteSpace(department) &&
            All.Contains(department, StringComparer.OrdinalIgnoreCase);
    }
}