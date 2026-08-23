using Hermaeus.Core.Services;

namespace Hermaeus.Services;

public sealed class TrayIntegrationState : ITrayIntegrationState
{
    private int _confirmed;

    public bool IsConfirmed => Volatile.Read(ref _confirmed) != 0;

    public void Confirm() => Interlocked.Exchange(ref _confirmed, 1);
}
