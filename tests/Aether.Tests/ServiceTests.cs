using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using Aether.Core.Models;
using Aether.Core.Services;
using Aether.Desktop.Controls;
using Aether.Services;
using Aether.Services.ProcessManagement;
using Aether.ViewModels;

static Task RedactionHidesSecrets()
{
    var redactor = new RedactionService();
    var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    var value = $"{home}/project api_key=abcdefghi123456789 bearer token_123456789012345 sk-abc123456789abcdef";
    var redacted = redactor.Redact(value);

    False(redacted.Contains(home, StringComparison.Ordinal), "home path should be redacted");
    False(redacted.Contains("abcdefghi123456789", StringComparison.Ordinal), "api key should be redacted");
    False(redacted.Contains("token_123456789012345", StringComparison.Ordinal), "bearer token should be redacted");
    False(redacted.Contains("sk-abc123456789abcdef", StringComparison.Ordinal), "openai key should be redacted");
    return Task.CompletedTask;
}

static async Task BenchmarkDbCreatesAndRecordsRuns()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var service = new BenchmarkService(settings);

    var run = await service.RecordRunAsync("suite-a", "baseline", 123.4, "tokens");
    True(!string.IsNullOrWhiteSpace(run.Id), "benchmark run should get an id");
    Equal("suite-a", run.Suite, "suite should persist");
    Equal(1, service.ListRuns().Count, "recorded run should be listed");
    True(File.Exists(Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "benchmarks.db")), "benchmark db should be created");
}

static Task BenchmarkScoringAndRanking()
{
    var service = new BenchmarkService(new FakeSettingsService());
    var scores = service.ScoreRuns([
        new BenchmarkRun { Suite = "suite", Name = "slow", Value = 10 },
        new BenchmarkRun { Suite = "suite", Name = "fast", Value = 20 },
        new BenchmarkRun { Suite = "suite", Name = "mid", Value = 15 }
    ]);

    Equal("fast", scores[0].Name, "highest scoring run should rank first");
    True(scores[0].Score >= scores[^1].Score, "scores should be sorted descending");
    return Task.CompletedTask;
}

static Task SystemInfoSafeFallback()
{
    var snapshot = new FakeSystemInfo().CaptureAsync().GetAwaiter().GetResult();
    Equal("test", snapshot.AppVersion, "snapshot should come from fake service");
    Equal(1, snapshot.Components.Count, "snapshot should include a component");
    return Task.CompletedTask;
}

static Task LocalAiAssetsDetectAndApplyPaths()
{
    using var temp = new TempDir();
    var root = temp.PathFor("assets");
    Directory.CreateDirectory(Path.Combine(root, "models"));
    Directory.CreateDirectory(Path.Combine(root, "tts"));
    Directory.CreateDirectory(Path.Combine(root, "voices"));
    File.WriteAllText(Path.Combine(root, "reranker.onnx"), "model");

    var settings = NewSettings(temp);
    settings.Settings.DataManagement.LocalAiAssetsRoot = root;
    LocalAiAssetLocator.ApplyDetected(settings.Settings, overwrite: true);

    Equal(Path.Combine(root, "models"), settings.Settings.Tts.ModelDirectory, "model directory should be applied");
    Equal(Path.Combine(root, "tts"), settings.Settings.Tts.PythonPath, "python path should be applied when detected");
    Equal(Path.Combine(root, "voices"), settings.Settings.Tts.VoiceDirectory, "voice directory should be applied");
    Equal(Path.Combine(root, "reranker.onnx"), settings.Settings.Rag.RerankerModelPath, "reranker path should be applied");
}

static async Task LocalAiSetupDetectsFolderLayout()
{
    using var temp = new TempDir();
    var root = temp.PathFor("AI");
    Directory.CreateDirectory(Path.Combine(root, "models"));
    Directory.CreateDirectory(Path.Combine(root, "voices"));
    Directory.CreateDirectory(Path.Combine(root, "xtts"));
    await File.WriteAllTextAsync(Path.Combine(root, "xtts", "run_xtts_api.py"), "print('xtts')");

    var settings = NewSettings(temp);
    var service = new LocalAiSetupService(new PythonHealthValidator());
    var report = await service.ScanAsync(settings.Settings with { DataManagement = { LocalAiAssetsRoot = root } });

    True(report.Items.Any(item => item.Status == LocalAiReadinessStatus.Ready), "scan should find ready items");
}

static async Task LocalAiSetupScriptHandlingIsApprovalGated()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var service = new LocalAiSetupService(new PythonHealthValidator());
    var plan = await service.BuildInstallPlanAsync(settings.Settings, temp.PathFor("AI"), allowOverwrite: false, progress: null);

    True(plan.Actions.Any(action => action.RequiresApproval), "install plan should contain gated actions");
}

