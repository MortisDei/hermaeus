using System.Buffers.Binary;
using System.IO.Compression;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Hermaeus.Core.Models;
using Hermaeus.Services;
using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

public sealed class HuggingFaceArtworkTests
{
    private const string Revision = "0123456789abcdef0123456789abcdef01234567";
    private const string Repo = "org/repo";

    [Fact]
    public async Task Card_parser_keeps_a_bounded_thumbnail_and_ignores_other_card_fields()
    {
        var json = "{\"sha\":\"" + Revision + "\",\"cardData\":{\"license\":\"mit\",\"thumbnail\":\"art.png\",\"widget\":[{\"text\":\"not artwork\"}]}}";
        var client = NewClient(_ => Response(json));

        var card = await client.GetModelCardAsync(Repo);

        Assert.Equal("art.png", card!.Thumbnail);
    }

    [Fact]
    public async Task Card_parser_drops_a_thumbnail_over_the_utf8_bound()
    {
        var tooLarge = new string('é', 1025);
        var json = "{\"sha\":\"" + Revision + "\",\"cardData\":{\"thumbnail\":\"" + tooLarge + "\"}}";
        var client = NewClient(_ => Response(json));

        var card = await client.GetModelCardAsync(Repo);

        Assert.Null(card!.Thumbnail);
    }

    [Fact]
    public void Missing_or_non_immutable_revision_never_produces_a_fetch_descriptor()
    {
        var tree = new[] { new HfTreeEntry("art.png", 1, null) };
        var missing = HuggingFaceArtworkService.Describe(Repo, new HfModelCard("", null, null, null, "art.png"), tree);
        var malformed = HuggingFaceArtworkService.Describe(Repo, new HfModelCard("main", null, null, null, "art.png"), tree);

        Assert.Equal(HfArtworkState.Invalid, missing.State);
        Assert.Equal(HfArtworkState.Invalid, malformed.State);
        Assert.Null(missing.Descriptor);
        Assert.Null(malformed.Descriptor);
    }

