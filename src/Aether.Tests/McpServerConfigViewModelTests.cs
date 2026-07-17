using Aether.Core.Models;
using Aether.ViewModels;
using Xunit;

namespace Aether.Tests;

/// <summary>r12 03-runtime-vm-correctness.md 3.9: a naive space split made a quoted argument with spaces impossible.</summary>
public sealed class McpServerConfigViewModelTests
{
    [Fact]
    public void ToConfig_honors_double_quoted_arguments_containing_spaces()
    {
        var vm = new McpServerConfigViewModel(new McpServerConfig
        {
            Name = "test",
            Command = "node",
            Arguments = ["server.js"]
        })
        {
            ArgumentsText = """--root "C:\Program Files\data" --verbose"""
        };

        var config = vm.ToConfig();

        Assert.Equal(["--root", @"C:\Program Files\data", "--verbose"], config.Arguments);
    }
}
