namespace Aether.Agent.Services;

internal static class WorkspaceCommandRecipes
{
    public static readonly IReadOnlyDictionary<string, (string FileName, string[] Args)> Executable =
        new Dictionary<string, (string, string[])>(StringComparer.OrdinalIgnoreCase)
        {
            ["dotnet build"] = ("dotnet", ["build"]),
            ["dotnet test"] = ("dotnet", ["test"]),
            ["npm test"] = ("npm", ["test"]),
            ["cargo test"] = ("cargo", ["test"]),
            ["pytest"] = ("pytest", [])
        };
}
