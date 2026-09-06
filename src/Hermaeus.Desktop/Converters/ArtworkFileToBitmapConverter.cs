using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;
using Hermaeus.Services;

namespace Hermaeus.Desktop.Views;

/// <summary>
/// Loads only artwork that the services layer has already bounded and preflighted.
/// The post-construction dimension check is a second guard at the Avalonia boundary.
/// </summary>
public sealed class ArtworkFileToBitmapConverter : IValueConverter
{
    public static readonly ArtworkFileToBitmapConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            return null;
        Bitmap? bitmap = null;
        try
        {
            bitmap = new Bitmap(path);
            var pixels = checked((long)bitmap.PixelSize.Width * bitmap.PixelSize.Height);
            var bytes = checked(pixels * 4);
            if (bitmap.PixelSize.Width <= 0 || bitmap.PixelSize.Height <= 0
                || bitmap.PixelSize.Width > HuggingFaceArtworkService.MaxDimension
                || bitmap.PixelSize.Height > HuggingFaceArtworkService.MaxDimension
                || pixels > HuggingFaceArtworkService.MaxDecodedPixels
                || bytes > HuggingFaceArtworkService.MaxDecodedBytes)
            {
                bitmap.Dispose();
                return null;
            }
            return bitmap;
        }
        catch
        {
            bitmap?.Dispose();
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Avalonia.AvaloniaProperty.UnsetValue;
}
