using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace Hermaeus.Voice;

/// <summary>
/// Maps a merged Whisper decoder graph's input and output names onto the roles
/// the decode loop needs (r25 doc 03 3.3).
///
/// Discovered from the loaded session rather than hardcoded. The exported names
/// follow a convention (<c>input_ids</c>, <c>encoder_hidden_states</c>,
/// <c>past_key_values.N.decoder.key</c>, <c>present.N.decoder.key</c>,
/// <c>use_cache_branch</c>) but conventions drift between exporter versions, and
/// a wrong guess here is a load failure with no useful message. Pairing by name
/// is pure and testable without a session.
/// </summary>
internal sealed class WhisperDecoderBinding
{
    public required string InputIds { get; init; }
    public required string EncoderHiddenStates { get; init; }
    public string? UseCacheBranch { get; init; }

    /// <summary>Past-input name to its matching present-output name, in graph order.</summary>
    public required IReadOnlyList<(string Past, string Present)> CachePairs { get; init; }

    public required string Logits { get; init; }

    /// <summary>Cross-attention cache entries are computed once from the encoder output
    /// and do not grow, unlike the self-attention ones.</summary>
    public required IReadOnlySet<string> EncoderCacheInputs { get; init; }

    public static WhisperDecoderBinding Discover(InferenceSession decoder) =>
        Pair([.. decoder.InputMetadata.Keys], [.. decoder.OutputMetadata.Keys]);

    /// <summary>The pure half, so the pairing is covered without an ONNX session.</summary>
    internal static WhisperDecoderBinding Pair(IReadOnlyList<string> inputs, IReadOnlyList<string> outputs)
    {
        var inputIds = Find(inputs, "input_ids")
            ?? throw new InvalidOperationException("Whisper decoder has no input_ids input.");
        var encoderStates = Find(inputs, "encoder_hidden_states")
            ?? throw new InvalidOperationException("Whisper decoder has no encoder_hidden_states input.");
        var logits = Find(outputs, "logits")
            ?? throw new InvalidOperationException("Whisper decoder has no logits output.");

        var pastInputs = inputs.Where(IsCacheName).ToList();
        var presentOutputs = outputs.Where(IsCacheName).ToList();

        var pairs = new List<(string, string)>();
        var encoderCache = new HashSet<string>(StringComparer.Ordinal);

        foreach (var past in pastInputs)
        {
            var suffix = CacheSuffix(past);
            var present = presentOutputs.FirstOrDefault(o =>
                string.Equals(CacheSuffix(o), suffix, StringComparison.Ordinal));
            if (present is null)
                continue;

            pairs.Add((past, present));
            if (suffix.Contains(".encoder.", StringComparison.Ordinal))
                encoderCache.Add(past);
        }

        return new WhisperDecoderBinding
        {
            InputIds = inputIds,
            EncoderHiddenStates = encoderStates,
            UseCacheBranch = Find(inputs, "use_cache_branch"),
            CachePairs = pairs,
            Logits = logits,
            EncoderCacheInputs = encoderCache
        };
    }

    private static bool IsCacheName(string name) =>
        name.StartsWith("past_key_values", StringComparison.Ordinal)
        || name.StartsWith("present", StringComparison.Ordinal);

    /// <summary>"past_key_values.3.decoder.key" and "present.3.decoder.key" share
    /// the suffix ".3.decoder.key", which is what pairs them.</summary>
    private static string CacheSuffix(string name)
    {
        var dot = name.IndexOf('.');
        return dot < 0 ? name : name[dot..];
    }

    private static string? Find(IReadOnlyList<string> names, string exact) =>
        names.FirstOrDefault(n => string.Equals(n, exact, StringComparison.Ordinal));
}

