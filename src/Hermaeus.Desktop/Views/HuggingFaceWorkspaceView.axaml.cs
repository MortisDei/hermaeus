using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class HuggingFaceWorkspaceView : UserControl
{
    private ModelManagementViewModel? _viewModel;

    public HuggingFaceWorkspaceView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object? sender, EventArgs e)
    {
        if (_viewModel is not null)
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;

        _viewModel = DataContext as ModelManagementViewModel;
        if (_viewModel is not null)
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is not (nameof(ModelManagementViewModel.SelectedHfRepo)
            or nameof(ModelManagementViewModel.IsLoadingHfFiles)))
            return;

        Dispatcher.UIThread.Post(
            () =>
            {
                if (_viewModel?.SelectedHfRepo is not null)
                    SelectedHfRepoPanel.BringIntoView();
            },
            DispatcherPriority.Background);
    }

    private void OnHfSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || DataContext is not ModelManagementViewModel vm
            || !vm.SearchHuggingFaceCommand.CanExecute(null))
            return;

        vm.SearchHuggingFaceCommand.Execute(null);
        e.Handled = true;
    }
}
