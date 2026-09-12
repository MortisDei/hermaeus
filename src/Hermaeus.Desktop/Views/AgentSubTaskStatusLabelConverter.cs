using Avalonia.Data.Converters;
using Hermaeus.Agent.Models;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public sealed class AgentSubTaskStatusLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is AgentSubTaskStatus status ? AgentPresentationText.SubTaskStatus(status) : value?.ToString() ?? string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