    [Fact]
    public void Relative_thumbnail_must_exist_in_the_selected_tree_and_is_revision_pinned()
    {
        var plan = HuggingFaceArtworkService.Describe(
            Repo,
            new HfModelCard(Revision, null, null, null, "assets/art.png"),
            [new HfTreeEntry("assets/art.png", 10, null)]);

        Assert.Equal(HfArtworkState.Loading, plan.State);
        Assert.Equal("assets/art.png", plan.Descriptor!.RepositoryPath);
        Assert.Contains($"/resolve/{Revision}/assets/art.png", HuggingFaceClient.ResolveDownloadUrl(Repo, plan.Descriptor!.RepositoryPath!, Revision));
        Assert.DoesNotContain("main", plan.Descriptor.CacheKey, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("../art.png")]
    [InlineData("assets/../art.png")]
    [InlineData("assets/%2e%2e/art.png")]
    [InlineData("assets/%2Fart.png")]
    [InlineData("assets\\art.png")]
    [InlineData("assets/art.png?sig=secret")]
    [InlineData("assets/art.png#fragment")]
    public void Unsafe_relative_thumbnail_values_are_rejected(string value)
    {
        var plan = HuggingFaceArtworkService.Describe(
            Repo,
            new HfModelCard(Revision, null, null, null, value),
            [new HfTreeEntry(value, 10, null), new HfTreeEntry("art.png", 10, null)]);

        Assert.Equal(HfArtworkState.Invalid, plan.State);
        Assert.Null(plan.Descriptor);
    }

    [Fact]
    public void Absolute_same_repository_resolve_url_is_accepted_only_when_tree_pinned()
    {
        var value = $"https://huggingface.co/{Repo}/resolve/{Revision}/art.png";
        var plan = HuggingFaceArtworkService.Describe(
            Repo,
            new HfModelCard(Revision, null, null, null, value),
            [new HfTreeEntry("art.png", 10, null)]);

        Assert.Equal(HfArtworkSourceKind.RepositoryFile, plan.Descriptor!.SourceKind);
        Assert.Equal("art.png", plan.Descriptor.RepositoryPath);
    }

    [Theory]
    [InlineData("https://example.com/art.png", HfArtworkState.ExternalBlocked)]
    [InlineData("http://huggingface.co/art.png", HfArtworkState.Invalid)]
    [InlineData("https://huggingface.co/art.png?token=secret", HfArtworkState.Invalid)]
    [InlineData("https://user:hunter2@huggingface.co/art.png", HfArtworkState.Invalid)]
    [InlineData("https://huggingface.co:8443/art.png", HfArtworkState.Invalid)]
    public void Declared_absolute_url_policy_is_strict(string value, HfArtworkState expected)
    {
        var plan = HuggingFaceArtworkService.Describe(
            Repo,
            new HfModelCard(Revision, null, null, null, value),
            [new HfTreeEntry("art.png", 10, null)]);

        Assert.Equal(expected, plan.State);
        Assert.Null(plan.Descriptor);
    }

    [Fact]
    public async Task Relative_artwork_fetch_is_bounded_and_cache_hit_is_offline()
    {
        using var temp = new TempDir();
        var calls = 0;
        var handler = new RecordingHandler(_ =>
        {
            calls++;
            return Bytes(OnePixelPng(), "image/png");
        });
        var service = new HuggingFaceArtworkService(new HttpClient(handler));
        var card = new HfModelCard(Revision, null, null, null, "art.png");
        var tree = new[] { new HfTreeEntry("art.png", 10, null) };
        var cache = HuggingFaceArtworkCache.ResolveRoot(temp.PathFor("data"));
        var lookupKey = HuggingFaceArtworkService.Describe(Repo, card, tree).Descriptor!.CacheKey;

        var first = await service.FetchAsync(Repo, card, tree, cache);
        var second = await service.FetchAsync(Repo, card, tree, cache);

        Assert.Equal(HfArtworkState.Available, first.State);
        Assert.Equal(1, first.Width);
        Assert.Equal(1, first.Height);
        Assert.True(File.Exists(first.CachePath));
        Assert.Equal(HfArtworkState.Available, second.State);
        Assert.Equal(1, calls);
        Assert.Contains(first.ContentHash!, Path.GetFileName(first.CachePath!), StringComparison.Ordinal);
        Assert.DoesNotContain("resolve", await File.ReadAllTextAsync(Path.Combine(cache, lookupKey + ".json")));
    }

    [Fact]
    public async Task Fetch_uses_the_immutable_revision_in_the_request()
    {
        using var temp = new TempDir();
        var handler = new RecordingHandler(_ => Bytes(OnePixelPng(), "image/png"));
        var service = new HuggingFaceArtworkService(new HttpClient(handler));

        await service.FetchAsync(Repo, new HfModelCard(Revision, null, null, null, "art.png"),
            [new HfTreeEntry("art.png", 10, null)],
            HuggingFaceArtworkCache.ResolveRoot(temp.PathFor("data")));

        Assert.Contains($"/resolve/{Revision}/art.png", handler.RequestedUrls.Single());
    }

    [Fact]
    public async Task Declared_external_host_is_blocked_without_a_network_call()
    {
        using var temp = new TempDir();
        var handler = new RecordingHandler(_ => Bytes(OnePixelPng(), "image/png"));
        var service = new HuggingFaceArtworkService(new HttpClient(handler));

        var result = await service.FetchAsync(Repo, new HfModelCard(Revision, null, null, null, "https://example.com/art.png"),
            [], HuggingFaceArtworkCache.ResolveRoot(temp.PathFor("data")));

        Assert.Equal(HfArtworkState.ExternalBlocked, result.State);
        Assert.Empty(handler.RequestedUrls);
    }

    [Fact]
    public async Task Signed_delivery_redirect_is_allowed_but_the_query_is_not_cached()
    {
        using var temp = new TempDir();
        var handler = new RecordingHandler(request =>
        {
            if (request.RequestUri!.Host.Equals("huggingface.co", StringComparison.OrdinalIgnoreCase))
            {
                var response = new HttpResponseMessage(HttpStatusCode.Found);
                response.Headers.Location = new Uri("https://cdn-lfs-us-1.hf.co/object?X-Amz-Signature=secret");
                return response;
            }
            return Bytes(OnePixelPng(), "image/png");
        });
        var service = new HuggingFaceArtworkService(new HttpClient(handler));

        var result = await service.FetchAsync(Repo, new HfModelCard(Revision, null, null, null, "art.png"),
            [new HfTreeEntry("art.png", 10, null)],
            HuggingFaceArtworkCache.ResolveRoot(temp.PathFor("data")));

        Assert.Equal(HfArtworkState.Available, result.State);
        Assert.Equal("cdn-lfs-us-1.hf.co", new Uri(handler.RequestedUrls[1]).Host);
        Assert.Contains("Signature=secret", handler.RequestedUrls[1], StringComparison.Ordinal);
        var metadata = await File.ReadAllTextAsync(Path.Combine(
            HuggingFaceArtworkCache.ResolveRoot(temp.PathFor("data")),
            HuggingFaceArtworkService.Describe(Repo, new HfModelCard(Revision, null, null, null, "art.png"),
                [new HfTreeEntry("art.png", 10, null)]).Descriptor!.CacheKey + ".json"));
        Assert.DoesNotContain("X-Amz", metadata, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("secret", metadata, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Artwork_client_strips_default_authorization_before_fetching()
    {
        using var temp = new TempDir();
        var handler = new RecordingHandler(_ => Bytes(OnePixelPng(), "image/png"));
        using var http = new HttpClient(handler);
        http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "should-not-travel");
        var service = new HuggingFaceArtworkService(http);

        var result = await service.FetchAsync(Repo, new HfModelCard(Revision, null, null, null, "art.png"),
            [new HfTreeEntry("art.png", 10, null)],
            HuggingFaceArtworkCache.ResolveRoot(temp.PathFor("data")));

        Assert.Equal(HfArtworkState.Available, result.State);
        Assert.False(handler.SawAuthorization);
    }

    [Fact]
    public async Task Delivery_redirect_to_a_different_origin_is_rejected()
    {
        using var temp = new TempDir();
        var handler = new RecordingHandler(request =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = request.RequestUri!.Host == "huggingface.co"
                ? new Uri("https://cas-server.xethub.hf.co/object")
                : new Uri("https://example.com/object");
            return response;
        });
        var service = new HuggingFaceArtworkService(new HttpClient(handler));

        var result = await service.FetchAsync(Repo, new HfModelCard(Revision, null, null, null, "art.png"),
            [new HfTreeEntry("art.png", 10, null)],
            HuggingFaceArtworkCache.ResolveRoot(temp.PathFor("data")));

        Assert.Equal("delivery_origin_changed", result.FailureCode);
        Assert.Equal(2, handler.RequestedUrls.Count);
    }

    [Fact]
    public async Task Redirect_loop_stops_at_the_fixed_hop_limit()
    {
        using var temp = new TempDir();
        var handler = new RecordingHandler(_ =>
        {
            var response = new HttpResponseMessage(HttpStatusCode.Found);
            response.Headers.Location = new Uri("https://huggingface.co/loop");
            return response;
        });
        var service = new HuggingFaceArtworkService(new HttpClient(handler));

        var result = await service.FetchAsync(Repo, new HfModelCard(Revision, null, null, null, "art.png"),
            [new HfTreeEntry("art.png", 10, null)],
            HuggingFaceArtworkCache.ResolveRoot(temp.PathFor("data")));

        Assert.Equal("redirect_limit", result.FailureCode);
        Assert.Equal(HuggingFaceArtworkService.MaxRedirects + 1, handler.RequestedUrls.Count);
    }

    [Theory]
    [InlineData("image/jpeg")]
    [InlineData("image/webp")]
    public async Task Supported_formats_require_matching_magic_and_dimensions(string mime)
    {
        using var temp = new TempDir();
        var bytes = mime == "image/jpeg" ? OnePixelJpeg() : OnePixelWebp();
        var service = new HuggingFaceArtworkService(new HttpClient(new RecordingHandler(_ => Bytes(bytes, mime))));

        var result = await service.FetchAsync(Repo, new HfModelCard(Revision, null, null, null, "art.bin"),
            [new HfTreeEntry("art.bin", bytes.Length, null)],
            HuggingFaceArtworkCache.ResolveRoot(temp.PathFor("data")));

        Assert.Equal(HfArtworkState.Available, result.State);
        Assert.Equal(1, result.Width);
        Assert.Equal(1, result.Height);
    }

    [Theory]
    [InlineData("image/png", "image/jpeg")]
    [InlineData("text/html", "image/png")]
    [InlineData("image/gif", "image/gif")]
    public async Task Wrong_or_unsupported_mime_fails_before_cache_write(string declaredMime, string actualMime)
    {
        using var temp = new TempDir();
        var bytes = actualMime == "image/jpeg" ? OnePixelJpeg() : OnePixelPng();
        var service = new HuggingFaceArtworkService(new HttpClient(new RecordingHandler(_ => Bytes(bytes, declaredMime))));
        var cache = HuggingFaceArtworkCache.ResolveRoot(temp.PathFor("data"));

        var result = await service.FetchAsync(Repo, new HfModelCard(Revision, null, null, null, "art.png"),
            [new HfTreeEntry("art.png", bytes.Length, null)], cache);

        Assert.Equal(HfArtworkState.Invalid, result.State);
        Assert.False(Directory.Exists(cache) && Directory.EnumerateFiles(cache).Any());
    }

    [Fact]
    public async Task Oversized_response_is_rejected_from_headers_or_bounded_body()
    {
        using var temp = new TempDir();
        var bytes = new byte[HuggingFaceArtworkService.MaxArtworkBytes + 1];
        var service = new HuggingFaceArtworkService(new HttpClient(new RecordingHandler(_ =>
            Bytes(bytes, "image/png"))));

        var result = await service.FetchAsync(Repo, new HfModelCard(Revision, null, null, null, "art.png"),
            [new HfTreeEntry("art.png", bytes.Length, null)],
            HuggingFaceArtworkCache.ResolveRoot(temp.PathFor("data")));

        Assert.Equal(HfArtworkState.Invalid, result.State);
        Assert.Equal("content_length_limit", result.FailureCode);
    }

    [Fact]
    public void Preflight_rejects_animated_webp_and_oversized_dimensions_without_decoding()
    {
        var animated = OnePixelWebp(animate: true);
        var oversized = PngHeader(5000, 1);

        Assert.False(ArtworkImagePreflight.TryRead(animated, "image/webp", out _, out var animationFailure));
        Assert.Equal("animated_webp", animationFailure);
        Assert.False(ArtworkImagePreflight.TryRead(oversized, "image/png", out _, out var dimensionFailure));
        Assert.Equal("dimension_limit", dimensionFailure);
    }

    [Fact]
    public async Task Corrupt_cache_is_ignored_and_replaced_by_a_fresh_fetch()
    {
        using var temp = new TempDir();
        var calls = 0;
        var service = new HuggingFaceArtworkService(new HttpClient(new RecordingHandler(_ =>
        {
            calls++;
            return Bytes(OnePixelPng(), "image/png");
        })));
        var card = new HfModelCard(Revision, null, null, null, "art.png");
        var tree = new[] { new HfTreeEntry("art.png", 10, null) };
        var cache = HuggingFaceArtworkCache.ResolveRoot(temp.PathFor("data"));

        var first = await service.FetchAsync(Repo, card, tree, cache);
        await File.WriteAllTextAsync(first.CachePath!, "corrupt");
        var second = await service.FetchAsync(Repo, card, tree, cache);

        Assert.Equal(HfArtworkState.Available, second.State);
        Assert.Equal(2, calls);
    }

    [Fact]
    public async Task Malformed_cache_metadata_is_not_treated_as_verified_artwork()
    {
        using var temp = new TempDir();
        var service = new HuggingFaceArtworkService(new HttpClient(new RecordingHandler(_ =>
            Bytes(OnePixelPng(), "image/png"))));
        var card = new HfModelCard(Revision, null, null, null, "art.png");
        var tree = new[] { new HfTreeEntry("art.png", 10, null) };
        var cache = HuggingFaceArtworkCache.ResolveRoot(temp.PathFor("data"));

        await service.FetchAsync(Repo, card, tree, cache);
        var metadataPath = Directory.EnumerateFiles(cache, "*.json").Single();
        var metadata = await File.ReadAllTextAsync(metadataPath);
        await File.WriteAllTextAsync(metadataPath,
            metadata.Replace("\"cacheKey\":\"", "\"cacheKey\":\"../", StringComparison.Ordinal));

        var result = await service.TryGetCachedAsync(Repo, Revision, cache);

        Assert.Null(result);
    }

    [Fact]
    public async Task Verified_manifest_artwork_reuse_is_offline_during_model_inventory_refresh()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var assets = temp.PathFor("assets");
        var modelPath = Path.Combine(assets, "Models", "llm", "org__repo", "model.gguf");
        var modelBytes = Encoding.UTF8.GetBytes("model");
        Directory.CreateDirectory(Path.GetDirectoryName(modelPath)!);
        await File.WriteAllBytesAsync(modelPath, modelBytes);
        settings.Settings.DataManagement.LocalAiAssetsRoot = assets;

        var cache = HuggingFaceArtworkCache.ResolveRoot(SettingsService.ResolveDataRoot(settings.Settings));
        var card = new HfModelCard(Revision, null, null, null, "art.png");
        var tree = new[] { new HfTreeEntry("art.png", 10, null) };
        var online = new HuggingFaceArtworkService(new HttpClient(new RecordingHandler(_ =>
            Bytes(OnePixelPng(), "image/png"))));
        var stored = await online.FetchAsync(Repo, card, tree, cache);
        var manifest = new ModelManifestStore(settings);
        await manifest.UpsertAsync(new ModelManifestEntry
        {
            FilePath = modelPath,
            RepoId = Repo,
            RepoFile = "model.gguf",
            RevisionSha = Revision,
            Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(modelBytes)),
            SizeBytes = modelBytes.Length,
            Source = "hf-browser"
        });

        var offlineHandler = new RecordingHandler(_ => throw new InvalidOperationException("network was not expected"));
        var vm = new ModelManagementViewModel(
            new ScriptedModelsLlm(() => []),
            new ModelProfileService(settings),
            new FakeToasts(),
            settings,
            new FakeSystemInfo(),
            Helpers.NewServicesViewModel(settings),
            manifest,
            new HuggingFaceClient(),
            new ModelDownloadService(),
            artwork: new HuggingFaceArtworkService(new HttpClient(offlineHandler)));

        await vm.RefreshAsync();

        var item = Assert.Single(vm.Models);
        Assert.Equal(HfArtworkState.Available, item.ArtworkState);
        Assert.Equal(stored.CachePath, item.ArtworkPath);
        Assert.Empty(offlineHandler.RequestedUrls);
    }

