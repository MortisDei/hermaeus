using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Rag.Models;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Microsoft.ML.Tokenizers;

namespace Aether.Rag.Retrieval;

public sealed class OnnxCrossEncoderReranker : IReranker, IDisposable
{
    private const string ModelUrl = "https://huggingface.co/cross-encoder/ms-marco-MiniLM-L6-v2/resolve/main/onnx/model_O4.onnx";
    private const string VocabUrl = "https://huggingface.co/cross-encoder/ms-marco-MiniLM-L6-v2/resolve/main/vocab.txt";
    private const string ModelFileName = "model_O4.onnx";
    private const string VocabFileName = "vocab.txt";

    private readonly ISettingsService _settings;
    private readonly HttpClient _http = new() { Timeout = TimeSpan.FromMinutes(10) };
    private readonly SemaphoreSlim _gate = new(1, 1);
    private InferenceSession? _session;
    private BertTokenizer? _tokenizer;
    private bool _unavailable;

    public OnnxCrossEncoderReranker(ISettingsService settings)
    {
        _settings = settings;
    }

    public async Task<List<ScoredChunk>> RerankAsync(
        string query,
        IReadOnlyList<ScoredChunk> candidates,
        int topK,
        CancellationToken ct = default)
    {
        if (!_settings.Settings.Rag.RerankerEnabled || _unavailable || candidates.Count == 0)
            return candidates.Take(topK).ToList();

        var loaded = await EnsureLoadedAsync(ct);
        if (!loaded || _session is null || _tokenizer is null)
            return candidates.Take(topK).ToList();

        var maxCandidates = Math.Clamp(_settings.Settings.Rag.RerankerMaxCandidates, topK, 100);
        var maxLength = Math.Clamp(_settings.Settings.Rag.RerankerMaxLength, 64, 512);
        var reranked = new List<ScoredChunk>();

        foreach (var candidate in candidates.Take(maxCandidates))
        {
            ct.ThrowIfCancellationRequested();
            var score = ScorePair(query, candidate.Chunk.Content, maxLength);
            reranked.Add(candidate with { Score = score, Source = ScoreSource.Reranker });
        }

        var originalRanks = candidates
            .Select((candidate, index) => new { candidate.Chunk.Id, index })
            .ToDictionary(x => x.Id, x => x.index);

        return reranked
            .OrderByDescending(x => x.Score)
            .ThenBy(x => originalRanks.GetValueOrDefault(x.Chunk.Id, int.MaxValue))
            .Take(topK)
            .ToList();
    }

    private async Task<bool> EnsureLoadedAsync(CancellationToken ct)
    {
        if (_session is not null && _tokenizer is not null)
            return true;

        await _gate.WaitAsync(ct);
        try
        {
            if (_session is not null && _tokenizer is not null)
                return true;

            var modelDir = ResolveModelDirectory(_settings.Settings);
            var modelPath = Path.Combine(modelDir, ModelFileName);
            var vocabPath = Path.Combine(modelDir, VocabFileName);

            if ((!File.Exists(modelPath) || !File.Exists(vocabPath)) && !_settings.Settings.Rag.RerankerAutoDownload)
            {
                _unavailable = true;
                return false;
            }

            Directory.CreateDirectory(modelDir);
            await DownloadIfMissingAsync(modelPath, ModelUrl, ct);
            await DownloadIfMissingAsync(vocabPath, VocabUrl, ct);

            _tokenizer = BertTokenizer.Create(vocabPath);
            _session = new InferenceSession(modelPath);
            return true;
        }
        catch
        {
            _unavailable = true;
            _session?.Dispose();
            _session = null;
            _tokenizer = null;
            return false;
        }
        finally
        {
            _gate.Release();
        }
    }

    private float ScorePair(string query, string passage, int maxLength)
    {
        var encoded = EncodePair(query, passage, maxLength);
        var shape = new[] { 1, maxLength };
        var inputs = new List<NamedOnnxValue>
        {
            NamedOnnxValue.CreateFromTensor("input_ids", new DenseTensor<long>(encoded.InputIds, shape)),
            NamedOnnxValue.CreateFromTensor("attention_mask", new DenseTensor<long>(encoded.AttentionMask, shape)),
            NamedOnnxValue.CreateFromTensor("token_type_ids", new DenseTensor<long>(encoded.TokenTypeIds, shape))
        };

        using var results = _session!.Run(inputs);
        var output = results.First().AsEnumerable<float>().FirstOrDefault();
        return Sigmoid(output);
    }

    private EncodedPair EncodePair(string query, string passage, int maxLength)
    {
        var tokenizer = _tokenizer!;
        var queryBudget = Math.Min(64, maxLength / 3);
        var passageBudget = Math.Max(8, maxLength - queryBudget - 3);
        var queryIds = tokenizer.EncodeToIds(query, queryBudget, addSpecialTokens: false, out _, out _).ToArray();
        var passageIds = tokenizer.EncodeToIds(passage, passageBudget, addSpecialTokens: false, out _, out _).ToArray();

        var inputIds = new long[maxLength];
        var attentionMask = new long[maxLength];
        var tokenTypeIds = new long[maxLength];
        var pos = 0;

        Add(tokenizer.ClassificationTokenId, segment: 0);
        foreach (var id in queryIds) Add(id, segment: 0);
        Add(tokenizer.SeparatorTokenId, segment: 0);
        foreach (var id in passageIds) Add(id, segment: 1);
        Add(tokenizer.SeparatorTokenId, segment: 1);

        return new EncodedPair(inputIds, attentionMask, tokenTypeIds);

        void Add(int id, int segment)
        {
            if (pos >= maxLength)
                return;

            inputIds[pos] = id;
            attentionMask[pos] = 1;
            tokenTypeIds[pos] = segment;
            pos++;
        }
    }

    private async Task DownloadIfMissingAsync(string path, string url, CancellationToken ct)
    {
        if (File.Exists(path))
            return;

        var temp = $"{path}.download";
        using var response = await _http.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();
        await using (var source = await response.Content.ReadAsStreamAsync(ct))
        await using (var target = File.Create(temp))
            await source.CopyToAsync(target, ct);

        File.Move(temp, path, overwrite: true);
    }

    private static string ResolveModelDirectory(AppSettings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.Rag.RerankerModelPath))
            return Path.GetFullPath(settings.Rag.RerankerModelPath);

        var configured = settings.DataManagement.DataRootDirectory?.Trim();
        var root = string.IsNullOrWhiteSpace(configured)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aether")
            : Path.GetFullPath(configured);
        return Path.Combine(root, "models", "rerank", "ms-marco-MiniLM-L6-v2");
    }

    private static float Sigmoid(float value)
    {
        if (value >= 0)
        {
            var z = MathF.Exp(-value);
            return 1f / (1f + z);
        }

        var neg = MathF.Exp(value);
        return neg / (1f + neg);
    }

    public void Dispose()
    {
        _session?.Dispose();
        _http.Dispose();
        _gate.Dispose();
    }

    private sealed record EncodedPair(long[] InputIds, long[] AttentionMask, long[] TokenTypeIds);
}