static async Task LocalAiSetupCommandPreviewsStayShellFree()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    var service = new LocalAiSetupService(new PythonHealthValidator());
    var plan = await service.BuildInstallPlanAsync(settings.Settings, temp.PathFor("AI"), allowOverwrite: false, progress: null);

    False(plan.CommandPreview.Contains(';', StringComparison.Ordinal), "command previews should not synthesize shell separators");
}

static Task XttsApiTemplateHasRequiredEndpoints()
{
    var template = XttsProcessManager.BuildApiScriptTemplate();
    True(template.Contains("/tts", StringComparison.Ordinal), "template should expose tts endpoint");
    True(template.Contains("/health", StringComparison.Ordinal), "template should expose health endpoint");
    return Task.CompletedTask;
}

static async Task TrustScanInsideAiRootIsLowRisk()
{
    using var temp = new TempDir();
    var root = temp.PathFor("AI");
    var bin = Path.Combine(root, "runtimes", "llama-server");
    var model = Path.Combine(root, "Models", "tiny.gguf");
    Directory.CreateDirectory(Path.GetDirectoryName(bin)!);
    Directory.CreateDirectory(Path.GetDirectoryName(model)!);
    await File.WriteAllTextAsync(bin, "trusted");
    await File.WriteAllTextAsync(model, "model");

    var settings = new AppSettings { DataManagement = { LocalAiAssetsRoot = root } };
    settings.ManagedServers.Clear();
    settings.ManagedServers.Add(new ServerConfig { Name = "Chat", ExecutablePath = bin, ModelPath = model });

    var report = await new TrustService().ScanAsync(settings);
    var executable = report.Items.Single(i => i.Label == "Chat executable");
    Equal(TrustItemStatus.Ready, executable.Status, "inside executable should be ready");
    Equal(TrustRiskLevel.Low, executable.RiskLevel, "inside executable should be low risk");
    Equal(true, executable.IsInsideAiRoot, "inside executable should be scoped to AI root");
    Equal(ExpectedSha256("trusted"), executable.Sha256, "SHA256 should be stable");
}

static async Task TrustScanOutsideAiRootWarns()
{
    using var temp = new TempDir();
    var root = temp.PathFor("AI");
    var external = temp.PathFor("tools/llama-server");
    Directory.CreateDirectory(root);
    Directory.CreateDirectory(Path.GetDirectoryName(external)!);
    await File.WriteAllTextAsync(external, "external");

    var settings = new AppSettings { DataManagement = { LocalAiAssetsRoot = root } };
    settings.ManagedServers.Clear();
    settings.ManagedServers.Add(new ServerConfig { Name = "Chat", ExecutablePath = external, ModelPath = string.Empty });

    var report = await new TrustService().ScanAsync(settings);
    var executable = report.Items.Single(i => i.Label == "Chat executable");
    Equal(TrustItemStatus.Warning, executable.Status, "outside executable should warn");
    Equal(TrustRiskLevel.Medium, executable.RiskLevel, "outside executable should be medium risk");
    Equal(false, executable.IsInsideAiRoot, "outside executable should report outside AI root");
}

static async Task TrustScanReportsMissingExecutable()
{
    using var temp = new TempDir();
    var root = temp.PathFor("AI");
    Directory.CreateDirectory(root);

    var settings = new AppSettings { DataManagement = { LocalAiAssetsRoot = root } };
    settings.ManagedServers.Clear();
    settings.ManagedServers.Add(new ServerConfig { Name = "Chat", ExecutablePath = temp.PathFor("missing/llama-server"), ModelPath = string.Empty });

    var report = await new TrustService().ScanAsync(settings);
    var executable = report.Items.Single(i => i.Label == "Chat executable");
    Equal(TrustItemStatus.Missing, executable.Status, "missing executable should be reported");
    Equal(TrustRiskLevel.High, executable.RiskLevel, "missing executable should be high risk");
}

static async Task TrustScanUnsetAiRootIsNeutral()
{
    using var temp = new TempDir();
    var executable = temp.PathFor("llama-server");
    Directory.CreateDirectory(Path.GetDirectoryName(executable)!);
    await File.WriteAllTextAsync(executable, "tool");

    var settings = new AppSettings();
    settings.ManagedServers.Clear();
    settings.ManagedServers.Add(new ServerConfig { Name = "Chat", ExecutablePath = executable, ModelPath = string.Empty });

    var report = await new TrustService().ScanAsync(settings);
    var item = report.Items.Single(i => i.Label == "Chat executable");
    Equal(TrustItemStatus.Ready, item.Status, "unset AI root should not warn for existing files");
    Equal(null, item.IsInsideAiRoot, "unset AI root should have neutral scope");
}

