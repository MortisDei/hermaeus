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
    public string KvCacheTypeK   { get; set; } = "f16";
    public string KvCacheTypeV   { get; set; } = "f16";

    /// <summary>Tri-state: "auto" (server default, emits nothing), "on", or "off".</summary>
    public string FlashAttention { get; set; } = "auto";
    public bool   ContextShift   { get; set; } = false;
    public bool   MemoryLock     { get; set; } = false;
    public bool   NoMemoryMap    { get; set; } = false;

    /// <summary>
    /// N-gram speculative decoding (r18 04-llama-server-engine-options.md 4.4): zero additional
    /// VRAM, drafts from the prompt/history itself. Emits <c>--spec-type ngram-mod</c> only.
    /// </summary>
    public bool   NgramSpeculative { get; set; } = false;

    /// <summary>
    /// r19 5.3: path to a vision projector (mmproj-*.gguf) companion file,
    /// enabling llama-server's multimodal chat mode. Empty (default) means
    /// text-only, byte-identical to today's launch command; set means
    /// <c>--mmproj &lt;path&gt;</c> is appended.
    /// </summary>
    public string MmprojPath { get; set; } = string.Empty;
}
