namespace Aether.Core.Models;

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
    /// Parallel request slots (r14 2.1), emitted as <c>--parallel N</c>. Aether
    /// is a single-user chat app, so the default of 1 gives the whole
    /// <see cref="ContextSize"/> to one conversation and keeps every send on the
    /// same KV cache. Existing saved configs deserialize this as 1.
    /// </summary>
    public int    Slots          { get; set; } = 1;
    public bool   EmbeddingsMode { get; set; } = false;
    public bool   AutoStart      { get; set; } = false;
    public string ExtraArgs      { get; set; } = string.Empty;
}
