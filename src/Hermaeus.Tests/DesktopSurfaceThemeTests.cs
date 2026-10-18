using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Hermaeus.Desktop.Views;
using Xunit;

namespace Hermaeus.Tests;

[Collection(AvaloniaTestHost.CollectionName)]
public sealed class DesktopSurfaceThemeTests(AvaloniaTestHost avalonia)
{
    [Theory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public Task Palette_setup_and_doctor_surfaces_are_opaque_in_both_themes(string theme) => avalonia.RunAsync(() =>
    {
        var window = new MainWindow
        {
            RequestedThemeVariant = theme == "Light" ? ThemeVariant.Light : ThemeVariant.Dark,
            Opacity = 0,
            ShowActivated = false,
            ShowInTaskbar = false
        };
        try
        {
            window.Show();
            var input = window.FindControl<TextBox>("PaletteTextBox")!;
            var palette = input.GetLogicalAncestors().OfType<Border>().First();
            AssertOpaque(palette.Background);

            var setup = new SetupWizardView();
            window.Content = setup;
            AssertOpaque(Assert.IsType<Border>(setup.Content).Background);

            var doctor = new DoctorView();
            window.Content = doctor;
            foreach (var title in new[]
            {
                "Installing Reranker", "Installing Kokoro (native)",
                "Updating llama.cpp", "Installing Embedding Model"
            })
            {
                var label = Assert.Single(doctor.GetLogicalDescendants().OfType<TextBlock>(),
                    text => text.Text == title);
                AssertOpaque(label.GetLogicalAncestors().OfType<Border>().First().Background);
            }
        }
        finally { window.Close(); }
        return Task.CompletedTask;
    });

    private static void AssertOpaque(IBrush? brush)
    {
        var solid = Assert.IsAssignableFrom<ISolidColorBrush>(brush);
        Assert.Equal(byte.MaxValue, solid.Color.A);
        Assert.Equal(1, solid.Opacity);
    }
}
