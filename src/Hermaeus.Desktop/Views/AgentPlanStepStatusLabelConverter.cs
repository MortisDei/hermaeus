using Avalonia.Data.Converters;
using Hermaeus.Agent.Models;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public sealed class AgentPlanStepStatusLabelConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        value is AgentPlanStepStatus status ? AgentPresentationText.PlanStatus(status) : value?.ToString() ?? string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture) =>
        throw new NotSupportedException();
}
