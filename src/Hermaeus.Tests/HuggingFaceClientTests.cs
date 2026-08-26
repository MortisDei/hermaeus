using System.Net;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

// r13 03-hugging-face.md: fixture-driven tests for the HF API client. Fixtures below mirror
// the live response shapes verified against huggingface.co during implementation.
public sealed class HuggingFaceClientTests
{
    private const string ModelCardFixture = """
        {"id":"TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF","sha":"52e7645ba7c309695bec7ac98f4f005b139cf465",
        "lastModified":"2023-12-31T21:29:33.000Z","downloads":178102,
        "cardData":{"license":"apache-2.0"}}
        """;

    private const string TreeFixture = """
        [{"type":"file","oid":"b2ffcdb64ce4658c43c27ea847e39bf929921847","size":33,"path":"config.json"},
         {"type":"file","oid":"d23f37326d7a802759db7a0d4aa39dd4b92ff9f3","size":483116416,
          "lfs":{"oid":"030a469a63576d59f601ef5608846b7718eaa884dd820e9aa7493efec1788afa","size":483116416},
          "path":"tinyllama-1.1b-chat-v1.0.Q2_K.gguf"}]
        """;

    private const string SearchFixture = """
        [{"id":"TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF","downloads":178102},
         {"id":"ggml-org/tiny-llamas","downloads":432}]
        """;

    private sealed class RoutedFakeHandler : HttpMessageHandler
    {
        private readonly Func<string, (HttpStatusCode Status, string Body)> _route;
        public int CallCount { get; private set; }
        public List<string> RequestedUrls { get; } = [];

