using Hermaeus.Desktop;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

internal static class SingleInstanceGuardTests
{
    public static Task SecondNormalLaunchStopsBeforeDesktopStartup()
    {
        False(Hermaeus.Desktop.Program.ShouldContinueStartup(ownsInstance: false, isPackageIntegration: false),
            "a second normal launch must exit before Avalonia constructs a window or owner services");
        True(Hermaeus.Desktop.Program.ShouldContinueStartup(ownsInstance: true, isPackageIntegration: false),
            "the lock owner may continue to desktop startup");
        return Task.CompletedTask;
    }

    public static Task SecondLaunchCannotAcquireTheApplicationLock()
    {
        using var temp = new TempDir();
        var lockPath = temp.PathFor("hermaeus.lock");

        True(SingleInstanceGuard.TryAcquire(lockPath), "the first acquire on a fresh lock file should succeed");

        // A second exclusive open while the first handle is still live stands
        // in for a second Hermaeus process launching against the same lock.
        // The application must exit before constructing a window or touching
        // any owner state. There is deliberately no activation IPC path.
        FileStream? contender = null;
        try
        {
            contender = new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            True(false, "a second exclusive open while the lock is held should fail, just like a second process launch");
        }
        catch (IOException)
        {
            // expected
        }
        finally
        {
            contender?.Dispose();
            SingleInstanceGuard.Release();
        }
        return Task.CompletedTask;
    }

    public static Task ReleaseFreesTheLockForANextAcquire()
    {
        using var temp = new TempDir();
        var lockPath = temp.PathFor("hermaeus.lock");

        True(SingleInstanceGuard.TryAcquire(lockPath), "the first acquire should succeed");
        SingleInstanceGuard.Release();

        True(SingleInstanceGuard.TryAcquire(lockPath),
            "acquiring again after Release should succeed, exactly like relaunching once Hermaeus has fully exited");
        SingleInstanceGuard.Release();
        return Task.CompletedTask;
    }

    public static Task RepeatedAcquireInTheSameProcessIsRejected()
    {
        using var temp = new TempDir();
        var lockPath = temp.PathFor("hermaeus.lock");

        True(SingleInstanceGuard.TryAcquire(lockPath), "the first acquire should succeed");
        False(SingleInstanceGuard.TryAcquire(lockPath),
            "a startup race in one process must not replace its authoritative lock handle");
        SingleInstanceGuard.Release();
        return Task.CompletedTask;
    }
}
