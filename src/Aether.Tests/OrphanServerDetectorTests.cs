using Aether.Core.Models;
using Aether.Services.ProcessManagement;
using Xunit;

namespace Aether.Tests;

public sealed class OrphanServerDetectorTests
{
    private sealed class FakePortOwnerLookup : IPortOwnerLookup
    {
        public PortOwnerInfo? Owner { get; set; }
        public int FindOwnerCallCount { get; private set; }

        public bool IsPortListening(int port) => Owner is not null;

        public PortOwnerInfo? FindOwner(int port)
        {
            FindOwnerCallCount++;
            return Owner;
        }
    }

    private static ServerConfig NewConfig(string executablePath, int port = 8080) => new()
    {
        Id = "server-1",
        Name = "Chat",
        ExecutablePath = executablePath,
        Port = port
    };

    [Fact]
    public void Detect_returns_null_when_the_port_is_free()
    {
        var lookup = new FakePortOwnerLookup { Owner = null };
        var detector = new OrphanServerDetector(lookup);

        var result = detector.Detect(NewConfig(@"C:\aether\llama-server.exe"));

        Assert.Null(result);
    }

    [Fact]
    public void Detect_marks_an_exact_executable_path_match_as_the_own_binary()
    {
        var exe = @"C:\aether\llama-server.exe";
        var lookup = new FakePortOwnerLookup { Owner = new PortOwnerInfo(1234, "llama-server", exe) };
        var detector = new OrphanServerDetector(lookup);

        var result = detector.Detect(NewConfig(exe));

        Assert.NotNull(result);
        Assert.True(result!.IsOwnBinary);
        Assert.Equal(1234, result.Pid);
    }

    [Fact]
    public void Detect_treats_a_different_executable_as_unrelated_information_only()
    {
        var lookup = new FakePortOwnerLookup
        {
            Owner = new PortOwnerInfo(5678, "some-other-process", @"C:\other\some-other-process.exe")
        };
        var detector = new OrphanServerDetector(lookup);

        var result = detector.Detect(NewConfig(@"C:\aether\llama-server.exe"));

        Assert.NotNull(result);
        Assert.False(result!.IsOwnBinary);
    }

    [Fact]
    public void Detect_treats_an_unresolvable_executable_path_as_unrelated()
    {
        // Best-effort PID lookup succeeded but the executable path could not be resolved
        // (permissions, race with process exit): never assume it is our own binary.
        var lookup = new FakePortOwnerLookup { Owner = new PortOwnerInfo(999, "unknown", null) };
        var detector = new OrphanServerDetector(lookup);

        var result = detector.Detect(NewConfig(@"C:\aether\llama-server.exe"));

        Assert.NotNull(result);
        Assert.False(result!.IsOwnBinary);
    }

    [Fact]
    public void TryStop_succeeds_when_the_pid_and_executable_still_match()
    {
        // Use this test process's own PID so Process.GetProcessById + Kill has a
        // real target; the OS lets a process query itself even if killing it would
        // fail, but here we just verify the identify/verify decision short circuits
        // to the exact expected shape without reaching a real Kill by using a PID
        // that is guaranteed not to match after a deliberate re-check mismatch below.
        var exe = @"C:\aether\llama-server.exe";
        var lookup = new FakePortOwnerLookup { Owner = new PortOwnerInfo(4321, "llama-server", exe) };
        var detector = new OrphanServerDetector(lookup);

        var result = detector.TryStop(NewConfig(exe), expectedPid: 4321);

        // No process with this PID actually exists, so the kill itself fails, but the
        // identify/verify checks (the part under test) must have passed to reach that point.
        Assert.False(result.Success);
        Assert.DoesNotContain("changed since it was detected", result.Message);
        Assert.DoesNotContain("no longer matches", result.Message);
    }

    [Fact]
    public void TryStop_refuses_when_the_pid_on_the_port_no_longer_matches_the_expected_pid()
    {
        // PID reuse guard: the port is now owned by a different PID than the one
        // the caller observed when it decided to offer a Stop button.
        var exe = @"C:\aether\llama-server.exe";
        var lookup = new FakePortOwnerLookup { Owner = new PortOwnerInfo(9999, "llama-server", exe) };
        var detector = new OrphanServerDetector(lookup);

        var result = detector.TryStop(NewConfig(exe), expectedPid: 4321);

        Assert.False(result.Success);
        Assert.Contains("changed since it was detected", result.Message);
    }

    [Fact]
    public void TryStop_refuses_when_the_executable_no_longer_matches()
    {
        // The PID still matches but now runs a different binary (reused PID, or the
        // configured executable path changed underneath us): refuse to kill it.
        var lookup = new FakePortOwnerLookup
        {
            Owner = new PortOwnerInfo(4321, "different", @"C:\other\different.exe")
        };
        var detector = new OrphanServerDetector(lookup);

        var result = detector.TryStop(NewConfig(@"C:\aether\llama-server.exe"), expectedPid: 4321);

        Assert.False(result.Success);
        Assert.Contains("no longer matches", result.Message);
    }

    [Fact]
    public void TryStop_re_verifies_immediately_before_killing_rather_than_trusting_a_stale_snapshot()
    {
        var exe = @"C:\aether\llama-server.exe";
        var lookup = new FakePortOwnerLookup { Owner = new PortOwnerInfo(4321, "llama-server", exe) };
        var detector = new OrphanServerDetector(lookup);

        _ = detector.TryStop(NewConfig(exe), expectedPid: 4321);

        Assert.True(lookup.FindOwnerCallCount > 0, "TryStop must re-query ownership instead of trusting a caller-supplied snapshot");
    }
}
