using Hermaeus.Core.Models;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ConfigurationIdentityFactoryTests
{
    [Fact]
    public void Placement_kind_and_schema_are_part_of_the_configuration_identity()
    {
        var cpu = ConfigurationIdentityFactory.Create(new ServerConfig
        {
            GpuPlacement = GpuPlacementIntent.Cpu()
        });
        var auto = ConfigurationIdentityFactory.Create(new ServerConfig
        {
            GpuPlacement = GpuPlacementIntent.Auto()
        });

        Assert.Equal("v1:cpu", cpu.GpuPlacement);
        Assert.Equal("v1:auto", auto.GpuPlacement);
        Assert.NotEqual(cpu.StableId, auto.StableId);
    }

    [Fact]
    public void Unknown_extra_arguments_make_identity_incomplete_without_retaining_the_raw_value()
    {
        var identity = ConfigurationIdentityFactory.Create(new ServerConfig
        {
            ExtraArgs = "--experimental-private-path /owner/private/model.gguf"
        });

        Assert.Equal(IdentityCompleteness.Incomplete, identity.Completeness);
        Assert.DoesNotContain(identity.ParsedExtraArguments.Values,
            value => value.Contains("private/model", StringComparison.Ordinal));
    }

    [Fact]
    public void Core_extra_aliases_do_not_create_a_second_identity_layer()
    {
        var withoutExtra = ConfigurationIdentityFactory.Create(new ServerConfig
        {
            GpuPlacement = GpuPlacementIntent.Exact(12)
        });
        var withAgreeingCoreExtra = ConfigurationIdentityFactory.Create(new ServerConfig
        {
            GpuPlacement = GpuPlacementIntent.Exact(12),
            ExtraArgs = "--n-gpu-layers 12 --ctx-size 4096"
        });

        Assert.Equal(withoutExtra.StableId, withAgreeingCoreExtra.StableId);
        Assert.Empty(withAgreeingCoreExtra.ParsedExtraArguments);
    }

    [Fact]
    public void Recognized_non_core_extras_are_in_the_identity_but_do_not_make_it_incomplete()
    {
        var identity = ConfigurationIdentityFactory.Create(new ServerConfig
        {
            ExtraArgs = "--alias local-model"
        });

        Assert.Equal(IdentityCompleteness.Complete, identity.Completeness);
        Assert.Single(identity.ParsedExtraArguments);
    }
}
