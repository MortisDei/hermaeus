namespace Aether.Core.Services;

public interface IAutomationScheduler : IDisposable
{
    void Start();
    void Stop();
}
