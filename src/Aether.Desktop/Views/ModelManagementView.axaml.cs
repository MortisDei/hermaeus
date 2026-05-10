using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Media;
using System.Globalization;

namespace Aether.Desktop.Views;

public partial class ModelManagementView : UserControl
{
    public ModelManagementView() => InitializeComponent();
}

public class NotEmptyConverter : IValueConverter
{
    public static readonly NotEmptyConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is string s && !string.IsNullOrEmpty(s);
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotImplementedException();
}

public class ErrorColorConverter : IValueConverter
{
    public static readonly ErrorColorConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is true
            ? (IBrush)new SolidColorBrush(Color.Parse("#EF5350"))
            : new SolidColorBrush(Color.Parse("#66BB6A"));
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotImplementedException();
}

public class PullingTextConverter : IValueConverter
{
    public static readonly PullingTextConverter Instance = new();
    public object Convert(object? v, Type t, object? p, CultureInfo c)
        => v is true ? "Pulling…" : "Pull";
    public object ConvertBack(object? v, Type t, object? p, CultureInfo c)
        => throw new NotImplementedException();
}
