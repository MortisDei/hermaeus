using System.Collections.Concurrent;
using System.Text;

namespace Hermaeus.Services;

/// <summary>
/// Header metadata for a local GGUF model file: architecture, quantization, and the shape
/// facts needed for KV-cache math (<see cref="KvCacheMath"/>). HeadCountKv/KeyLength/
/// ValueLength already carry their documented fallbacks (head_count, embedding_length /
/// head_count) resolved at read time, so callers never need to re-derive them.
/// </summary>
public sealed record GgufModelInfo(
    string Architecture,
    string Quantization,
    int? BlockCount,
    int? TrainingContextLength,
    int? EmbeddingLength,
    int? HeadCount,
    int? HeadCountKv,
    int? KeyLength,
    int? ValueLength,
    int? SlidingWindow = null,
    IReadOnlyList<bool>? SlidingWindowPattern = null,
    /// <summary>
    /// r27 03-drafting-and-proof.md 3.3: the token count a draft model must
    /// share with its target, read either from the architecture's
    /// <c>.vocab_size</c> key or from the length of the tokenizer token array.
    /// Null when the file declares neither.
    /// </summary>
    int? VocabularySize = null,
    int? NextnPredictLayers = null,
    int? ExpertCount = null,
    int? ExpertUsedCount = null,
    bool HasChatTemplate = false,
    string Name = "",
    string RepositoryUrl = "",
    string BaseModelName = "",
    string BaseModelRepositoryUrl = "",
    string TokenizerModel = "",
    string TokenizerPre = "",
    string GeneralType = "")
{
    public string TokenizerIdentity =>
        string.IsNullOrWhiteSpace(TokenizerModel) || string.IsNullOrWhiteSpace(TokenizerPre)
            ? string.Empty
            : $"{TokenizerModel.Trim().ToLowerInvariant()}:{TokenizerPre.Trim().ToLowerInvariant()}:{VocabularySize?.ToString(System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty}";
}

/// <summary>
/// Reads only the metadata key/value section of a GGUF file header; tensor data is never
/// read. Model files are downloaded from the internet, so this parser is untrusted-input
/// surface: every read is bounds-checked against hard caps, and any parse failure - malformed
/// structure, an oversized declared length, a truncated file - returns null instead of letting
/// an exception escape (r17 01-gguf-context-and-tuning.md 1.1; docs/security-review.md).
/// </summary>
public static class GgufMetadataReader
{
    private const long MaxStringLength = 64 * 1024;
    private const long MaxArrayCount = 1_000_000;
    private const ulong MaxMetadataKvCount = 100_000;
    private const int MaxNestingDepth = 8;

    private static readonly ConcurrentDictionary<(string Path, long Size, DateTime Mtime), GgufModelInfo?> Cache = new();

    private static readonly Dictionary<long, string> QuantizationLabels = new()
    {
        [0] = "F32",
        [1] = "F16",
        [2] = "Q4_0",
        [3] = "Q4_1",
        [7] = "Q8_0",
        [8] = "Q5_0",
        [9] = "Q5_1",
        [10] = "Q2_K",
        [12] = "Q3_K_M",
        [14] = "Q4_K_S",
        [15] = "Q4_K_M",
        [17] = "Q5_K_M",
        [18] = "Q6_K",
        [30] = "IQ4_XS",
        [32] = "BF16"
    };