/// <summary>
/// One decode in progress: holds the encoder output and the growing key/value
/// cache, and exposes a single <see cref="Step"/> the pure decoder drives.
///
/// The cache is carried as tensors between calls, which is the whole reason
/// in-process Whisper is tractable: ONNX Runtime produces the next state, this
/// hands it back, and no attention arithmetic is written here.
/// </summary>
internal sealed class WhisperDecoderSession
{
    private readonly InferenceSession _decoder;
    private readonly WhisperDecoderBinding _binding;
    private readonly DenseTensor<float> _encoderStates;
    private readonly Dictionary<string, DenseTensor<float>> _cache = new(StringComparer.Ordinal);
    private bool _hasCache;

    public WhisperDecoderSession(
        InferenceSession decoder, WhisperDecoderBinding binding, DenseTensor<float> encoderStates)
    {
        _decoder = decoder;
        _binding = binding;
        _encoderStates = encoderStates;
    }

    public float[] Step(IReadOnlyList<int> tokens)
    {
        // Without a cache the graph is fed the whole prefix; with one it is fed
        // only the newest token, which is the point of keeping the cache.
        var feed = _hasCache ? new[] { (long)tokens[^1] } : tokens.Select(t => (long)t).ToArray();
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor(
                _binding.InputIds, new DenseTensor<long>(feed, [1, feed.Length])),
            NamedOnnxValue.CreateFromTensor(_binding.EncoderHiddenStates, _encoderStates)
        };

        if (_binding.UseCacheBranch is { } flag)
            inputs.Add(NamedOnnxValue.CreateFromTensor(
                flag, new DenseTensor<bool>(new[] { _hasCache }, [1])));

        foreach (var (past, _) in _binding.CachePairs)
            inputs.Add(NamedOnnxValue.CreateFromTensor(past, CacheOrEmpty(past)));

        using var results = _decoder.Run(inputs);
        var byName = results.ToDictionary(r => r.Name, r => r, StringComparer.Ordinal);

        foreach (var (past, present) in _binding.CachePairs)
        {
            // Cross-attention entries are constant for the window, so once they
            // are populated they are not replaced on later steps.
            if (_hasCache && _binding.EncoderCacheInputs.Contains(past))
                continue;
            if (!byName.TryGetValue(present, out var value))
                continue;

            var tensor = value.AsTensor<float>();
            _cache[past] = new DenseTensor<float>(tensor.ToArray(), tensor.Dimensions.ToArray());
        }

        _hasCache = true;

        var logits = byName[_binding.Logits].AsTensor<float>();
        return LastPositionLogits(logits);
    }

    /// <summary>
    /// An empty cache tensor for the first pass, shaped [batch, heads, 0, dim] so
    /// the graph's concat sees a zero-length sequence.
    /// </summary>
    private DenseTensor<float> CacheOrEmpty(string name)
    {
        if (_cache.TryGetValue(name, out var existing))
            return existing;

        var meta = _decoder.InputMetadata[name];
        var dims = meta.Dimensions.ToArray();
        var shape = new int[dims.Length];
        for (var i = 0; i < dims.Length; i++)
            shape[i] = dims[i] > 0 ? dims[i] : 0;

        if (shape.Length > 0) shape[0] = 1;
        // The head dimension is fixed by the model even when the export marks it
        // dynamic; the sequence dimension is the one that must be zero.
        if (shape.Length == 4 && shape[1] == 0)
            shape[1] = InferHeadCount();

        return new DenseTensor<float>(shape);
    }

    private int InferHeadCount()
    {
        foreach (var (past, _) in _binding.CachePairs)
        {
            var dims = _decoder.InputMetadata[past].Dimensions;
            if (dims.Length == 4 && dims[1] > 0)
                return dims[1];
        }
        return 1;
    }

    /// <summary>Logits are [batch, sequence, vocab]; only the final position predicts
    /// the next token.</summary>
    internal static float[] LastPositionLogits(Tensor<float> logits)
    {
        var sequence = logits.Dimensions[1];
        var vocab = logits.Dimensions[2];
        var last = sequence - 1;
        var slice = new float[vocab];
        for (var v = 0; v < vocab; v++)
            slice[v] = logits[0, last, v];
        return slice;
    }
}
