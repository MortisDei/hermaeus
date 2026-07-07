namespace Aether.Core.Services;

public sealed record RuntimeHealth(string ProfileId, bool IsHealthy, string Message);
