using System.Net;
using Aether.Services;
using Xunit;

namespace Aether.Tests;

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
