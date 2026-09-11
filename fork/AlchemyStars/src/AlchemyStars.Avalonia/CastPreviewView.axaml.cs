using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace AlchemyStars.Avalonia;

public sealed partial class CastPreviewView : UserControl
{
    private Point? previous;
    public CastPreviewView()
    {
        InitializeComponent();
        LayoutUpdated += (_, _) => { if (IsEffectivelyVisible) Preview?.SetViewportSize(Viewport.Bounds.Width, Viewport.Bounds.Height); };
        FrameSlider.AddHandler(PointerPressedEvent, (_, _) => Preview?.Pause(), RoutingStrategies.Tunnel);
        FrameSlider.AddHandler(KeyDownEvent, (_, _) => Preview?.Pause(), RoutingStrategies.Tunnel);
    }
    private CastPreviewViewModel? Preview => DataContext as CastPreviewViewModel;
    internal void VerifyContextMenuLocalization()
    {
        var menu = FitButton.ContextMenu ?? throw new InvalidOperationException("The CAST framing menu is missing.");
        menu.Open(FitButton);
        var labels = menu.Items.OfType<MenuItem>().Select(item => item.Header?.ToString()).ToArray();
        menu.Close();
        if (Preview is null || !labels.SequenceEqual(new[] { Preview.Text.FitSubjectMenu, Preview.Text.FitAllGeometryMenu }))
            throw new InvalidOperationException("The CAST framing menu does not follow the active UI language.");
    }
    private void FitClick(object? sender, RoutedEventArgs e) => Preview?.Fit();
    private void FitAllClick(object? sender, RoutedEventArgs e) => Preview?.FitAll();
    private void FirstPersonClick(object? sender, RoutedEventArgs e) => Preview?.ToggleFirstPerson();
    private void ZoomInClick(object? sender, RoutedEventArgs e) => Preview?.Zoom(1);
    private void ZoomOutClick(object? sender, RoutedEventArgs e) => Preview?.Zoom(-1);
    private void PlayClick(object? sender, RoutedEventArgs e) => Preview?.TogglePlayback();
    private void PreviousClick(object? sender, RoutedEventArgs e) => Preview?.Step(-1);
    private void NextClick(object? sender, RoutedEventArgs e) => Preview?.Step(1);
    private void ViewportSizeChanged(object? sender, SizeChangedEventArgs e)
    {
        if (IsEffectivelyVisible) Preview?.SetViewportSize(e.NewSize.Width, e.NewSize.Height);
    }
    private void ViewportPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Viewport).Properties.IsLeftButtonPressed) return;
        Viewport.Focus(); previous = e.GetPosition(Viewport); e.Pointer.Capture(Viewport); e.Handled = true;
    }
    private void ViewportMoved(object? sender, PointerEventArgs e)
    {
        if (previous is not { } last || e.Pointer.Captured != Viewport) return;
        var current = e.GetPosition(Viewport); previous = current;
        Preview?.Orbit(current.X - last.X, current.Y - last.Y);
    }
    private void ViewportReleased(object? sender, PointerReleasedEventArgs e) { previous = null; e.Pointer.Capture(null); }
    private void ViewportWheel(object? sender, PointerWheelEventArgs e) { Preview?.Zoom(e.Delta.Y); e.Handled = true; }
    private void ViewportKeyDown(object? sender, KeyEventArgs e)
    {
        // Let modified global shortcuts bubble; Ctrl+1 must never toggle the camera.
        if (e.Handled || e.KeyModifiers != KeyModifiers.None && !(e.Key == Key.F && e.KeyModifiers == KeyModifiers.Shift)
            && !(e.Key == Key.OemPlus && e.KeyModifiers == KeyModifiers.Shift)) return;
        switch (e.Key)
        {
            case Key.Left: Preview?.Orbit(-10, 0); break;
            case Key.Right: Preview?.Orbit(10, 0); break;
            case Key.Up: Preview?.Orbit(0, -10); break;
            case Key.Down: Preview?.Orbit(0, 10); break;
            case Key.F: if (e.KeyModifiers.HasFlag(KeyModifiers.Shift)) Preview?.FitAll(); else Preview?.Fit(); break;
            case Key.D1: case Key.NumPad1: Preview?.ToggleFirstPerson(); break;
            case Key.Add: case Key.OemPlus: Preview?.Zoom(1); break;
            case Key.Subtract: case Key.OemMinus: Preview?.Zoom(-1); break;
            case Key.Space: Preview?.TogglePlayback(); break;
            case Key.OemOpenBrackets: Preview?.Step(-1); break;
            case Key.OemCloseBrackets: Preview?.Step(1); break;
            case Key.B: if (Preview is { } preview) preview.ShowBones = !preview.ShowBones; break;
            default: return;
        }
        e.Handled = true;
    }
}
