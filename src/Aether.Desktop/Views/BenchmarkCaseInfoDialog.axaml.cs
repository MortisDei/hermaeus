using Avalonia.Controls;
using Avalonia.Interactivity;
using Aether.Core.Models;

namespace Aether.Desktop.Views;

public partial class BenchmarkCaseInfoDialog : Window
{
    private BenchmarkCaseInfoViewModel? _dataContext;

    public BenchmarkCaseInfoDialog()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        _dataContext = DataContext as BenchmarkCaseInfoViewModel;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e)
    {
        Close();
    }
}

public sealed class BenchmarkCaseInfoViewModel
{
    private readonly BenchmarkResult _result;

    public string CaseName => _result.CaseName;
    public string Prompt => _result.Prompt;
    public string SystemPrompt => _result.SystemPrompt;
    public bool HasSystemPrompt => !string.IsNullOrWhiteSpace(_result.SystemPrompt);
    public bool HasExpectedKeywords => _result.ExpectedKeywords.Count > 0;
    public bool HasExpectedRegexes => _result.ExpectedRegexes.Count > 0;
    public bool ShouldRefuse => _result.ShouldRefuse;
    public bool IsOpenEnded => !HasExpectedKeywords && !HasExpectedRegexes && !ShouldRefuse;
    public List<string> ExpectedKeywords => _result.ExpectedKeywords;
    public List<string> ExpectedRegexes => _result.ExpectedRegexes;

    public BenchmarkCaseInfoViewModel(BenchmarkResult result)
    {
        _result = result;
    }
}
