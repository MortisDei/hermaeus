using System;
using System.Globalization;
using System.IO;
using Avalonia.Data.Converters;
using Avalonia.Media.Imaging;

namespace Hermaeus.Desktop.Views;

/// <summary>r19 5.3: decodes a chat image attachment's <c>data:&lt;mediaType&gt;;base64,...</c>
/// string into a Bitmap for the context-attachments thumbnail chip. Malformed input renders no
/// image rather than throwing, since this only ever backs a best-effort preview.</summary>
public sealed class DataUriToBitmapConverter : IValueConverter
{
    public static readonly DataUriToBitmapConverter Instance = new();

    public object? Convert(object? v, Type t, object? parameter, CultureInfo c)
    {
        if (v is not string dataUri || string.IsNullOrEmpty(dataUri)) return null;
        var comma = dataUri.IndexOf(',');
        if (comma < 0) return null;
        try
        {
            var bytes = System.Convert.FromBase64String(dataUri[(comma + 1)..]);
            using var stream = new MemoryStream(bytes);
            return new Bitmap(stream);
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? v, Type t, object? p, CultureInfo c) => Avalonia.AvaloniaProperty.UnsetValue;
}
