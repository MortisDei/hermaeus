using Aether.Core.Models;

namespace Aether.Core.Services;

public interface IModelProfileService
{
    IReadOnlyList<ModelProfile> Profiles { get; }
    ModelProfile GetOrCreate(string modelId, string backend = "");
    ModelProfile? Get(string modelId);
    void ApplyProfiles(IList<LlmModel> models);
    Task SaveAsync(ModelProfile profile, CancellationToken ct = default);
    Task ResetAsync(string modelId, CancellationToken ct = default);
}