    [Fact]
    public async Task Clear_artwork_cache_leaves_model_and_manifest_siblings_untouched()
    {
        using var temp = new TempDir();
        var data = temp.PathFor("data");
        var cache = HuggingFaceArtworkCache.ResolveRoot(data);
        Directory.CreateDirectory(cache);
        var model = Path.Combine(data, "model-manifest.json");
        var modelFile = Path.Combine(data, "model.gguf");
        Directory.CreateDirectory(data);
        await File.WriteAllTextAsync(model, "manifest");
        await File.WriteAllTextAsync(modelFile, "model");
        await File.WriteAllTextAsync(Path.Combine(cache, "art.png"), "art");

        await HuggingFaceArtworkCache.ClearAsync(cache);

        Assert.Equal("manifest", await File.ReadAllTextAsync(model));
        Assert.Equal("model", await File.ReadAllTextAsync(modelFile));
        Assert.Empty(Directory.EnumerateFiles(cache));
    }

    [Fact]
    public async Task Data_root_backup_excludes_rebuildable_artwork_but_keeps_workspace_state()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var data = SettingsService.ResolveDataRoot(settings.Settings);
        var cache = HuggingFaceArtworkCache.ResolveRoot(data);
        Directory.CreateDirectory(cache);
        await File.WriteAllTextAsync(Path.Combine(cache, "art.png"), "art");
        await File.WriteAllTextAsync(Path.Combine(data, "workspace.json"), "state");

