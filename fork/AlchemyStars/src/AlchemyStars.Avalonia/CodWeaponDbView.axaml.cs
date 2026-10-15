using Avalonia;
using Avalonia.Automation;
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
    internal Button UpdateHeaderButton => CodUpdateHeaderButton;
    internal Border UpdateCard => CodUpdateCard;

    internal void VerifyHeaderLayout()
    {
        UpdateLayout();
        var header = CodDetailsHeaderGrid;
        var heading = BoundsInHeader(CodDetailsHeading);
        var title = BoundsInHeader(CodDetailsTitle);
        var commands = BoundsInHeader(CodDetailsHeaderCommands);
        var update = BoundsInHeader(CodUpdateHeaderButton);
        var copy = BoundsInHeader(CodCopyCodenameButton);
        Require(heading.Right <= commands.Left + 0.5 && title.Right <= commands.Left + 0.5,
            $"Title overlaps the header commands: heading={heading}, title={title}, commands={commands}.");
        Require(update.Right <= copy.Left + 0.5, $"Header commands overlap: update={update}, copy={copy}.");
        foreach (var (button, expectedName, glyph) in new[]
        {
            (CodUpdateHeaderButton, Model.Text.CodDbUpdateSection, "restore-layout"),
            (CodCopyCodenameButton, Model.Text.CodDbCopyCodename, "copy"),
        })
        {
            Require(Math.Abs(button.Bounds.Width - 44) < 0.5 && Math.Abs(button.Bounds.Height - 44) < 0.5,
                $"Header command must remain 44×44: {button.Name}, actual={button.Bounds.Size}.");
            Require(AutomationProperties.GetName(button) == expectedName && !string.IsNullOrWhiteSpace(ToolTip.GetTip(button)?.ToString()),
                $"Header command lost its localized name or tooltip: {button.Name}.");
            Require(button.Content is AppIcon icon && icon.Kind == glyph && icon.HasSymbol,
                $"Header command icon did not resolve: {button.Name}.");
        }

        Rect BoundsInHeader(Control control)
        {
            var origin = control.TranslatePoint(default, header)
                ?? throw new InvalidOperationException($"COD details header control is detached: {control.Name}.");
            var bounds = new Rect(origin, control.Bounds.Size);
            Require(control.IsEffectivelyVisible && bounds.Width > 0 && bounds.Height > 0
                && bounds.Left >= -0.5 && bounds.Top >= -0.5
                && bounds.Right <= header.Bounds.Width + 0.5 && bounds.Bottom <= header.Bounds.Height + 0.5,
                $"Header control is clipped or hidden: {control.Name}, bounds={bounds}, header={header.Bounds.Size}.");
            return bounds;
        }

        static void Require(bool condition, string detail)
        {
            if (!condition) throw new InvalidOperationException("COD details header: " + detail);
        }
    }

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
    private async void CheckUpdateClick(object? sender, RoutedEventArgs e) => await Model.CheckCodUpdateAsync();
    private async void UpdateDatabaseClick(object? sender, RoutedEventArgs e) => await Model.UpdateCodDatabaseAsync();

    /// <summary>
    /// The header shortcut reveals the update card and refreshes its status, so the
    /// feature is reachable without scrolling the whole inspector first.
    /// </summary>
    private async void UpdateHeaderClick(object? sender, RoutedEventArgs e)
    {
        CodUpdateCard.BringIntoView();
        await Model.CheckCodUpdateAsync();
        CodUpdateCard.BringIntoView();
    }
    private async void OpenWikiClick(object? sender, RoutedEventArgs e) => await Model.OpenCodSourceAsync(Model.CodWikiPageUrl);
    private async void OpenSourceClick(object? sender, RoutedEventArgs e) => await Model.OpenCodSourceAsync(Model.CodSourceUrl);

    private void CompareWindowClick(object? sender, RoutedEventArgs e)
    {
        var window = new CodIconReferenceWindow(Model);
        if (TopLevel.GetTopLevel(this) is Window owner) window.Show(owner);
        else window.Show();
    }
}
