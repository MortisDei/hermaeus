namespace Hermaeus.Core.Models;

public enum ServerStatus { Stopped, Starting, Running, Error }

public class ServerConfig
{
    public string Id             { get; set; } = Guid.NewGuid().ToString();
    public string Name           { get; set; } = "llama-server";
    public string ExecutablePath { get; set; } = "llama-server";
    public string ModelPath      { get; set; } = string.Empty;
    public int    Port           { get; set; } = 8080;
    public int    ContextSize    { get; set; } = 4096;

    /// <summary>
    /// GPU offload layers (r14 1.3). 0 means explicit CPU inference (the flag
    /// is omitted); -1 means "all layers", rendered as <c>--n-gpu-layers 999</c>
    /// and the default for new managed servers when a real GPU is detected; a
    /// positive N offloads exactly N layers.
    /// </summary>
    public int    GpuLayers      { get; set; } = 0;
    public int    Threads        { get; set; } = 4;

    /// <summary>
    /// Parallel request slots (r14 2.1), emitted as <c>--parallel N</c>. Hermaeus
    /// is a single-user chat app, so the default of 1 gives the whole
    /// <see cref="ContextSize"/> to one conversation and keeps every send on the
    /// same KV cache. Existing saved configs deserialize this as 1.
    /// </summary>
    public int    Slots          { get; set; } = 1;
    public bool   EmbeddingsMode { get; set; } = false;
    public bool   AutoStart      { get; set; } = false;
    public string ExtraArgs      { get; set; } = string.Empty;

    /// <summary>
    /// First-class engine options (r18 04-llama-server-engine-options.md 4.1). All default to
    /// today's exact command line (additive JSON: an older saved config deserializes to these
    /// defaults and produces a byte-identical launch). Every option is the user's explicit
    /// choice; nothing here is ever forced or auto-changed.
    /// </summary>
    /// <summary>One user-facing KV precision applied to both llama.cpp caches.</summary>
    public string KvCacheType    { get; set; } = "f16";

    /// <summary>Legacy compatibility fields read during the r30 migration.</summary>
    public string KvCacheTypeK   { get; set; } = "f16";
    public string KvCacheTypeV   { get; set; } = "f16";
    public bool PreserveReasoning { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool ReasoningPreserveSupported { get; set; }

    /// <summary>Tri-state: "auto" (server default, emits nothing), "on", or "off".</summary>
    public string FlashAttention { get; set; } = "auto";
    public bool   ContextShift   { get; set; } = false;
    public bool   MemoryLock     { get; set; } = false;
    public bool   NoMemoryMap    { get; set; } = false;

    /// <summary>
    /// Mixture-of-Experts CPU offload, verified against llama-server b10215's
    /// own <c>--help</c>: <c>-cmoe, --cpu-moe</c> and <c>-ncmoe, --n-cpu-moe N</c>.
    ///
    /// <para>Why this exists as its own option rather than as GPU layers: on a
    /// MoE model the expert weights are most of the file but only a fraction
    /// are active per token, so the useful trade is "attention on the GPU,
    /// experts in RAM", not "the first N layers on the GPU". Turning
    /// <see cref="GpuLayers"/> down to fit a MoE model in VRAM gives up
    /// attention offload, which is the part that actually wants the GPU.</para>
    ///
    /// <para>0 emits nothing and is the previous behaviour. A positive N emits
    /// <c>--n-cpu-moe N</c>, keeping the MoE weights of the first N layers on
    /// the CPU. -1 emits <c>--cpu-moe</c>, keeping all of them there.</para>
    /// </summary>
    public int    CpuMoeLayers   { get; set; } = 0;

    /// <summary>
    /// Legacy n-gram speculative decoding flag (r18 04-llama-server-engine-options.md 4.4).
    /// Superseded by <see cref="Speculative"/> in r27 03-drafting-and-proof.md 3.1:
    /// <c>--spec-type</c> accepts a comma-separated list, and one bool owning a
    /// list flag cannot express drafting and n-gram speculation together.
    /// Read once by <c>SettingsService.NormalizeManagedServers</c>, upgraded to
    /// <c>Types = ["ngram-mod"]</c>, and never written again.
    /// </summary>
    public bool   NgramSpeculative { get; set; } = false;

    /// <summary>
    /// r27 03-drafting-and-proof.md 3.1: one composable speculative-decoding
    /// section rather than a bool per technique. Defaults are empty, so a config
    /// that never touches this produces a byte-identical launch command.
    /// </summary>
    public SpeculativeDecodingConfig Speculative { get; set; } = new();

    /// <summary>
    /// r19 5.3: path to a vision projector (mmproj-*.gguf) companion file,
    /// enabling llama-server's multimodal chat mode. Empty (default) means
    /// text-only, byte-identical to today's launch command; set means
    /// <c>--mmproj &lt;path&gt;</c> is appended.
    /// </summary>
    public string MmprojPath { get; set; } = string.Empty;
}
