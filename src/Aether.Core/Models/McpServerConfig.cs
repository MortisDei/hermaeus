namespace Aether.Core.Models;

public sealed class McpServerConfig
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "MCP Server";
    public string Command { get; set; } = string.Empty;
    public List<string> Arguments { get; set; } = [];
    public string WorkingDirectory { get; set; } = string.Empty;
    public bool Enabled { get; set; } = true;
}

public sealed class McpSettings
{
    public List<McpServerConfig> Servers { get; set; } = [];
}