    /// <summary>Process-lifetime cache keyed on (full path, size, mtime), same spirit as the
    /// r13 <c>HardwareProfile</c> cache. Callers are responsible for calling this off the UI
    /// thread; failures are cached too so a corrupt/unsupported file isn't re-parsed every call.</summary>
    public static GgufModelInfo? TryRead(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                return null;

            var key = (Path.GetFullPath(path), info.Length, info.LastWriteTimeUtc);
            return Cache.GetOrAdd(key, static k => TryReadCore(k.Path));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Reads a bounded GGUF header supplied by a trusted transport probe. The
    /// caller must cap the input before calling this method; tensor data is not required.</summary>
    public static GgufModelInfo? TryRead(ReadOnlyMemory<byte> bytes)
    {
        try
        {
            using var stream = new MemoryStream(bytes.ToArray(), writable: false);
            return TryReadCore(stream);
        }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
    }

    private static GgufModelInfo? TryReadCore(string path)
    {
        try
        {
            using var stream = File.OpenRead(path);
            return TryReadCore(stream);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
    }

    private static GgufModelInfo? TryReadCore(Stream stream)
    {
        try
        {
            using var reader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            var magic = ReadExactBytes(reader, 4);
            if (magic[0] != (byte)'G' || magic[1] != (byte)'G' || magic[2] != (byte)'U' || magic[3] != (byte)'F')
                return null;

            var version = reader.ReadUInt32();
            if (version is not (2 or 3))
                return null;

            _ = reader.ReadUInt64(); // tensor_count: unused, but part of the fixed layout
            var kvCount = reader.ReadUInt64();
            if (kvCount > MaxMetadataKvCount)
                return null;

            string architecture = string.Empty;
            var generalType = string.Empty;
            long? fileType = null;
            long? blockCount = null;
            long? contextLength = null;
            long? embeddingLength = null;
            long? headCount = null;
            long? headCountKv = null;
            long? keyLength = null;
            long? valueLength = null;
            long? slidingWindow = null;
            long? vocabularySize = null;
            long? nextnPredictLayers = null;
            long? expertCount = null;
            long? expertUsedCount = null;
            var hasChatTemplate = false;
            var name = string.Empty;
            var repositoryUrl = string.Empty;
            var baseModelName = string.Empty;
            var baseModelRepositoryUrl = string.Empty;
            var tokenizerModel = string.Empty;
            var tokenizerPre = string.Empty;
            IReadOnlyList<bool>? slidingWindowPattern = null;

            for (ulong i = 0; i < kvCount; i++)
            {
                var key = ReadGgufString(reader);
                var valueType = reader.ReadUInt32();

                // Suffix-matched rather than architecture-prefix-matched: llama.cpp always
                // writes exactly one architecture's shape keys per file, and matching by
                // suffix means the order "general.architecture" appears relative to the
                // shape keys never matters.
                if (key == "general.architecture")
                    architecture = ReadValue(reader, valueType, 0) as string ?? string.Empty;
                else if (key == "general.type")
                    generalType = ReadValue(reader, valueType, 0) as string ?? string.Empty;
                else if (key == "general.name")
                    name = ReadValue(reader, valueType, 0) as string ?? string.Empty;
                else if (key == "general.repo_url")
                    repositoryUrl = ReadValue(reader, valueType, 0) as string ?? string.Empty;
                else if (key == "general.base_model.0.name")
                    baseModelName = ReadValue(reader, valueType, 0) as string ?? string.Empty;
                else if (key == "general.base_model.0.repo_url")
                    baseModelRepositoryUrl = ReadValue(reader, valueType, 0) as string ?? string.Empty;
                else if (key == "general.file_type")
                    fileType = ToScalarLong(ReadValue(reader, valueType, 0));
                else if (key.EndsWith(".block_count", StringComparison.Ordinal))
                    blockCount = ToScalarLong(ReadValue(reader, valueType, 0));
                else if (key.EndsWith(".context_length", StringComparison.Ordinal))
                    contextLength = ToScalarLong(ReadValue(reader, valueType, 0));
                else if (key.EndsWith(".embedding_length", StringComparison.Ordinal))
                    embeddingLength = ToScalarLong(ReadValue(reader, valueType, 0));
                else if (key.EndsWith(".attention.head_count_kv", StringComparison.Ordinal))
                    headCountKv = ToScalarLong(ReadValue(reader, valueType, 0));
                else if (key.EndsWith(".attention.head_count", StringComparison.Ordinal))
                    headCount = ToScalarLong(ReadValue(reader, valueType, 0));
                else if (key.EndsWith(".attention.key_length", StringComparison.Ordinal))
                    keyLength = ToScalarLong(ReadValue(reader, valueType, 0));
                else if (key.EndsWith(".attention.value_length", StringComparison.Ordinal))
                    valueLength = ToScalarLong(ReadValue(reader, valueType, 0));
                else if (key.EndsWith(".attention.sliding_window", StringComparison.Ordinal))
                    slidingWindow = ToScalarLong(ReadValue(reader, valueType, 0));
                else if (key.EndsWith(".attention.sliding_window_pattern", StringComparison.Ordinal))
                    slidingWindowPattern = ToBoolList(ReadValue(reader, valueType, 0));
                else if (key.EndsWith(".vocab_size", StringComparison.Ordinal))
                    vocabularySize = ToScalarLong(ReadValue(reader, valueType, 0));
                else if (key.EndsWith(".nextn_predict_layers", StringComparison.Ordinal))
                    nextnPredictLayers = ToScalarLong(ReadValue(reader, valueType, 0));
                else if (key.EndsWith(".expert_count", StringComparison.Ordinal))
                    expertCount = ToScalarLong(ReadValue(reader, valueType, 0));
                else if (key.EndsWith(".expert_used_count", StringComparison.Ordinal))
                    expertUsedCount = ToScalarLong(ReadValue(reader, valueType, 0));
                else if (key == "tokenizer.chat_template")
                {
                    _ = ReadValue(reader, valueType, 0);
                    hasChatTemplate = true;
                }
                else if (key == "tokenizer.ggml.model")
                    tokenizerModel = ReadValue(reader, valueType, 0) as string ?? string.Empty;
                else if (key == "tokenizer.ggml.pre")
                    tokenizerPre = ReadValue(reader, valueType, 0) as string ?? string.Empty;
                else if (key == "tokenizer.ggml.tokens" && valueType == 9)
                    // The token array itself is never materialised: only its
                    // declared length is read, then the elements are skipped.
                    vocabularySize ??= SkipArrayAndCount(reader);
                else
                    SkipValue(reader, valueType, 0);
            }

            headCountKv ??= headCount;
            if (keyLength is null && embeddingLength is > 0 && headCount is > 0)
                keyLength = embeddingLength / headCount;
            if (valueLength is null && embeddingLength is > 0 && headCount is > 0)
                valueLength = embeddingLength / headCount;

            return new GgufModelInfo(
                Architecture: architecture,
                Quantization: FormatQuantization(fileType),
                BlockCount: ToInt(blockCount),
                TrainingContextLength: ToInt(contextLength),
                EmbeddingLength: ToInt(embeddingLength),
                HeadCount: ToInt(headCount),
                HeadCountKv: ToInt(headCountKv),
                KeyLength: ToInt(keyLength),
                ValueLength: ToInt(valueLength),
                SlidingWindow: ToInt(slidingWindow),
                SlidingWindowPattern: slidingWindowPattern,
                VocabularySize: ToInt(vocabularySize),
                NextnPredictLayers: ToInt(nextnPredictLayers),
                ExpertCount: ToInt(expertCount),
                ExpertUsedCount: ToInt(expertUsedCount),
                HasChatTemplate: hasChatTemplate,
                Name: name,
                RepositoryUrl: repositoryUrl,
                BaseModelName: baseModelName,
                BaseModelRepositoryUrl: baseModelRepositoryUrl,
                TokenizerModel: tokenizerModel,
                TokenizerPre: tokenizerPre,
                GeneralType: generalType);
        }
        catch (EndOfStreamException) { return null; }
        catch (IOException) { return null; }
        catch (InvalidDataException) { return null; }
        catch (ArgumentException) { return null; }
        catch (NotSupportedException) { return null; }
        catch (OverflowException) { return null; }
    }

    private static string FormatQuantization(long? fileType) =>
        fileType is not long n ? string.Empty
        : QuantizationLabels.TryGetValue(n, out var label) ? label
        : $"type {n}";

    private static int? ToInt(long? value) =>
        value is long v && v >= 0 && v <= int.MaxValue ? (int)v : null;

    private static long? ToScalarLong(object? value)
    {
        switch (value)
        {
            case long l: return l;
            case double d: return (long)d;
            case List<object?> list:
                long? max = null;
                foreach (var item in list)
                {
                    if (ToScalarLong(item) is long v && (max is null || v > max))
                        max = v;
                }
                return max;
            default: return null;
        }
    }

    private static IReadOnlyList<bool>? ToBoolList(object? value)
    {
        if (value is not List<object?> list || list.Count == 0)
            return null;

        var result = new bool[list.Count];
        for (var i = 0; i < list.Count; i++)
        {
            if (ToScalarLong(list[i]) is not long v)
                return null;
            result[i] = v != 0;
        }
        return result;
    }

    /// <summary>Reads a value fully into memory. Used for the small, bounded set of keys this
    /// reader cares about; unrelated keys go through <see cref="SkipValue"/> instead so this
    /// never materializes e.g. a multi-hundred-thousand-entry tokenizer vocabulary array.</summary>
    private static object? ReadValue(BinaryReader r, uint type, int depth)
    {
        if (depth > MaxNestingDepth)
            throw new InvalidDataException("GGUF array nesting too deep.");

        switch (type)
        {
            case 0: return (long)r.ReadByte();
            case 1: return (long)r.ReadSByte();
            case 2: return (long)r.ReadUInt16();
            case 3: return (long)r.ReadInt16();
            case 4: return (long)r.ReadUInt32();
            case 5: return (long)r.ReadInt32();
            case 6: return (double)r.ReadSingle();
            case 7: return r.ReadBoolean() ? 1L : 0L;
            case 8: return ReadGgufString(r);
            case 9:
                var elemType = r.ReadUInt32();
                var count = r.ReadUInt64();
                if (count > MaxArrayCount)
                    throw new InvalidDataException("GGUF array count exceeds the safety cap.");
                var list = new List<object?>((int)Math.Min(count, 1024));
                for (ulong i = 0; i < count; i++)
                    list.Add(ReadValue(r, elemType, depth + 1));
                return list;
            case 10: return unchecked((long)r.ReadUInt64());
            case 11: return r.ReadInt64();
            case 12: return r.ReadDouble();
            default: throw new InvalidDataException($"Unknown GGUF value type {type}.");
        }
    }

    /// <summary>Advances past a value without materializing it, including nested arrays -
    /// required to reach later keys since GGUF has no per-value length prefix for skipping.</summary>
    private static void SkipValue(BinaryReader r, uint type, int depth)
    {
        if (depth > MaxNestingDepth)
            throw new InvalidDataException("GGUF array nesting too deep.");

        switch (type)
        {
            case 0: case 1: case 7: r.ReadByte(); break;
            case 2: case 3: r.ReadUInt16(); break;
            case 4: case 5: case 6: r.ReadUInt32(); break;
            case 8: SkipGgufString(r); break;
            case 9:
                var elemType = r.ReadUInt32();
                var count = r.ReadUInt64();
                if (count > MaxArrayCount)
                    throw new InvalidDataException("GGUF array count exceeds the safety cap.");
                for (ulong i = 0; i < count; i++)
                    SkipValue(r, elemType, depth + 1);
                break;
            case 10: case 11: case 12: r.ReadUInt64(); break;
            default: throw new InvalidDataException($"Unknown GGUF value type {type}.");
        }
    }

    /// <summary>
    /// Reads an array value's declared element count, skips its elements, and
    /// returns the count. Used for tokenizer.ggml.tokens, where the length is
    /// the vocabulary size and the tokens themselves are never needed.
    /// </summary>
    private static long? SkipArrayAndCount(BinaryReader r)
    {
        var elemType = r.ReadUInt32();
        var count = r.ReadUInt64();
        if (count > MaxArrayCount)
            throw new InvalidDataException("GGUF array count exceeds the safety cap.");
        for (ulong i = 0; i < count; i++)
            SkipValue(r, elemType, 1);
        return (long)count;
    }

    private static string ReadGgufString(BinaryReader r)
    {
        var length = r.ReadUInt64();
        if (length > MaxStringLength)
            throw new InvalidDataException("GGUF string length exceeds the safety cap.");
        return Encoding.UTF8.GetString(ReadExactBytes(r, (long)length));
    }

    private static void SkipGgufString(BinaryReader r)
    {
        var length = r.ReadUInt64();
        if (length > MaxStringLength)
            throw new InvalidDataException("GGUF string length exceeds the safety cap.");
        ReadExactBytes(r, (long)length);
    }

    private static byte[] ReadExactBytes(BinaryReader r, long count)
    {
        if (count < 0 || count > int.MaxValue)
            throw new InvalidDataException("GGUF declared an unsupported length.");
        var buffer = new byte[(int)count];
        var offset = 0;
        while (offset < buffer.Length)
        {
            var read = r.Read(buffer, offset, buffer.Length - offset);
            if (read == 0)
                throw new EndOfStreamException("GGUF file truncated.");
            offset += read;
        }
        return buffer;
    }
}