static Task TrustScanDetectsNetworkExtraArgs()
{
    var warnings = new TrustService().AnalyzeServerExtraArgs(
        new ServerConfig { Name = "Chat", ExtraArgs = "--host 0.0.0.0 --alias local" },
        DateTime.UtcNow);

    Equal(1, warnings.Count, "network-facing host should produce one warning");
    Equal(TrustRiskLevel.High, warnings[0].RiskLevel, "network exposure should be high risk");
    False(warnings[0].Target.Contains(';', StringComparison.Ordinal), "trust warnings should not synthesize shell separators");
    return Task.CompletedTask;
}

static Task SourceStringsAvoidLongDashes()
{
    var root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
    var src = Path.Combine(root, "src");
    var explicitDocs = new[]
    {
        Path.Combine(root, "README.md"),
        Path.Combine(root, "CHANGELOG.md"),
        Path.Combine(root, "docs", "security-review.md")
    };
    var files = Directory.EnumerateFiles(src, "*.*", SearchOption.AllDirectories)
        .Where(path => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
            || path.EndsWith(".axaml", StringComparison.OrdinalIgnoreCase))
        .Concat(explicitDocs.Where(File.Exists));
    var offenders = files
        .SelectMany(path => File.ReadLines(path).Select((line, index) => (path, line, index)))
        .Where(item => item.line.Contains('\u2014') || item.line.Contains('\u2013'))
        .Select(item => $"{item.path}:{item.index + 1}")
        .ToList();
    Equal(0, offenders.Count, $"source should avoid em dash and en dash characters: {string.Join(", ", offenders)}");
    return Task.CompletedTask;
}

static string ExpectedSha256(string content) =>
    Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(content))).ToLowerInvariant();

static async Task SecretStoreFallbackWithoutPlaintext()
{
    using var temp = new TempDir();
    var previous = Environment.GetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN");
    Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", "1");
    try
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var store = new SecretStore(settings);
        var reference = await store.StoreAsync("openai-api-key", "sk-test-secret");
        Equal(true, store.IsReference(reference), "stored secret should return a reference");
        Equal("sk-test-secret", await store.ResolveAsync(reference), "secret reference should resolve");
        Equal("Local fallback file", await store.BackendLabelAsync(), "disabled keychain should use fallback label");

        var localVault = Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "secrets.local.json");
        True(File.Exists(localVault), "fallback vault should exist");
        var json = await File.ReadAllTextAsync(localVault);
        False(json.Contains("sk-test-secret", StringComparison.Ordinal), "fallback vault should not contain plaintext");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", previous);
    }
}

static async Task RuntimeProfileValidation()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    var service = new RuntimeProfileService(settings);
    var profile = new RuntimeProfile
    {
        Id = "runtime-1",
        Name = "  Custom Runtime  ",
        Kind = RuntimeKind.OpenAiCompatible,
        BaseUrl = "  https://example.test/v1/  ",
        ApiKey = " secret:runtime ",
        Enabled = true,
        LinkedServerId = " server-1 "
    };

    await service.SaveAsync(profile);
    var saved = settings.Settings.RuntimeProfiles.Single(p => p.Id == "runtime-1");
    Equal("Custom Runtime", saved.Name, "runtime profile name should be trimmed");
    Equal("https://example.test/v1", saved.BaseUrl, "runtime profile URL should be trimmed");
    Equal("secret:runtime", saved.ApiKey, "runtime profile API key should be trimmed");
    Equal("server-1", saved.LinkedServerId, "linked server id should be trimmed");

    var defaulted = RuntimeProfileService.NormalizeProfile(new RuntimeProfile
    {
        Id = string.Empty,
        Name = " ",
        Kind = RuntimeKind.LlamaCpp,
        BaseUrl = " "
    });
    True(Guid.TryParse(defaulted.Id, out _), "blank runtime id should be replaced");
    Equal("LlamaCpp", defaulted.Name, "blank runtime name should default to kind");
    Equal("http://127.0.0.1:8080", defaulted.BaseUrl, "blank runtime URL should default to loopback");

    var unsafeProfile = new RuntimeProfileViewModel(new RuntimeProfile { BaseUrl = "http://0.0.0.0:8080" });
    True(unsafeProfile.HasUnsafeHost, "runtime profile view model should flag 0.0.0.0");
}

