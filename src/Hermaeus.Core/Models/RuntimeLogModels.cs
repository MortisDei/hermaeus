using System;

namespace Hermaeus.Core.Models;

public enum RuntimeLogLevel
{
    Info,
    Warning,
    Error,
    Debug
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
    string Message,
    string? OperationId = null);
