using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AlchemyStars.Avalonia;

public partial class RavenfieldView : UserControl
{
    public RavenfieldView()
    {
        InitializeComponent();
        // Keep a usable image area on short windows; file actions remain scrollable.
        SizeChanged += (_, _) => this.FindControl<ScrollViewer>("RfOutputScroll")!.MaxHeight = Bounds.Height < 650 ? 140 : 230;
    }
    private MainWindowViewModel Model => (MainWindowViewModel)DataContext!;
    private async void BrowseClick(object? sender, RoutedEventArgs e) => await Model.ChooseRavenfieldAsync();
    private async void AdaptClick(object? sender, RoutedEventArgs e) => await Model.AdaptRavenfieldAsync();
    private async void BlendClick(object? sender, RoutedEventArgs e) => await Model.OpenRavenfieldAsync(false);
    private async void PreviewClick(object? sender, RoutedEventArgs e) => await Model.OpenRavenfieldAsync(true);
    private async void FbxClick(object? sender, RoutedEventArgs e) => await Model.OpenRavenfieldArtifactAsync("fbx");
    private async void ReportClick(object? sender, RoutedEventArgs e) => await Model.OpenRavenfieldArtifactAsync("report");
    private void AnimationsClick(object? sender, RoutedEventArgs e) => Model.SelectPage(WorkspacePage.Animations);
    private void PartsClick(object? sender, RoutedEventArgs e) => Model.SelectPage(WorkspacePage.ModelParts);
}
