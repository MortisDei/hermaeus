using System.Collections.Generic;

namespace Hermaeus.Core.Models;

public sealed record PythonHealthIssue(
    string Code,
    string Message);

public sealed record PythonHealthReport(
    bool IsHealthy,
    string Version,
    IReadOnlyList<PythonHealthIssue> Issues,
    string Summary,
    string Detail,
    string Diagnostics);
