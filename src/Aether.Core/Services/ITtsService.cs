namespace Aether.Core.Services;

public interface ITtsService
{
    Task SpeakAsync(string text, CancellationToken ct = default);
}
