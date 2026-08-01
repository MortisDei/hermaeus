using Hermaeus.ViewModels;
using Xunit;

namespace Hermaeus.Tests;

/// <summary>
/// r29 doc 01 1.4: the chat box used to own only the send half of
/// Ui.CtrlEnterToSend and delegate the newline half to TextBox.AcceptsReturn.
/// With the setting false (the default) AcceptsReturn was false, so no key
/// combination produced a newline in the app's primary input.
/// </summary>
public sealed class ChatInputKeyTests
{
    [Fact]
    public void With_enter_sending_ctrl_enter_and_shift_enter_insert_a_newline()
    {
        Assert.Equal(ChatInputKeyAction.Send,
            ChatInputKeys.Resolve(true, ChatInputModifiers.None, ctrlEnterToSend: false));
        Assert.Equal(ChatInputKeyAction.Newline,
            ChatInputKeys.Resolve(true, ChatInputModifiers.Control, ctrlEnterToSend: false));
        Assert.Equal(ChatInputKeyAction.Newline,
            ChatInputKeys.Resolve(true, ChatInputModifiers.Shift, ctrlEnterToSend: false));
    }

    [Fact]
    public void With_ctrl_enter_sending_enter_and_shift_enter_insert_a_newline()
    {
        Assert.Equal(ChatInputKeyAction.Send,
            ChatInputKeys.Resolve(true, ChatInputModifiers.Control, ctrlEnterToSend: true));
        Assert.Equal(ChatInputKeyAction.Newline,
            ChatInputKeys.Resolve(true, ChatInputModifiers.None, ctrlEnterToSend: true));
        Assert.Equal(ChatInputKeyAction.Newline,
            ChatInputKeys.Resolve(true, ChatInputModifiers.Shift, ctrlEnterToSend: true));
    }

    [Fact]
    public void A_key_that_is_not_enter_is_never_handled()
    {
        Assert.Equal(ChatInputKeyAction.Pass,
            ChatInputKeys.Resolve(false, ChatInputModifiers.None, ctrlEnterToSend: false));
        Assert.Equal(ChatInputKeyAction.Pass,
            ChatInputKeys.Resolve(false, ChatInputModifiers.Control, ctrlEnterToSend: true));
    }

    [Fact]
    public void Enter_with_an_unrelated_modifier_is_left_to_the_text_box()
    {
        Assert.Equal(ChatInputKeyAction.Pass,
            ChatInputKeys.Resolve(true, ChatInputModifiers.Alt, ctrlEnterToSend: false));
        Assert.Equal(ChatInputKeyAction.Pass,
            ChatInputKeys.Resolve(true, ChatInputModifiers.Meta, ctrlEnterToSend: true));
    }

    /// <summary>The invariant that was broken: whatever the setting, some key
    /// combination must produce a newline.</summary>
    [Fact]
    public void Every_setting_leaves_at_least_one_combination_that_inserts_a_newline()
    {
        foreach (var ctrlEnterToSend in new[] { false, true })
        {
            var combinations = new[] { ChatInputModifiers.None, ChatInputModifiers.Control, ChatInputModifiers.Shift };
            Assert.Contains(combinations,
                m => ChatInputKeys.Resolve(true, m, ctrlEnterToSend) == ChatInputKeyAction.Newline);
            Assert.Contains(combinations,
                m => ChatInputKeys.Resolve(true, m, ctrlEnterToSend) == ChatInputKeyAction.Send);
        }
    }
}
