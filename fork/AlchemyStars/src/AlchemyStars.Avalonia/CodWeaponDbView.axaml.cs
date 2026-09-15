using Avalonia.Controls;
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace AlchemyStars.Avalonia;

public partial class CodWeaponDbView : UserControl
{
    public CodWeaponDbView() => InitializeComponent();

    private MainWindowViewModel Model => (MainWindowViewModel)DataContext!;

    internal TextBox SearchBox => CodSearchBox;
    internal ComboBox GamePicker => CodGamePicker;
    internal ComboBox ClassPicker => CodClassPicker;
    internal ListBox ResultList => CodResultList;
    internal ListBox BlueprintList => CodBlueprintList;
    internal Image IconPreview => CodIconPreview;

    private void ClearFiltersClick(object? sender, RoutedEventArgs e)
    {
        Model.CodSearch = string.Empty;
        Model.CodPrefix = string.Empty;
        if (Model.CodGameOptions.Count > 0) Model.CodGameFilter = Model.CodGameOptions[0];
        if (Model.CodClassOptions.Count > 0) Model.CodClassFilter = Model.CodClassOptions[0];
    }

    private async void CopyCodenameClick(object? sender, RoutedEventArgs e)
    {
        if (Model.CodSelectedWeapon is not { HasCodename: true } weapon) return;
        if (TopLevel.GetTopLevel(this)?.Clipboard is not { } clipboard) return;
        await clipboard.SetTextAsync(weapon.Codename);
        Model.ReportCodenameCopied(weapon.Codename);
    }

    private async void FetchIconClick(object? sender, RoutedEventArgs e) => await Model.FetchCodIconAsync(false);
    private async void RefreshIconClick(object? sender, RoutedEventArgs e) => await Model.FetchCodIconAsync(true);
    private async void SaveIconClick(object? sender, RoutedEventArgs e) => await Model.SaveCodIconAsync();
    private async void ChooseDatasetClick(object? sender, RoutedEventArgs e) => await Model.ChooseCodDatasetAsync();
    private async void RestoreDatasetClick(object? sender, RoutedEventArgs e) => await Model.RestoreBuiltInCodDatasetAsync();
    private async void OpenWikiClick(object? sender, RoutedEventArgs e) => await Model.OpenCodSourceAsync(Model.CodWikiPageUrl);
    private async void OpenSourceClick(object? sender, RoutedEventArgs e) => await Model.OpenCodSourceAsync(Model.CodSourceUrl);

    private void CompareWindowClick(object? sender, RoutedEventArgs e)
    {
        var window = new CodIconReferenceWindow(Model);
        if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner);
        else window.Show();
    }
}
