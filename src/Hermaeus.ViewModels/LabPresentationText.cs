using Hermaeus.Core.Models;

namespace Hermaeus.ViewModels;

/// <summary>
/// Presents Lab's typed states in the experiment and evidence surfaces without
/// hiding the raw values in the expandable evidence details.
/// </summary>
public static class LabPresentationText
{
    public static string CapabilityState(Hermaeus.Core.Models.CapabilityState state) => state switch
    {
        Hermaeus.Core.Models.CapabilityState.Available => "Available",
        Hermaeus.Core.Models.CapabilityState.Unavailable => "Unavailable",
        Hermaeus.Core.Models.CapabilityState.Unknown => "Unknown",
        _ => state.ToString()
    };

    public static string CapabilityHint(Hermaeus.Core.Models.CapabilityState state) => state switch
    {
        Hermaeus.Core.Models.CapabilityState.Available => "The declared prerequisites are established. Launch can still fail if the runtime rejects the configuration.",
        Hermaeus.Core.Models.CapabilityState.Unavailable => "This recipe cannot run with the current model or runtime capabilities.",
        Hermaeus.Core.Models.CapabilityState.Unknown => "Inspect the exact runtime or choose a verified model pair before running this recipe.",
        _ => "The current runtime did not provide a recognized capability state."
    };

    public static string RunStatus(string value) => value switch
    {
        "Not started" => "Ready to start",
        "Starting" => "Starting isolated runtime",
        "Running" => "Running",
        "Succeeded" => "Completed",
        "PartiallySucceeded" => "Completed with reservations",
        "Inconclusive" => "Completed, effective configuration unverified",
        "Cancelled" => "Cancelled",
        "Failed" => "Failed",
        _ => string.IsNullOrWhiteSpace(value) ? "No run" : value
    };

    public static string Outcome(NormalizedOutcome outcome) => outcome switch
    {
        NormalizedOutcome.Succeeded => "Succeeded",
        NormalizedOutcome.PartiallySucceeded => "Partial success",
        NormalizedOutcome.NoEffect => "No effect",
        NormalizedOutcome.Unavailable => "Unavailable",
        NormalizedOutcome.Denied => "Denied",
        NormalizedOutcome.Blocked => "Blocked",
        NormalizedOutcome.Failed => "Failed",
        NormalizedOutcome.Cancelled => "Cancelled",
        NormalizedOutcome.TimedOut => "Timed out",
        NormalizedOutcome.Unknown => "Unknown",
        _ => outcome.ToString()
    };

    public static string EvidenceOrigin(Hermaeus.Core.Models.EvidenceOrigin origin) => origin switch
    {
        Hermaeus.Core.Models.EvidenceOrigin.DirectObservation => "Direct observation",
        Hermaeus.Core.Models.EvidenceOrigin.UserProvided => "Owner provided",
        Hermaeus.Core.Models.EvidenceOrigin.ModelInference => "Model inference",
        Hermaeus.Core.Models.EvidenceOrigin.DeterministicCalculation => "Calculated",
        Hermaeus.Core.Models.EvidenceOrigin.Extracted => "Extracted",
        _ => origin.ToString()
    };

    public static string EvidenceStatus(EmpiricalExperienceStatus status) => status switch
    {
        EmpiricalExperienceStatus.Current => "Current",
        EmpiricalExperienceStatus.Superseded => "Superseded",
        _ => status.ToString()
    };
}