        var backup = await new BackupService(settings).BackupAsync(temp.PathFor("backups"));

        using var archive = ZipFile.OpenRead(backup.Path);
        Assert.Null(archive.GetEntry("cache/huggingface-artwork/art.png"));
        Assert.NotNull(archive.GetEntry("workspace.json"));
    }

    [Fact]
    public async Task Cache_info_counts_only_artwork_cache_files()
    {
        using var temp = new TempDir();
        var cache = HuggingFaceArtworkCache.ResolveRoot(temp.PathFor("data"));
        Directory.CreateDirectory(cache);
        await File.WriteAllTextAsync(Path.Combine(cache, "one.png"), "123");
        await File.WriteAllTextAsync(Path.Combine(cache, "one.json"), "{}");

        var info = await HuggingFaceArtworkCache.GetInfoAsync(cache);

        Assert.Equal(5, info.ByteCount);
        Assert.Equal(1, info.EntryCount);
    }

    [Fact]
    public async Task Selecting_a_repository_publishes_one_artwork_result_to_all_its_file_cards()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var image = OnePixelPng();
        var model = Encoding.UTF8.GetBytes("not a real GGUF");
        var modelHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(model));
        var cardJson = "{\"sha\":\"" + Revision + "\",\"cardData\":{\"license\":\"mit\",\"thumbnail\":\"art.png\"}}";
        var treeJson = "[{\"path\":\"model.gguf\",\"size\":" + model.Length + ",\"lfs\":{\"oid\":\"" + modelHash + "\"}},{\"path\":\"art.png\",\"size\":" + image.Length + "}]";
        var handler = new RecordingHandler(request =>
        {
            var url = request.RequestUri!.ToString();
            if (url.Contains("/tree/", StringComparison.Ordinal))
                return Response(treeJson);
            if (url.Contains("/api/models/", StringComparison.Ordinal))
                return Response(cardJson);
            return url.EndsWith("/art.png", StringComparison.Ordinal)
                ? Bytes(image, "image/png")
                : Bytes(model, "application/octet-stream");
        });
        var hf = NewClient(request => handler.Route(request));
        var artwork = new HuggingFaceArtworkService(new HttpClient(handler));
        var vm = new ModelManagementViewModel(
            new ScriptedModelsLlm(() => []),
            new ModelProfileService(settings),
            new FakeToasts(),
            settings,
            new FakeSystemInfo(),
            Helpers.NewServicesViewModel(settings),
            new ModelManifestStore(settings),
            hf,
            new ModelDownloadService(),
            artwork: artwork);
        var repo = new HfRepoResultViewModel(Repo, 100);

        await vm.SelectHfRepoCommand.ExecuteAsync(repo);
        await Helpers.WaitForAsync(
            () => repo.ArtworkState == HfArtworkState.Available,
            $"repository artwork (state={repo.ArtworkState}, code={repo.ArtworkFailureCode}, urls={string.Join(",", handler.RequestedUrls)})");

        var file = Assert.Single(vm.HfFiles);
        Assert.True(repo.HasArtwork);
        Assert.Equal(repo.ArtworkPath, file.ArtworkPath);
        Assert.Equal(Revision, repo.RevisionSha);
    }

    [Fact]
    public async Task Rapid_repository_selection_cannot_publish_stale_artwork()
    {
        using var temp = new TempDir();
        var settings = Helpers.NewSettings(temp);
        var image = OnePixelPng();
        var model = Encoding.UTF8.GetBytes("not a real GGUF");
        var modelHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(model));
        var firstArtworkStarted = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var handler = new AsyncRecordingHandler(async (request, ct) =>
        {
            var url = request.RequestUri!.ToString();
            var repo = url.Contains("repo-a", StringComparison.Ordinal) ? "repo-a" : "repo-b";
            if (url.Contains("/tree/", StringComparison.Ordinal))
            {
                var tree = "[{\"path\":\"model.gguf\",\"size\":" + model.Length + ",\"lfs\":{\"oid\":\"" + modelHash + "\"}},{\"path\":\"art.png\",\"size\":" + image.Length + "}]";
                return Response(tree);
            }
            if (url.Contains("/api/models/", StringComparison.Ordinal))
                return Response("{\"sha\":\"" + Revision + "\",\"cardData\":{\"license\":\"mit\",\"thumbnail\":\"art.png\"}}");
            if (url.Contains("/resolve/", StringComparison.Ordinal) && url.EndsWith("/art.png", StringComparison.Ordinal))
            {
                if (repo == "repo-a")
                {
                    firstArtworkStarted.TrySetResult(true);
                    await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                }
                return Bytes(image, "image/png");
            }
            return Bytes(model, "application/octet-stream");
        });
        var http = new HttpClient(handler);
        var vm = new ModelManagementViewModel(
            new ScriptedModelsLlm(() => []),
            new ModelProfileService(settings),
            new FakeToasts(),
            settings,
            new FakeSystemInfo(),
            Helpers.NewServicesViewModel(settings),
            new ModelManifestStore(settings),
            new HuggingFaceClient(http),
            new ModelDownloadService(),
            artwork: new HuggingFaceArtworkService(http));
        var firstRepo = new HfRepoResultViewModel("org/repo-a", 100);
        var secondRepo = new HfRepoResultViewModel("org/repo-b", 90);

        var firstSelection = vm.SelectHfRepoCommand.ExecuteAsync(firstRepo);
        await firstArtworkStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));
        await vm.SelectHfRepoCommand.ExecuteAsync(secondRepo);
        try { await firstSelection; } catch (OperationCanceledException) { }
        await Helpers.WaitForAsync(() => secondRepo.ArtworkState == HfArtworkState.Available, "second repository artwork");

        Assert.Same(secondRepo, vm.SelectedHfRepo);
        Assert.NotNull(secondRepo.ArtworkPath);
        Assert.Null(firstRepo.ArtworkPath);
    }

    private static HuggingFaceClient NewClient(Func<HttpRequestMessage, HttpResponseMessage> route) =>
        new(new HttpClient(new RecordingHandler(route)));

    private static HttpResponseMessage Response(string json)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
        return response;
    }

    private static HttpResponseMessage Bytes(byte[] bytes, string mime)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(bytes)
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mime);
        response.Content.Headers.ContentLength = bytes.Length;
        return response;
    }

    private static byte[] OnePixelPng() =>
        Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAQAAAC1HAwCAAAAC0lEQVR42mNk+A8AAQUBAScY42YAAAAASUVORK5CYII=");

    private static byte[] OnePixelJpeg() =>
    [
        0xff, 0xd8, 0xff, 0xc0, 0x00, 0x0b, 0x08, 0x00, 0x01, 0x00, 0x01, 0x01, 0x01, 0x11, 0x00,
        0xff, 0xd9
    ];

    private static byte[] OnePixelWebp(bool animate = false)
    {
        var bytes = new byte[animate ? 38 : 30];
        Encoding.ASCII.GetBytes("RIFF").CopyTo(bytes, 0);
        BitConverter.GetBytes(bytes.Length - 8).CopyTo(bytes, 4);
        Encoding.ASCII.GetBytes("WEBP").CopyTo(bytes, 8);
        if (!animate)
        {
            Encoding.ASCII.GetBytes("VP8 ").CopyTo(bytes, 12);
            BitConverter.GetBytes(10).CopyTo(bytes, 16);
            bytes[23] = 0x9d; bytes[24] = 0x01; bytes[25] = 0x2a;
            bytes[26] = 1; bytes[28] = 1;
        }
        else
        {
            Encoding.ASCII.GetBytes("VP8X").CopyTo(bytes, 12);
            BitConverter.GetBytes(10).CopyTo(bytes, 16);
            bytes[20] = 0x02;
        }
        return bytes;
    }

    private static byte[] PngHeader(int width, int height)
    {
        var bytes = new byte[45];
        Convert.FromHexString("89504E470D0A1A0A").CopyTo(bytes, 0);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8, 4), 13);
        Encoding.ASCII.GetBytes("IHDR").CopyTo(bytes, 12);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(16, 4), (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(20, 4), (uint)height);
        Encoding.ASCII.GetBytes("IEND").CopyTo(bytes, 37);
        return bytes;
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> route) : HttpMessageHandler
    {
        public List<string> RequestedUrls { get; } = [];
        public bool SawAuthorization { get; private set; }

        public HttpResponseMessage Route(HttpRequestMessage request) => route(request);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestedUrls.Add(request.RequestUri!.ToString());
            SawAuthorization |= request.Headers.Authorization is not null
                || request.Headers.Contains("Authorization")
                || request.Headers.Contains("Cookie");
            return Task.FromResult(Route(request));
        }
    }

    private sealed class AsyncRecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> route) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            await route(request, cancellationToken);
    }
}
