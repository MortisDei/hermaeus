namespace Hermaeus.ViewModels;

/// <summary>What the chat input box should do with a key press.</summary>
public enum ChatInputKeyAction
{
    /// <summary>Not ours; let the TextBox handle it.</summary>
    Pass,
    Send,
    Newline,
}

/// <summary>
/// The modifier keys this decision cares about. Declared here rather than
/// reusing Avalonia's KeyModifiers because Hermaeus.ViewModels must never
/// reference Avalonia; the view maps one to the other.
/// </summary>
[Flags]
public enum ChatInputModifiers
{
    None = 0,
    Control = 1,
    Shift = 2,
    Alt = 4,
    Meta = 8,
}

/// <summary>
/// r29 doc 01 1.4: which Enter combination sends and which inserts a newline.
///
/// The app used to own only the send half and delegate the newline half to
/// TextBox.AcceptsReturn, which meant that with Ui.CtrlEnterToSend false (the
/// default) AcceptsReturn was false and NO key combination could produce a
/// newline in the chat box. Both halves are decided here now, and the view
/// keeps AcceptsReturn true always.
/// </summary>
public static class ChatInputKeys
{
    /// <param name="isReturnKey">True for Enter/Return, false for every other key.</param>
    /// <param name="modifiers">Modifiers held with it.</param>
    /// <param name="ctrlEnterToSend">Ui.CtrlEnterToSend.</param>
    public static ChatInputKeyAction Resolve(bool isReturnKey, ChatInputModifiers modifiers, bool ctrlEnterToSend)
    {
        if (!isReturnKey)
            return ChatInputKeyAction.Pass;

        // Shift+Enter is a newline in both modes, which is what every chat
        // client does and what users reach for without being told.
        if (modifiers == ChatInputModifiers.Shift)
            return ChatInputKeyAction.Newline;

        var send = ctrlEnterToSend ? ChatInputModifiers.Control : ChatInputModifiers.None;
        var newline = ctrlEnterToSend ? ChatInputModifiers.None : ChatInputModifiers.Control;

        if (modifiers == send)
            return ChatInputKeyAction.Send;
        if (modifiers == newline)
            return ChatInputKeyAction.Newline;

        return ChatInputKeyAction.Pass;
    }
}
