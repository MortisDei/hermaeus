using Aether.Core.Models;

    await vm.AddContextFilesAsync([file]);

    Equal("Estimated", vm.ContextUsageKind, "pending context should be locally estimated");
    True(vm.IsContextUsageWarning, "80 percent estimate should warn");
    False(vm.IsContextUsageCritical, "80 percent estimate should not be critical");

        ActiveStep = "Inspect",
        Constraints = ["local-first"],
        CompletedSteps = ["created"],
        PendingSteps = ["inspect"],
        Summary = "Ready"
    };

    await store.SaveAsync(state);
    var json = await File.ReadAllTextAsync(Path.Combine(store.GetTaskDirectory("task-1"), "task_state.json"));
    True(json.Contains("\"task_id\"", StringComparison.Ordinal), "task state should use schema task_id field");
    True(json.Contains("\"status\": \"running\"", StringComparison.Ordinal), "task state should serialize schema enum values");
    True(json.Contains("\"completed_steps\"", StringComparison.Ordinal), "task state should use schema completed_steps field");
    True(json.Contains("\"approval_history\"", StringComparison.Ordinal), "task state should include approval history");
    var loaded = await store.LoadAsync("task-1");
    Equal("Check project", loaded?.Goal, "stored task state should reload");
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
