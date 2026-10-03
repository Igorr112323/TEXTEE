namespace Visits11.Models;

public sealed class SemesterReport
{
    public string Title { get; set; } = string.Empty;
    public List<string> Headers { get; init; } = new();
    public List<SemesterStudent> Students { get; init; } = new();
}

public sealed class SemesterStudent
{
    public string Name { get; init; } = string.Empty;
    public List<string> Marks { get; init; } = new();
    public string TotalText { get; init; } = string.Empty;
    public string PercentText { get; init; } = string.Empty;
}
