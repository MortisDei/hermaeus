using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.VisualTree;
using Hermaeus.Core.Models;
using Avalonia.Threading;

namespace Hermaeus.Desktop.Controls;

/// <summary>
/// Renders the fixed ChatGPT Pet v2 atlas without turning a package into a
/// second UI or script host. The standard atlas is eight columns by eleven
/// rows, with the first three rows used for idle and horizontal walking.
/// </summary>
public sealed class PetSprite : Control
{
    public static readonly StyledProperty<PetPackage?> PackageProperty =
        AvaloniaProperty.Register<PetSprite, PetPackage?>(nameof(Package));

    private const int AtlasColumns = 8;
    private const int AtlasRows = 11;
    private const int AtlasWidth = 1536;
    private const int AtlasHeight = 2288;
    private const int AnimationFrameMilliseconds = 220;
    private const int BlinkFrameIndex = 2;
    private const int BlinkPeriodMilliseconds = 4000;
    private const int BlinkDurationMilliseconds = 180;
    // The blink atlas frame is used only as an eye-band overlay. Drawing this
    // small rectangle keeps the body and its current animation frame intact.
    private static readonly Rect BlinkEyeBand = new(43, 83, 106, 31);
    private static readonly int[] IdleFrameIndices = [0, 1, 3, 4, 5, 6, 7];

    private readonly DispatcherTimer _animationTimer;
    private Bitmap? _spritesheet;
    private int _frame;
    private int _row;
    private bool _moving;
    private bool _movingRight = true;
    private bool _blinkActive;
    private DateTime _nextBlinkAtUtc = DateTime.UtcNow.AddMilliseconds(BlinkPeriodMilliseconds);
    private DateTime _blinkUntilUtc;

    public PetSprite()
    {
        Focusable = false;
        IsTabStop = false;
        _animationTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(AnimationFrameMilliseconds) };
        _animationTimer.Tick += OnAnimationTick;
        AttachedToVisualTree += OnAttachedToVisualTree;
        DetachedFromVisualTree += OnDetachedFromVisualTree;
    }

    public PetPackage? Package
    {
        get => GetValue(PackageProperty);
        set => SetValue(PackageProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == PackageProperty)
            LoadPackage(change.GetNewValue<PetPackage?>());
        else if (change.Property == IsVisibleProperty)
            UpdateTimerState();
    }

    public void SetDragging(bool moving, bool movingRight)
    {
        if (moving != _moving)
            ResetBlinkSchedule();
        if (moving != _moving || (moving && movingRight != _movingRight))
            _frame = 0;
        _moving = moving;
        _movingRight = movingRight;
        _row = moving ? (movingRight ? 1 : 2) : 0;
        if (!moving)
            _frame = 0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        if (_spritesheet is null || Bounds.Width <= 0 || Bounds.Height <= 0)
            return;

        var source = new Rect(
            (_moving ? _frame : IdleFrameIndices[_frame % IdleFrameIndices.Length]) * AtlasWidth / AtlasColumns,
            _row * AtlasHeight / AtlasRows,
            AtlasWidth / AtlasColumns,
            AtlasHeight / AtlasRows);
        var destination = new Rect(0, 0, Bounds.Width, Bounds.Height);
        context.DrawImage(_spritesheet, source, destination);

        if (_blinkActive && !_moving)
        {
            var cellWidth = AtlasWidth / AtlasColumns;
            var cellHeight = AtlasHeight / AtlasRows;
            var blinkSource = new Rect(
                BlinkFrameIndex * cellWidth + BlinkEyeBand.X,
                BlinkEyeBand.Y,
                BlinkEyeBand.Width,
                BlinkEyeBand.Height);
            var scaleX = Bounds.Width / cellWidth;
            var scaleY = Bounds.Height / cellHeight;
            var blinkDestination = new Rect(
                BlinkEyeBand.X * scaleX,
                BlinkEyeBand.Y * scaleY,
                BlinkEyeBand.Width * scaleX,
                BlinkEyeBand.Height * scaleY);
            context.DrawImage(_spritesheet, blinkSource, blinkDestination);
        }
    }

    private void LoadPackage(PetPackage? package)
    {
        _animationTimer.Stop();
        _spritesheet?.Dispose();
        _spritesheet = null;
        _frame = 0;
        _row = 0;
        ResetBlinkSchedule();

        if (package is null || !File.Exists(package.SpritesheetPath))
        {
            InvalidateVisual();
            return;
        }

        try
        {
            var bitmap = new Bitmap(package.SpritesheetPath);
            if (bitmap.PixelSize.Width != AtlasWidth || bitmap.PixelSize.Height != AtlasHeight)
            {
                bitmap.Dispose();
                InvalidateVisual();
                return;
            }

            _spritesheet = bitmap;
            UpdateTimerState();
            InvalidateVisual();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NotSupportedException)
        {
            InvalidateVisual();
        }
    }

    private void OnAnimationTick(object? sender, EventArgs e)
    {
        if (_spritesheet is null || !IsVisible)
            return;
        UpdateBlinkState(DateTime.UtcNow);
        _frame = _moving
            ? (_frame + 1) % AtlasColumns
            : (_frame + 1) % IdleFrameIndices.Length;
        InvalidateVisual();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => UpdateTimerState();

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e) => _animationTimer.Stop();

    private void UpdateTimerState()
    {
        if (_spritesheet is not null && IsVisible && VisualRoot is not null)
            _animationTimer.Start();
        else
            _animationTimer.Stop();
    }

    private void UpdateBlinkState(DateTime nowUtc)
    {
        if (_moving)
        {
            _blinkActive = false;
            return;
        }

        if (_blinkActive)
        {
            if (nowUtc >= _blinkUntilUtc)
            {
                _blinkActive = false;
                _nextBlinkAtUtc = nowUtc.AddMilliseconds(BlinkPeriodMilliseconds);
            }
            return;
        }

        if (nowUtc >= _nextBlinkAtUtc)
        {
            _blinkActive = true;
            _blinkUntilUtc = nowUtc.AddMilliseconds(BlinkDurationMilliseconds);
        }
    }

    private void ResetBlinkSchedule()
    {
        _blinkActive = false;
        _nextBlinkAtUtc = DateTime.UtcNow.AddMilliseconds(BlinkPeriodMilliseconds);
        _blinkUntilUtc = default;
    }
}
