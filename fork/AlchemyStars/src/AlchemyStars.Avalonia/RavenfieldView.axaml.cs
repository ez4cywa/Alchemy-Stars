using Avalonia.Controls;
using Avalonia.Interactivity;

namespace AlchemyStars.Avalonia;

public partial class RavenfieldView : UserControl
{
    public RavenfieldView() => InitializeComponent();
    private MainWindowViewModel Model => (MainWindowViewModel)DataContext!;
    private async void BrowseClick(object? sender, RoutedEventArgs e) => await Model.ChooseRavenfieldAsync();
    private async void AdaptClick(object? sender, RoutedEventArgs e) => await Model.AdaptRavenfieldAsync();
    private async void BlendClick(object? sender, RoutedEventArgs e) => await Model.OpenRavenfieldAsync(false);
    private async void PreviewClick(object? sender, RoutedEventArgs e) => await Model.OpenRavenfieldAsync(true);
}
