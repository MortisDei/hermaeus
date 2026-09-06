using System.Text;
using Hermaeus.Services;
using Xunit;
using static Hermaeus.Tests.Helpers;

namespace Hermaeus.Tests;

// r17 01-gguf-context-and-tuning.md 1.1: GGUF header metadata parser. Fixtures are
// hand-written bytes in temp files; no real model files in the repo.
public sealed class GgufMetadataReaderTests
{
    private const uint TypeU8 = 0, TypeI8 = 1, TypeU16 = 2, TypeI16 = 3, TypeU32 = 4, TypeI32 = 5,
        TypeF32 = 6, TypeBool = 7, TypeString = 8, TypeArray = 9, TypeU64 = 10, TypeI64 = 11, TypeF64 = 12;

    private sealed class GgufWriter
    {
        private readonly MemoryStream _stream = new();
        private readonly BinaryWriter _w;
        public GgufWriter() => _w = new BinaryWriter(_stream);

        public GgufWriter Magic(string magic = "GGUF")
        {
            _w.Write(Encoding.ASCII.GetBytes(magic));
            return this;
        }

        public GgufWriter Header(uint version, ulong tensorCount, ulong kvCount)
        {
            _w.Write(version);
            _w.Write(tensorCount);
            _w.Write(kvCount);
            return this;
        }

        public GgufWriter Key(string key)
        {
            _w.Write((ulong)Encoding.UTF8.GetByteCount(key));
            _w.Write(Encoding.UTF8.GetBytes(key));
            return this;
        }

        public GgufWriter StringValue(string key, string value)
        {
            Key(key);
            _w.Write(TypeString);
            _w.Write((ulong)Encoding.UTF8.GetByteCount(value));
            _w.Write(Encoding.UTF8.GetBytes(value));
            return this;
        }

        public GgufWriter U32Value(string key, uint value)
        {
            Key(key);
            _w.Write(TypeU32);
            _w.Write(value);
            return this;
        }

        public GgufWriter ArrayU32Value(string key, uint[] values)
        {
            Key(key);
            _w.Write(TypeArray);
            _w.Write(TypeU32);
            _w.Write((ulong)values.Length);
            foreach (var v in values)
                _w.Write(v);
            return this;
        }

        public GgufWriter ArrayBoolValue(string key, bool[] values)
        {
            Key(key);
            _w.Write(TypeArray);
            _w.Write(TypeBool);
            _w.Write((ulong)values.Length);
            foreach (var v in values)
                _w.Write(v);
            return this;
        }

        public GgufWriter ArrayStringValue(string key, string[] values)
        {
            Key(key);
            _w.Write(TypeArray);
            _w.Write(TypeString);
            _w.Write((ulong)values.Length);
            foreach (var v in values)
            {
                _w.Write((ulong)Encoding.UTF8.GetByteCount(v));
                _w.Write(Encoding.UTF8.GetBytes(v));
            }
            return this;
        }

        public GgufWriter RawKeyWithOversizedLength(string keyPrefix)
        {
            // A key string declaring a length far beyond the 64 KiB cap and beyond the
            // remaining file - the reader must reject this without reading further.
            _w.Write(ulong.MaxValue / 2);
            _w.Write(Encoding.UTF8.GetBytes(keyPrefix));
            return this;
        }

        public byte[] ToBytes() => _stream.ToArray();

        public string WriteToTempFile(TempDir dir, string name = "model.gguf")
        {
            var path = dir.PathFor(name);
            File.WriteAllBytes(path, ToBytes());
            return path;
        }
    }

    private static GgufWriter LlamaShapeKeys(GgufWriter w, string arch = "llama") => w
        .StringValue("general.architecture", arch)
        .U32Value("general.file_type", 15) // Q4_K_M
        .U32Value($"{arch}.block_count", 32)
        .U32Value($"{arch}.context_length", 8192)
        .U32Value($"{arch}.embedding_length", 4096)
        .U32Value($"{arch}.attention.head_count", 32)
        .U32Value($"{arch}.attention.head_count_kv", 8)
        .U32Value($"{arch}.attention.key_length", 128)
        .U32Value($"{arch}.attention.value_length", 128);

    [Fact]
    public void Valid_v3_header_reads_the_llama_shape_keys()
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic().Header(3, tensorCount: 0, kvCount: 9);
        LlamaShapeKeys(w);
        var path = w.WriteToTempFile(temp);

        var info = GgufMetadataReader.TryRead(path);