        public RoutedFakeHandler(Func<string, (HttpStatusCode, string)> route) => _route = route;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            CallCount++;
            var url = request.RequestUri!.ToString();
            RequestedUrls.Add(url);
            var (status, body) = _route(url);
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    private sealed class RoutedBytesHandler(Func<string, byte[]> route) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var bytes = route(request.RequestUri!.ToString());
            var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(bytes) };
            response.Content.Headers.ContentLength = bytes.Length;
            return Task.FromResult(response);
        }
    }

    private static byte[] GgufModel(string architecture, int vocabulary, string tokenizerModel, string tokenizerPre)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
        writer.Write((uint)3);
        writer.Write((ulong)0);
        writer.Write((ulong)5);
        WriteString(writer, "general.architecture", architecture);
        WriteU32(writer, $"{architecture}.vocab_size", (uint)vocabulary);
        WriteString(writer, "tokenizer.ggml.model", tokenizerModel);
        WriteString(writer, "tokenizer.ggml.pre", tokenizerPre);
        WriteU32(writer, $"{architecture}.block_count", 1);
        return stream.ToArray();
    }

    private static byte[] GgufProjector()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
        writer.Write((uint)3);
        writer.Write((ulong)0);
        writer.Write((ulong)2);
        WriteString(writer, "general.architecture", "clip");
        WriteString(writer, "general.type", "clip");
        return stream.ToArray();
    }

    private static byte[] GgufMtp(string architecture, int vocabulary, string tokenizerModel, string tokenizerPre)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream);
        writer.Write(System.Text.Encoding.ASCII.GetBytes("GGUF"));
        writer.Write((uint)3);
        writer.Write((ulong)0);
        writer.Write((ulong)6);
        WriteString(writer, "general.architecture", architecture);
        WriteU32(writer, $"{architecture}.vocab_size", (uint)vocabulary);
        WriteString(writer, "tokenizer.ggml.model", tokenizerModel);
        WriteString(writer, "tokenizer.ggml.pre", tokenizerPre);
        WriteU32(writer, $"{architecture}.nextn_predict_layers", 1);
        WriteU32(writer, $"{architecture}.block_count", 1);
        return stream.ToArray();
    }

    private static void WriteString(BinaryWriter writer, string key, string value)
    {
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
        var valueBytes = System.Text.Encoding.UTF8.GetBytes(value);
        writer.Write((ulong)keyBytes.Length);
        writer.Write(keyBytes);
        writer.Write((uint)8);
        writer.Write((ulong)valueBytes.Length);
        writer.Write(valueBytes);
    }

    private static void WriteU32(BinaryWriter writer, string key, uint value)
    {
        var keyBytes = System.Text.Encoding.UTF8.GetBytes(key);
        writer.Write((ulong)keyBytes.Length);
        writer.Write(keyBytes);
        writer.Write((uint)4);
        writer.Write(value);
    }

    private static HuggingFaceClient NewClient(Func<string, (HttpStatusCode, string)> route) =>
        new(new HttpClient(new RoutedFakeHandler(route)) { Timeout = TimeSpan.FromSeconds(5) });

    [Fact]
    public async Task GetModelCardAsync_parses_sha_lastModified_license_and_downloads()
    {
        var client = NewClient(_ => (HttpStatusCode.OK, ModelCardFixture));

        var card = await client.GetModelCardAsync("TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF");

        Assert.NotNull(card);
        Assert.Equal("52e7645ba7c309695bec7ac98f4f005b139cf465", card!.Sha);
        Assert.Equal("apache-2.0", card.License);
        Assert.Equal(178102, card.Downloads);
        Assert.NotNull(card.LastModified);
    }

    [Fact]
    public async Task GetModelCardAsync_returns_null_on_404()
    {
        var client = NewClient(_ => (HttpStatusCode.NotFound, ""));

        var card = await client.GetModelCardAsync("nobody/nothing");

        Assert.Null(card);
    }

    [Fact]
    public async Task GetModelCardAsync_returns_null_on_malformed_json_instead_of_throwing()
    {
        var client = NewClient(_ => (HttpStatusCode.OK, "{not json"));

        var card = await client.GetModelCardAsync("a/b");

        Assert.Null(card);
    }

    [Fact]
    public async Task GetModelCardAsync_rejects_a_repo_id_that_is_not_org_slash_repo()
    {
        var client = NewClient(_ => (HttpStatusCode.OK, ModelCardFixture));

        await Assert.ThrowsAsync<ArgumentException>(() => client.GetModelCardAsync("not-a-repo-id"));
    }

    [Fact]
    public async Task GetTreeAsync_extracts_gguf_size_and_lfs_oid()
    {
        var client = NewClient(_ => (HttpStatusCode.OK, TreeFixture));

        var tree = await client.GetTreeAsync("TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF");

        Assert.NotNull(tree);
        Assert.Equal(2, tree!.Count);
        var gguf = tree.Single(e => e.Path.EndsWith(".gguf", StringComparison.Ordinal));
        Assert.Equal(483116416, gguf.SizeBytes);
        Assert.Equal("030a469a63576d59f601ef5608846b7718eaa884dd820e9aa7493efec1788afa", gguf.LfsSha256);
        var nonLfs = tree.Single(e => e.Path == "config.json");
        Assert.Null(nonLfs.LfsSha256);
    }

    [Fact]
    public async Task GetTreeAsync_returns_null_on_failure_distinct_from_an_empty_repo()
    {
        var client = NewClient(_ => (HttpStatusCode.NotFound, ""));

        var tree = await client.GetTreeAsync("nobody/nothing");

        Assert.Null(tree);
    }

    [Fact]
    public async Task GetCompanionMetadataAsync_accepts_only_a_hash_verified_explicit_mapping()
    {
        const string metadata = "{\"models\":[{\"model_path\":\"model.gguf\",\"companions\":[{\"path\":\"mmproj.gguf\",\"role\":\"projector\"},{\"path\":\"mtp.gguf\",\"role\":\"draft_head\"}]}]}";
        var metadataBytes = System.Text.Encoding.UTF8.GetBytes(metadata);
        var metadataHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(metadataBytes));
        var client = NewClient(url => url.Contains(".hermaeus/companions.json", StringComparison.Ordinal)
            ? (HttpStatusCode.OK, metadata)
            : (HttpStatusCode.OK, ""));
        var tree = new[]
        {
            new HfTreeEntry(".hermaeus/companions.json", metadataBytes.Length, metadataHash),
            new HfTreeEntry("model.gguf", 10, "model-hash"),
            new HfTreeEntry("mmproj.gguf", 20, "projector-hash"),
            new HfTreeEntry("mtp.gguf", 30, "draft-hash")
        };

        var mappings = await client.GetCompanionMetadataAsync("org/repo", tree);

        Assert.Equal(2, mappings.Count);
        Assert.Contains(mappings, m => m.Role == ModelFileRole.Projector && m.CompanionPath == "mmproj.gguf");
        Assert.Contains(mappings, m => m.Role == ModelFileRole.DraftHead && m.CompanionPath == "mtp.gguf");
    }

    [Fact]
    public async Task GetCompanionMetadataAsync_rejects_metadata_without_a_tree_hash()
    {
        const string metadata = "{\"models\":[]}";
        var client = NewClient(_ => (HttpStatusCode.OK, metadata));
        var tree = new[] { new HfTreeEntry(".hermaeus/companions.json", metadata.Length, null) };

        var mappings = await client.GetCompanionMetadataAsync("org/repo", tree);

        Assert.Empty(mappings);
    }

    [Fact]
    public async Task Fallback_resolves_existing_mmproj_and_mtp_layout_without_a_Hermaeus_manifest()
    {
        var modelBytes = GgufModel("gemma3", 128, "llama-bpe", "llama3");
        var projectorBytes = GgufProjector();
        var mtpBytes = GgufMtp("gemma3", 128, "llama-bpe", "llama3");
        var model = GgufMetadataReader.TryRead(modelBytes);
        Assert.NotNull(model);

        var client = new HuggingFaceClient(new HttpClient(new RoutedBytesHandler(url =>
            url.Contains("mmproj-F16.gguf", StringComparison.Ordinal) ? projectorBytes
            : url.Contains("MTP/mtp-model.gguf", StringComparison.Ordinal) ? mtpBytes
            : [])));
        var tree = new[]
        {
            new HfTreeEntry("model.gguf", modelBytes.Length, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(modelBytes)), "0123456789abcdef0123456789abcdef01234567"),
            new HfTreeEntry("mmproj-F16.gguf", projectorBytes.Length, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(projectorBytes)), "0123456789abcdef0123456789abcdef01234567"),
            new HfTreeEntry("MTP/mtp-model.gguf", mtpBytes.Length, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(mtpBytes)), "0123456789abcdef0123456789abcdef01234567")
        };

        var mappings = await client.ResolveCompanionDeclarationsAsync("org/repo", tree, "model.gguf", model, "0123456789abcdef0123456789abcdef01234567");

        Assert.Equal(2, mappings.Count);
        Assert.All(mappings, mapping => Assert.True(mapping.AutoSelect));
        Assert.Contains(mappings, mapping => mapping.Role == ModelFileRole.Projector && mapping.CompanionPath == "mmproj-F16.gguf");
        Assert.Contains(mappings, mapping => mapping.Role == ModelFileRole.DraftHead && mapping.CompanionPath == "MTP/mtp-model.gguf");
    }

    [Fact]
    public async Task Fallback_keeps_ambiguous_projector_candidates_reviewable()
    {
        var modelBytes = GgufModel("gemma3", 128, "llama-bpe", "llama3");
        var projectorBytes = GgufProjector();
        var model = GgufMetadataReader.TryRead(modelBytes);
        Assert.NotNull(model);

        var client = new HuggingFaceClient(new HttpClient(new RoutedBytesHandler(_ => projectorBytes)));
        var hash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(projectorBytes));
        var tree = new[]
        {
            new HfTreeEntry("model.gguf", modelBytes.Length, Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(modelBytes))),
            new HfTreeEntry("mmproj-F16.gguf", projectorBytes.Length, hash),
            new HfTreeEntry("mmproj-Q8_0.gguf", projectorBytes.Length, hash)
        };

        var mappings = await client.ResolveCompanionDeclarationsAsync("org/repo", tree, "model.gguf", model);

        Assert.Equal(2, mappings.Count);
        Assert.All(mappings, mapping => Assert.False(mapping.AutoSelect));
        Assert.All(mappings, mapping => Assert.Equal(HfCompanionEvidence.ReviewRequired, mapping.Evidence));

        var set = ModelFileSetResolver.Resolve("org/repo", tree, "model.gguf", mappings);
        Assert.All(set.Optional, entry => Assert.False(entry.SelectedByDefault));
    }

    [Fact]
    public async Task SearchAsync_extracts_repo_id_and_downloads()
    {
        var client = NewClient(_ => (HttpStatusCode.OK, SearchFixture));

        var results = await client.SearchAsync("tinyllama");

        Assert.Equal(2, results.Count);
        Assert.Equal("TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF", results[0].RepoId);
        Assert.Equal(178102, results[0].Downloads);
    }

    [Fact]
    public async Task No_network_calls_happen_at_construction_time()
    {
        var handler = new RoutedFakeHandler(_ => (HttpStatusCode.OK, ModelCardFixture));
        _ = new HuggingFaceClient(new HttpClient(handler));

        Assert.Equal(0, handler.CallCount);
    }

    [Fact]
    public void ResolveDownloadUrl_builds_the_resolve_main_url()
    {
        var url = HuggingFaceClient.ResolveDownloadUrl("TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF", "tinyllama-1.1b-chat-v1.0.Q2_K.gguf");

        Assert.Equal("https://huggingface.co/TheBloke/TinyLlama-1.1B-Chat-v1.0-GGUF/resolve/main/tinyllama-1.1b-chat-v1.0.Q2_K.gguf", url);
    }
}
