namespace AlchemyStars.Avalonia;

internal static class NativeTextResources
{
    internal static void Apply(UiText text)
    {
        if (global::Avalonia.Application.Current is not { } app) return;
        app.Resources["StringTextFlyoutCutText"] = text.EditCut;
        app.Resources["StringTextFlyoutCopyText"] = text.EditCopy;
        app.Resources["StringTextFlyoutPasteText"] = text.EditPaste;
    }
}

public sealed partial class UiText
{
    public string EditCut => L("剪切", "Cut");
    public string EditCopy => L("复制", "Copy");
    public string EditPaste => L("粘贴", "Paste");
}
