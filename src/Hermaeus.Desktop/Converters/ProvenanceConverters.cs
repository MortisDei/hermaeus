using Avalonia.Data.Converters;
using Hermaeus.Core.Models;

namespace Hermaeus.Desktop.Views;

/// <summary>
/// r25 doc 02: the context receipt shows pills for every
/// <see cref="ProvenanceKind"/>, but "Open in Memories" navigates to the
/// Memories panel prefilled with the item's title
/// (<c>ChatViewModel.RequestNavigateToMemory</c>), which is only meaningful
/// for a real memory. A Recall hit or a knowledge excerpt has no memory to
/// open, so the action is hidden rather than offered and left to fail.
/// </summary>
public static class ProvenanceConverters
{
    public static readonly IValueConverter IsMemory =
        new FuncValueConverter<ProvenanceKind, bool>(kind => kind == ProvenanceKind.Memory);
}