        Assert.NotNull(info);
        Assert.Equal("llama", info!.Architecture);
        Assert.Equal("Q4_K_M", info.Quantization);
        Assert.Equal(32, info.BlockCount);
        Assert.Equal(8192, info.TrainingContextLength);
        Assert.Equal(4096, info.EmbeddingLength);
        Assert.Equal(32, info.HeadCount);
        Assert.Equal(8, info.HeadCountKv);
        Assert.Equal(128, info.KeyLength);
        Assert.Equal(128, info.ValueLength);
    }

    [Fact]
    public void Sliding_window_attention_metadata_is_read_when_present()
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic().Header(3, tensorCount: 0, kvCount: 11);
        LlamaShapeKeys(w, arch: "gemma3");
        w.U32Value("gemma3.attention.sliding_window", 1024);
        w.ArrayBoolValue("gemma3.attention.sliding_window_pattern", [true, true, true, true, true, false]);
        var path = w.WriteToTempFile(temp);

        var info = GgufMetadataReader.TryRead(path);

        Assert.NotNull(info);
        Assert.Equal(1024, info!.SlidingWindow);
        Assert.Equal([true, true, true, true, true, false], info.SlidingWindowPattern);
    }

    [Fact]
    public void Valid_v2_header_is_accepted()
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic().Header(2, tensorCount: 0, kvCount: 9);
        LlamaShapeKeys(w);
        var path = w.WriteToTempFile(temp);

        var info = GgufMetadataReader.TryRead(path);

        Assert.NotNull(info);
        Assert.Equal(32, info!.BlockCount);
    }

    [Fact]
    public void Speculative_pair_identity_metadata_is_read_without_tensor_data()
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic().Header(3, tensorCount: 99, kvCount: 8)
            .StringValue("general.architecture", "eagle3")
            .StringValue("general.name", "Qwen3 4B EAGLE-3")
            .StringValue("general.repo_url", "draft/repository")
            .StringValue("general.base_model.0.name", "Qwen3 4B")
            .StringValue("general.base_model.0.repo_url", "Qwen/Qwen3-4B")
            .StringValue("tokenizer.ggml.model", "gpt2")
            .StringValue("tokenizer.ggml.pre", "qwen2")
            .U32Value("eagle3.vocab_size", 128);
        var path = w.WriteToTempFile(temp, "companion.gguf");

        var info = GgufMetadataReader.TryRead(path);

        Assert.NotNull(info);
        Assert.Equal("Qwen3 4B EAGLE-3", info!.Name);
        Assert.Equal("Qwen/Qwen3-4B", info.BaseModelRepositoryUrl);
        Assert.Equal("gpt2:qwen2:128", info.TokenizerIdentity);
    }

    [Fact]
    public void Mixture_of_experts_and_nextn_metadata_are_read_as_capability_facts()
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic().Header(3, tensorCount: 0, kvCount: 3)
            .StringValue("general.architecture", "mixtral")
            .U32Value("mixtral.expert_count", 8)
            .U32Value("mixtral.expert_used_count", 2);
        var path = w.WriteToTempFile(temp, "moe.gguf");

        var info = GgufMetadataReader.TryRead(path);

        Assert.NotNull(info);
        Assert.Equal(8, info!.ExpertCount);
        Assert.Equal(2, info.ExpertUsedCount);
    }

    [Fact]
    public void Unknown_key_values_are_skipped_including_string_arrays_so_later_keys_are_still_reached()
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic().Header(3, tensorCount: 0, kvCount: 3);
        w.ArrayStringValue("tokenizer.ggml.tokens", ["<s>", "</s>", "hello", "world"]);
        w.U32Value("llama.block_count", 32);
        w.StringValue("general.architecture", "llama");
        var path = w.WriteToTempFile(temp);

        var info = GgufMetadataReader.TryRead(path);

        Assert.NotNull(info);
        Assert.Equal(32, info!.BlockCount);
        Assert.Equal("llama", info.Architecture);
    }

    [Fact]
    public void Per_layer_head_count_kv_array_takes_the_maximum()
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic().Header(3, tensorCount: 0, kvCount: 2);
        w.U32Value("llama.block_count", 4);
        w.ArrayU32Value("llama.attention.head_count_kv", [2, 4, 8, 3]);
        var path = w.WriteToTempFile(temp);

        var info = GgufMetadataReader.TryRead(path);

        Assert.NotNull(info);
        Assert.Equal(8, info!.HeadCountKv);
    }

    [Fact]
    public void Missing_key_value_and_length_fall_back_to_head_count_and_embedding_split()
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic().Header(3, tensorCount: 0, kvCount: 3);
        w.U32Value("llama.embedding_length", 4096);
        w.U32Value("llama.attention.head_count", 32);
        // no explicit key_length/value_length and no head_count_kv
        w.U32Value("llama.block_count", 32);
        var path = w.WriteToTempFile(temp);

        var info = GgufMetadataReader.TryRead(path);

        Assert.NotNull(info);
        Assert.Equal(32, info!.HeadCountKv); // falls back to head_count
        Assert.Equal(128, info.KeyLength);   // 4096 / 32
        Assert.Equal(128, info.ValueLength);
    }

    [Fact]
    public void Magic_mismatch_returns_null()
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic("GGML").Header(3, 0, 0);
        var path = w.WriteToTempFile(temp);

        Assert.Null(GgufMetadataReader.TryRead(path));
    }

    [Fact]
    public void Version_1_is_rejected()
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic().Header(1, 0, 0);
        var path = w.WriteToTempFile(temp);

        Assert.Null(GgufMetadataReader.TryRead(path));
    }

    [Fact]
    public void Version_4_is_rejected()
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic().Header(4, 0, 0);
        var path = w.WriteToTempFile(temp);

        Assert.Null(GgufMetadataReader.TryRead(path));
    }

    [Theory]
    [InlineData(0)]  // truncated right after magic
    [InlineData(4)]  // truncated after magic, mid-version
    [InlineData(8)]  // truncated after version, mid-tensor-count
    [InlineData(16)] // truncated after tensor_count, mid-kv-count
    public void Truncated_at_structural_boundaries_returns_null(int keepBytes)
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic().Header(3, tensorCount: 0, kvCount: 9);
        LlamaShapeKeys(w);
        var full = w.ToBytes();
        var truncated = full[..Math.Min(keepBytes, full.Length)];
        var path = temp.PathFor("truncated.gguf");
        File.WriteAllBytes(path, truncated);

        Assert.Null(GgufMetadataReader.TryRead(path));
    }

    [Fact]
    public void Truncated_mid_value_returns_null()
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic().Header(3, tensorCount: 0, kvCount: 9);
        LlamaShapeKeys(w);
        var full = w.ToBytes();
        // Cut off the last few bytes, landing mid-value on the final declared key.
        var path = temp.PathFor("truncated-value.gguf");
        File.WriteAllBytes(path, full[..(full.Length - 2)]);

        Assert.Null(GgufMetadataReader.TryRead(path));
    }

    [Fact]
    public void Declared_string_larger_than_supported_buffer_returns_null()
    {
        using var temp = new TempDir();
        var path = temp.PathFor("oversized-value.gguf");
        using var stream = File.Create(path);
        using var writer = new BinaryWriter(stream);
        writer.Write(Encoding.ASCII.GetBytes("GGUF"));
        writer.Write(3u);
        writer.Write(0UL);
        writer.Write(1UL);
        writer.Write(ulong.MaxValue);

        Assert.Null(GgufMetadataReader.TryRead(path));
    }

    [Fact]
    public void Oversized_declared_string_length_returns_null()
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic().Header(3, tensorCount: 0, kvCount: 1);
        w.RawKeyWithOversizedLength("x");
        var path = w.WriteToTempFile(temp);

        Assert.Null(GgufMetadataReader.TryRead(path));
    }

    [Fact]
    public void Oversized_kv_count_returns_null()
    {
        using var temp = new TempDir();
        var w = new GgufWriter().Magic().Header(3, tensorCount: 0, kvCount: 1_000_000);
        var path = w.WriteToTempFile(temp);

        Assert.Null(GgufMetadataReader.TryRead(path));
    }

    [Fact]
    public void Missing_file_returns_null()
    {
        Assert.Null(GgufMetadataReader.TryRead(@"C:\definitely\does\not\exist.gguf"));
    }

    [Fact]
    public void Quantization_maps_known_file_type_and_falls_back_to_type_n_for_unknown()
    {
        using var temp = new TempDir();
        var known = new GgufWriter().Magic().Header(3, 0, 1);
        known.U32Value("general.file_type", 18); // Q6_K
        var knownPath = known.WriteToTempFile(temp, "known.gguf");
        Assert.Equal("Q6_K", GgufMetadataReader.TryRead(knownPath)!.Quantization);

        var unknown = new GgufWriter().Magic().Header(3, 0, 1);
        unknown.U32Value("general.file_type", 999);
        var unknownPath = unknown.WriteToTempFile(temp, "unknown.gguf");
        Assert.Equal("type 999", GgufMetadataReader.TryRead(unknownPath)!.Quantization);
    }
}
