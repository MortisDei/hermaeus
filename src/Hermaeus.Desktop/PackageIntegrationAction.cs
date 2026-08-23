using System.Diagnostics;

namespace Hermaeus.Desktop;

internal enum PackageIntegrationActionKind
{
    Install,
    Uninstall
}

internal sealed record PackageIntegrationLaunch(
    PackageIntegrationActionKind Action,
    string PackageRoot,
    string ScriptPath,
    bool CanRun = true);

internal sealed record PackageIntegrationResult(bool Success, string Detail);

internal static class PackageIntegrationAction
{
    internal static PackageIntegrationLaunch? Resolve(string? processPath)
    {
        if (string.IsNullOrWhiteSpace(processPath))
            return null;

        var action = Path.GetFileName(processPath) switch
        {
            "install-hermaeus" => PackageIntegrationActionKind.Install,
            "uninstall-hermaeus" => PackageIntegrationActionKind.Uninstall,
            _ => (PackageIntegrationActionKind?)null
        };
        if (action is null)
            return null;

        var appDirectory = Path.GetDirectoryName(Path.GetFullPath(processPath));
        if (appDirectory is null
            || !string.Equals(Path.GetFileName(appDirectory), "app", StringComparison.Ordinal))
        {
            return null;
        }

        var packageRoot = Directory.GetParent(appDirectory)?.FullName;
        if (packageRoot is null)
            return null;

        var scriptName = action == PackageIntegrationActionKind.Install
            ? "install-desktop.sh"
            : "uninstall-desktop.sh";
        return new PackageIntegrationLaunch(
            action.Value,
            packageRoot,
            Path.Combine(appDirectory, "integration", scriptName));
    }

    internal static async Task<PackageIntegrationResult> RunAsync(
        PackageIntegrationLaunch launch,
        CancellationToken cancellationToken = default)
    {
        if (!File.Exists(launch.ScriptPath))
            return new PackageIntegrationResult(false, "The package integration script is missing.");

        var startInfo = new ProcessStartInfo
        {
            FileName = launch.ScriptPath,
            WorkingDirectory = launch.PackageRoot,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        try
        {
            using var process = new Process { StartInfo = startInfo };
            if (!process.Start())
                return new PackageIntegrationResult(false, "The package integration process could not be started.");

            var standardOutput = process.StandardOutput.ReadToEndAsync(cancellationToken);
            var standardError = process.StandardError.ReadToEndAsync(cancellationToken);
            await process.WaitForExitAsync(cancellationToken);
            var output = (await standardOutput).Trim();
            var error = (await standardError).Trim();

            if (process.ExitCode == 0)
                return new PackageIntegrationResult(true, output);

            return new PackageIntegrationResult(
                false,
                string.IsNullOrWhiteSpace(error)
                    ? $"The operation exited with code {process.ExitCode}."
                    : error);
        }
        catch (Exception ex) when (ex is IOException or System.ComponentModel.Win32Exception)
        {
            return new PackageIntegrationResult(false, ex.Message);
        }
    }
}
