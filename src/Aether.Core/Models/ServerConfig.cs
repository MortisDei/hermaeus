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
    public int    GpuLayers      { get; set; } = 0;
    public int    Threads        { get; set; } = 4;
    public bool   EmbeddingsMode { get; set; } = false;
    public bool   AutoStart      { get; set; } = false;
    public string ExtraArgs      { get; set; } = string.Empty;
}
