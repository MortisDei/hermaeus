namespace Hermaeus.Core.Models;

public sealed class McpServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "MCP Server";
    public string Command { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = [];
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Restricts which of the server's declared tools can actually be
    /// called (docs/review/03-next-level-roadmap.md Phase 3). Empty means no
    /// restriction: every tool the server declares via <c>tools/list</c> is
    /// callable, matching prior behavior. A server's own tool list is
    /// prompt-injection attack surface (a compromised or malicious server can
    /// declare more tools than the user reviewed when they configured it), so
    /// this lets a user narrow a server down to only the tools they actually
    /// intend to use.
    /// </summary>
    public List<string> AllowedTools { get; set; } = [];
}

public sealed class McpSettings
{
    public List<McpServerConfig> Servers { get; set; } = [];
}
