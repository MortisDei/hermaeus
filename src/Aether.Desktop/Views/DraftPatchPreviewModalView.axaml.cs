using Avalonia.Controls;
using Aether.ViewModels;

namespace Aether.Desktop.Views;

public partial class DraftPatchPreviewModalView : Window
{
    private DraftPatchDiffViewModel? _viewModel;

    public DraftPatchPreviewModalView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.DecisionCompleted -= OnDecisionCompleted;

        _viewModel = DataContext as DraftPatchDiffViewModel;
        if (_viewModel is not null)
            _viewModel.DecisionCompleted += OnDecisionCompleted;
    }

    private void OnDecisionCompleted(bool decision) => Close(decision);
}
