using DTIOneLink.Services;

namespace DTIOneLink.Tests;

public class TaskTypesTests
{
    [Theory]
    [InlineData(TaskTypes.DirectAdmin, true)]
    [InlineData(TaskTypes.DepartmentDirective, true)]
    [InlineData(TaskTypes.WholeOffice, true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("something-else", false)]
    public void IsValid(string? taskType, bool expected)
    {
        Assert.Equal(expected, TaskTypes.IsValid(taskType));
    }

    [Theory]
    [InlineData(TaskTypes.DirectAdmin, true)]
    [InlineData(TaskTypes.WholeOffice, true)]
    [InlineData(TaskTypes.DepartmentDirective, false)] // goes through the department's Admin instead
    [InlineData(null, false)]
    public void IsAssignedByOpd(string? taskType, bool expected)
    {
        Assert.Equal(expected, TaskTypes.IsAssignedByOpd(taskType));
    }
}

public class TaskLevelsTests
{
    [Theory]
    [InlineData("main", TaskLevels.Main)]
    [InlineData("Main", TaskLevels.Main)]
    [InlineData("MAIN", TaskLevels.Main)]
    [InlineData("subtask", TaskLevels.Subtask)]
    [InlineData(null, TaskLevels.Subtask)]
    [InlineData("", TaskLevels.Subtask)]
    [InlineData("anything-else", TaskLevels.Subtask)]
    public void Normalize(string? level, string expected)
    {
        Assert.Equal(expected, TaskLevels.Normalize(level));
    }
}

public class TargetDepartmentsTests
{
    [Theory]
    [InlineData(TargetDepartments.BDD, true)]
    [InlineData(TargetDepartments.FAU, true)]
    [InlineData(TargetDepartments.CPD, true)]
    [InlineData("business development division", true)] // case-insensitive
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("Office of the Provincial Director", false)] // OPD itself is not a targetable department
    public void IsValid(string? department, bool expected)
    {
        Assert.Equal(expected, TargetDepartments.IsValid(department));
    }
}
