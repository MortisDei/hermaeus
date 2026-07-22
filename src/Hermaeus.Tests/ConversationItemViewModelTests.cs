using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r18 01-finish-the-open-work.md 1.2: a rewrite of the partial change handlers dropped
/// <c>OnUpdatedAtChanged</c> entirely, so the conversation list row's "12m ago / Tue / 3 Jul"
/// label stopped updating on every save/rename/new message until the app restarted.
/// </summary>
public sealed class ConversationItemViewModelTests
{
    [Fact]
    public void UpdatedAt_change_raises_a_TimeDisplay_notification()
    {
        var item = new ConversationItemViewModel { Id = "x", Title = "t" };
        var raisedProperties = new List<string?>();
        item.PropertyChanged += (_, e) => raisedProperties.Add(e.PropertyName);

        item.UpdatedAt = DateTime.UtcNow;

        Assert.Contains(nameof(ConversationItemViewModel.TimeDisplay), raisedProperties);
    }

    [Fact]
    public void Editing_title_folder_or_tags_raises_MetadataChanged()
    {
        var item = new ConversationItemViewModel { Id = "x", Title = "t" };
        var changeCount = 0;
        item.MetadataChanged += _ => changeCount++;

        item.Title = "new title";
        item.Folder = "work";
        item.TagsText = "a, b";
        item.IsPinned = true;
        item.IsArchived = true;

        Assert.Equal(5, changeCount);
    }
}
