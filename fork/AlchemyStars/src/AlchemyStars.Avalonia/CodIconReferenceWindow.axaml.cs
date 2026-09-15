using Avalonia.Controls;

namespace AlchemyStars.Avalonia;

/// <summary>
/// A small always-on-top window showing the wiki reference icon. It shares the
/// main view model so switching weapons in the database page updates this window
/// in place while it sits beside the CAST preview.
/// </summary>
public partial class CodIconReferenceWindow : Window
{
    public CodIconReferenceWindow() => InitializeComponent();

    public CodIconReferenceWindow(MainWindowViewModel model) : this() => DataContext = model;

    internal Image Preview => CompareImage;
    internal Slider ScaleSlider => CompareScale;
    internal CheckBox TopmostToggle => CompareTopmost;
}
