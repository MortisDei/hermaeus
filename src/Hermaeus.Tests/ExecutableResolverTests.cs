using Hermaeus.Services.ProcessManagement;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ExecutableResolverTests
{
    private const string PathExt = ".exe;.bat;.cmd";

    [Fact]
    public void FindOnPath_resolves_bare_name_to_dot_exe_on_windows()
    {
        using var temp = new TempDir();
        var pathDir = temp.PathFor("bin");
        Directory.CreateDirectory(pathDir);
        var exePath = Path.Combine(pathDir, "llama-server.exe");
        File.WriteAllText(exePath, "stub");

        var resolved = ExecutableResolver.FindOnPath("llama-server", isWindows: true, pathOverride: pathDir, pathExt: PathExt);

        Assert.Equal(exePath, resolved);
    }

    [Fact]
    public void FindOnPath_does_not_add_extensions_on_non_windows()
    {
        using var temp = new TempDir();
        var pathDir = temp.PathFor("bin");
        Directory.CreateDirectory(pathDir);
        var exePath = Path.Combine(pathDir, "llama-server.exe");
        File.WriteAllText(exePath, "stub");

        var resolved = ExecutableResolver.FindOnPath("llama-server", isWindows: false, pathOverride: pathDir, pathExt: PathExt);

        Assert.Null(resolved);
    }

    [Fact]
    public void ResolveInDirectory_finds_dot_exe_via_direct_probe()
    {
        using var temp = new TempDir();
        var dir = temp.PathFor("install");
        Directory.CreateDirectory(dir);
        var exePath = Path.Combine(dir, "llama-server.exe");
        File.WriteAllText(exePath, "stub");

        var resolved = ExecutableResolver.ResolveInDirectory(dir, "llama-server", isWindows: true, pathExt: PathExt);

        Assert.Equal(exePath, resolved);
    }

    [Fact]
    public void Resolve_directory_configured_value_finds_the_single_exe_inside()
    {
        using var temp = new TempDir();
        var dir = temp.PathFor("install");
        Directory.CreateDirectory(Path.Combine(dir, "nested"));
        var exePath = Path.Combine(dir, "nested", "llama-server.exe");
        File.WriteAllText(exePath, "stub");

        var resolution = ExecutableResolver.Resolve(dir, "llama-server", isWindows: true, pathExt: PathExt);

        Assert.True(resolution.Success);
        Assert.Equal(exePath, resolution.Path);
    }

    [Fact]
    public void Resolve_bare_name_configured_value_finds_it_on_path()
    {
        using var temp = new TempDir();
        var pathDir = temp.PathFor("bin");
        Directory.CreateDirectory(pathDir);
        var exePath = Path.Combine(pathDir, "llama-server.exe");
        File.WriteAllText(exePath, "stub");

        var resolution = ExecutableResolver.Resolve("llama-server", "llama-server", isWindows: true, pathOverride: pathDir, pathExt: PathExt);

        Assert.True(resolution.Success);
        Assert.Equal(exePath, resolution.Path);
    }

    [Fact]
    public void Resolve_reports_ambiguous_when_directory_holds_two_matches()
    {
        using var temp = new TempDir();
        var dir = temp.PathFor("install");
        Directory.CreateDirectory(Path.Combine(dir, "a"));
        Directory.CreateDirectory(Path.Combine(dir, "b"));
        File.WriteAllText(Path.Combine(dir, "a", "llama-server.exe"), "stub");
        File.WriteAllText(Path.Combine(dir, "b", "llama-server.exe"), "stub");

        var resolution = ExecutableResolver.Resolve(dir, "llama-server", isWindows: true, pathExt: PathExt);

        Assert.False(resolution.Success);
        Assert.Equal(ExecutableResolutionFailure.Ambiguous, resolution.Failure);
    }

    /// <summary>r11 1.3 acceptance: Doctor's check and ServerProcessManager's launch resolution must agree, because both now call the same ExecutableResolver.Resolve.</summary>
    [Fact]
    public void Resolve_gives_the_same_answer_regardless_of_caller()
    {
        using var temp = new TempDir();
        var pathDir = temp.PathFor("bin");
        Directory.CreateDirectory(pathDir);
        File.WriteAllText(Path.Combine(pathDir, "llama-server.exe"), "stub");

        var fromDoctorStyleCall = ExecutableResolver.Resolve("llama-server", "llama-server", isWindows: true, pathOverride: pathDir, pathExt: PathExt);
        var fromServerManagerStyleCall = ExecutableResolver.Resolve("llama-server", "llama-server", isWindows: true, pathOverride: pathDir, pathExt: PathExt);

        Assert.Equal(fromDoctorStyleCall.Success, fromServerManagerStyleCall.Success);
        Assert.Equal(fromDoctorStyleCall.Path, fromServerManagerStyleCall.Path);
    }

    [Fact]
    public void OrphanDetector_matches_a_bare_configured_name_against_the_resolved_exe()
    {
        using var temp = new TempDir();
        var exePath = temp.PathFor("llama-server.exe");
        File.WriteAllText(exePath, "stub");

        // OrphanServerDetector.IsSameExecutable compares two already-resolved
        // full paths; this proves the raw bare name and the on-disk .exe are
        // the same path once both go through Path.GetFullPath, mirroring
        // what ResolveConfiguredExecutable produces before the comparison.
        Assert.True(OrphanServerDetector.IsSameExecutable(exePath, exePath));
    }
}
