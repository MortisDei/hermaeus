using Hermaeus.Core.Models;
using Hermaeus.Services.ProcessManagement;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// The embeddings server's physical batch is the largest input it will embed at
/// all. Anything larger is refused with "input (N tokens) is too large to
/// process. increase the physical batch size".
///
/// r14 2.4 pinned the pair to a hardcoded 512 to silence a startup clamp
/// warning. RAG chunks default to 1600 characters plus 320 of overlap, which is
/// 500 to 650 real tokens for prose and denser for code, so a large share of
/// every ingest was rejected. An owner's runtime log carried 846 of those
/// errors and nothing in the UI ever said so: ingestion reported success for
/// the chunks that happened to fit.
/// </summary>
public sealed class EmbeddingBatchSizeTests
{
    private static ServerConfig Embeddings(int contextSize, string extraArgs = "") => new()
    {
        Name = "Embeddings",
        ModelPath = "embed.gguf",
        Port = 8081,
        ContextSize = contextSize,
        EmbeddingsMode = true,
        ExtraArgs = extraArgs
    };

    private static string ValueAfter(IReadOnlyList<string> args, string flag)
    {
        var index = args.ToList().IndexOf(flag);
        Assert.True(index >= 0 && index + 1 < args.Count, $"{flag} should be present with a value");
        return args[index + 1];
    }

    [Fact]
    public void The_physical_batch_follows_the_context_size_rather_than_a_hardcoded_512()
    {
        var args = ServerProcessManager.BuildLaunchArguments(Embeddings(2048));

        Assert.Equal("2048", ValueAfter(args, "-ub"));
        Assert.Equal("2048", ValueAfter(args, "-b"));
    }

    /// <summary>
    /// The regression this file exists for: a chunk of roughly 1920 characters
    /// is around 600 tokens, and with the old hardcoded 512 the server refused
    /// it. The batch must cover what the chunker can actually produce.
    /// </summary>
    [Fact]
    public void The_default_chunk_size_fits_inside_the_batch_a_default_embeddings_server_gets()
    {
        // TargetChunkChars 1600 + OverlapChars 320, at a conservative 3
        // characters per token rather than the chunker's optimistic 4.
        const int worstCaseChunkTokens = (1600 + 320) / 3;

        var args = ServerProcessManager.BuildLaunchArguments(Embeddings(2048));

        Assert.True(int.Parse(ValueAfter(args, "-ub")) >= worstCaseChunkTokens,
            "the physical batch must be at least as large as the biggest chunk the chunker can emit, "
            + "or those chunks are silently refused by the server and never embedded");
    }

    [Fact]
    public void A_small_context_still_gets_at_least_the_previous_512()
    {
        var args = ServerProcessManager.BuildLaunchArguments(Embeddings(256));

        Assert.Equal("512", ValueAfter(args, "-ub"));
        Assert.Equal("512", ValueAfter(args, "-b"));
    }

    /// <summary>
    /// Equal values are what stops llama-server logging the clamp warning pair
    /// that r14 2.4 was chasing in the first place.
    /// </summary>
    [Fact]
    public void The_logical_and_physical_batch_stay_equal_so_no_clamp_warning_is_logged()
    {
        foreach (var contextSize in new[] { 256, 512, 2048, 8192 })
        {
            var args = ServerProcessManager.BuildLaunchArguments(Embeddings(contextSize));
            Assert.Equal(ValueAfter(args, "-b"), ValueAfter(args, "-ub"));
        }
    }

    [Theory]
    [InlineData("-ub 4096")]
    [InlineData("--ubatch-size 4096")]
    public void Extra_args_still_win(string extraArgs)
    {
        var args = ServerProcessManager.BuildLaunchArguments(Embeddings(2048, extraArgs));

        Assert.Equal(1, args.Count(a => a is "-ub" or "--ubatch-size"));
    }

    [Fact]
    public void A_chat_server_is_unaffected()
    {
        var args = ServerProcessManager.BuildLaunchArguments(new ServerConfig
        {
            Name = "Chat",
            ModelPath = "chat.gguf",
            ContextSize = 8192,
            EmbeddingsMode = false
        });

        Assert.DoesNotContain("-ub", args);
        Assert.DoesNotContain("-b", args);
    }
}