static Task RuntimeProfilesAreDeduplicated()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.RuntimeProfiles =
    [
        new RuntimeProfile { Id = "llama-a", Name = "llama.cpp local", Kind = RuntimeKind.LlamaCpp, BaseUrl = "http://localhost:8080" },
        new RuntimeProfile { Id = "llama-b", Name = "llama.cpp local", Kind = RuntimeKind.LlamaCpp, BaseUrl = "http://localhost:8080/" },
        new RuntimeProfile { Id = "ollama", Name = "Ollama local", Kind = RuntimeKind.Ollama, BaseUrl = "http://127.0.0.1:11434" },
        new RuntimeProfile { Id = "ollama", Name = "Ollama local", Kind = RuntimeKind.Ollama, BaseUrl = "http://127.0.0.1:11434" }
    ];

    var service = new RuntimeProfileService(settings);
    Equal(2, service.Profiles.Count, "duplicate runtime defaults should be collapsed");
    Equal(2, settings.Settings.RuntimeProfiles.Count, "dedupe should update backing settings list");
    return Task.CompletedTask;
}

static async Task SettingsSaveMigratesOpenAiKey()
{
    using var temp = new TempDir();
    var previous = Environment.GetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN");
    Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", "1");
    try
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        settings.Settings.Llm.OpenAiApiKey = "sk-plain-key-123456";
        var secrets = new SecretStore(settings);
        var vm = NewSettingsViewModel(settings, secrets);

        Equal("sk-plain-key-123456", vm.OpenAiApiKey, "plaintext setting should load into editable field");
        await vm.SaveCommand.ExecuteAsync(null);

        True(secrets.IsReference(settings.Settings.Llm.OpenAiApiKey), "save should migrate plaintext key to a secret reference");
        Equal("sk-plain-key-123456", await secrets.ResolveAsync(settings.Settings.Llm.OpenAiApiKey), "migrated reference should resolve");
        var localVault = Path.Combine(settings.Settings.DataManagement.DataRootDirectory, "secrets.local.json");
        False((await File.ReadAllTextAsync(localVault)).Contains("sk-plain-key-123456", StringComparison.Ordinal),
            "migrated local vault should not contain plaintext");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", previous);
    }
}

static async Task SettingsSavePreservesExistingSecretReference()
{
    using var temp = new TempDir();
    var previous = Environment.GetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN");
    Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", "1");
    try
    {
        var settings = NewSettings(temp);
        settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
        var secrets = new SecretStore(settings);
        var reference = await secrets.StoreAsync("openai-api-key", "sk-existing-secret");
        settings.Settings.Llm.OpenAiApiKey = reference;

        var vm = NewSettingsViewModel(settings, secrets);
        Equal(string.Empty, vm.OpenAiApiKey, "existing secret reference should not be displayed");
        await vm.SaveCommand.ExecuteAsync(null);

        Equal(reference, settings.Settings.Llm.OpenAiApiKey, "blank API key field should preserve existing reference");
        Equal("sk-existing-secret", await secrets.ResolveAsync(reference), "preserved reference should still resolve");
    }
    finally
    {
        Environment.SetEnvironmentVariable("AETHER_DISABLE_OS_KEYCHAIN", previous);
    }
}

static async Task SettingsSavePersistsGlobalHotkeyPreference()
{
    using var temp = new TempDir();
    var settings = NewSettings(temp);
    settings.Settings.DataManagement.DataRootDirectory = temp.PathFor("data");
    var vm = NewSettingsViewModel(settings, new FakeSecretStore());

    vm.EnableGlobalHotkeys = true;
    await vm.SaveCommand.ExecuteAsync(null);
    True(settings.Settings.Ui.EnableGlobalHotkeys, "global hotkey setting should save when enabled");

    vm.EnableGlobalHotkeys = false;
    await vm.SaveCommand.ExecuteAsync(null);
    False(settings.Settings.Ui.EnableGlobalHotkeys, "global hotkey setting should save when disabled");
}

static Task ServerProcessArgumentsAreSafe()
{
    var args = ServerProcessManager.BuildLaunchArguments(new ServerConfig
    {
        ModelPath = "/models/local model.gguf",
        Port = 9090,
        ContextSize = 8192,
        Threads = 6,
        GpuLayers = 12,
        EmbeddingsMode = true,
        ExtraArgs = "--alias \"local model\" --host 0.0.0.0 --flag"
    }).ToList();

    Equal("-m", args[0], "model flag should be first");
    Equal("/models/local model.gguf", args[1], "model path with spaces should remain one argument");
    ContainsInOrder(args, "--host", "127.0.0.1", "managed host should be loopback by default");
    ContainsInOrder(args, "--alias", "local model", "quoted extra arg should remain one argument");
    ContainsInOrder(args, "--host", "0.0.0.0", "extra args should be preserved as data arguments");
    False(args.Any(a => a.Contains(';', StringComparison.Ordinal)), "argument builder should not synthesize shell separators");
    True(args.Contains("--embeddings"), "embeddings mode should add embeddings flag");
    return Task.CompletedTask;
}