using System;

namespace Aether.Core.Models;

public enum RuntimeLogLevel
{
    Info,
    Warning,
    Error
}

public enum RuntimeLogCategory
{
    Startup,
    Network,
    ModelLoad,
    Voice,
    Rag,
    Agent,
    Service
}

public sealed record RuntimeLogEntry(
    DateTime Timestamp,
    RuntimeLogLevel Level,
    RuntimeLogCategory Category,
    string Message);
