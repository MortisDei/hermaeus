using Hermaeus.Core.Services;
using Hermaeus.Services;
using Xunit;

namespace Hermaeus.Tests;

public sealed class ToastDetailsTests
{
    [Fact]
    public void Detail_notifications_are_marked_copyable_without_changing_ordinary_toasts()
    {
        var service = new ToastService();
        var raised = new List<ToastMessage>();
        service.ToastRaised += raised.Add;

        service.Show("Ordinary", "Short status");
        service.ShowDetails("Moss: learned", "Diagnostic detail");

        Assert.False(raised[0].CanCopyDetails);
        Assert.True(raised[1].CanCopyDetails);
    }
}
