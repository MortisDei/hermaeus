using Avalonia.Controls;
using Avalonia;
using Avalonia.Input;
using Avalonia.Interactivity;
using Hermaeus.ViewModels;

namespace Hermaeus.Desktop.Views;

public partial class ChatGptPetOverlay : UserControl
{
    private IPointer? _dragPointer;
    private Point _dragStart;
    private double _positionStartX;
    private double _positionStartY;

    public ChatGptPetOverlay()
    {
        InitializeComponent();
        SizeChanged += OnSizeChanged;
    }

    private void OnSizeChanged(object? sender, SizeChangedEventArgs e) => UpdateViewport();

    private void OnPetPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(PetSprite).Properties.IsLeftButtonPressed
            || DataContext is not ChatGptPetViewModel pet)
            return;

        _dragPointer = e.Pointer;
        _dragStart = e.GetPosition(PetCanvas);
        _positionStartX = pet.PositionX;
        _positionStartY = pet.PositionY;
        PetSprite.SetDragging(true, movingRight: true);
        e.Pointer.Capture(PetSprite);
        e.Handled = true;
    }

    private void OnPetPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragPointer is null || !ReferenceEquals(e.Pointer, _dragPointer)
            || !e.GetCurrentPoint(PetCanvas).Properties.IsLeftButtonPressed
            || DataContext is not ChatGptPetViewModel pet)
            return;

        var current = e.GetPosition(PetCanvas);
        var deltaX = current.X - _dragStart.X;
        var deltaY = current.Y - _dragStart.Y;
        pet.SetPosition(_positionStartX + deltaX, _positionStartY + deltaY);
        if (Math.Abs(deltaX) > 1)
            PetSprite.SetDragging(true, deltaX > 0);
        e.Handled = true;
    }

    private void OnPetPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragPointer is null || !ReferenceEquals(e.Pointer, _dragPointer))
            return;
        EndDrag();
        e.Handled = true;
    }

    private void OnPetPointerCaptureLost(object? sender, PointerCaptureLostEventArgs e) => EndDrag();

    private void EndDrag()
    {
        _dragPointer?.Capture(null);
        _dragPointer = null;
        PetSprite.SetDragging(false, movingRight: true);
    }

    private void UpdateViewport()
    {
        if (DataContext is ChatGptPetViewModel pet)
            pet.UpdateViewport(Bounds.Width, Bounds.Height, PetSprite.Bounds.Width, PetSprite.Bounds.Height);
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        UpdateViewport();
    }
}
