namespace Hermaeus.ViewModels;

/// <summary>
/// r29 doc 02 2.1: where a model in the Models panel came from.
///
/// A presentation concept, deliberately not in Hermaeus.Core: nothing persists
/// it, it is derived from the manifest's RepoId and the model's provider. It
/// exists so that adding a second download provider is a new enum value and a
/// new badge glyph, rather than a second boolean threaded through the template.
/// </summary>
public enum ModelSourceKind
{
    /// <summary>Not determinable: a model reported live by a running provider.</summary>
    Unknown,

    /// <summary>A GGUF found on disk with no recorded download provenance.</summary>
    LocalFile,

    /// <summary>Downloaded through, or linked to, a Hugging Face repo.</summary>
    HuggingFace,
}
