namespace AlchemyStars.Avalonia;

public sealed class LocalizedOption(string label) : ObservableModel
{
    private string label = label;
    public string Label { get => label; set => SetProperty(ref label, value); }
    public override string ToString() => Label;
}

public sealed partial class MainWindowViewModel
{
    // Keep the option identities stable: replacing localized strings resets ComboBox selection.
    public IReadOnlyList<LocalizedOption> LayerTypeOptions { get; private set; } = [];
    public IReadOnlyList<LocalizedOption> PartTypeOptions { get; private set; } = [];
    public IReadOnlyList<LocalizedOption> WeaponFollowOptions { get; private set; } = [];
    public IReadOnlyList<LocalizedOption> DualModeOptions { get; private set; } = [];

    private void InitializeLocalizedOptions()
    {
        LayerTypeOptions = Text.LayerTypes.Select(label => new LocalizedOption(label)).ToArray();
        PartTypeOptions = Text.PartTypes.Select(label => new LocalizedOption(label)).ToArray();
        WeaponFollowOptions = Text.WeaponFollowModes.Select(label => new LocalizedOption(label)).ToArray();
        DualModeOptions = Text.DualModes.Select(label => new LocalizedOption(label)).ToArray();
    }

    private void RefreshLocalizedOptions()
    {
        Refresh(LayerTypeOptions, Text.LayerTypes);
        Refresh(PartTypeOptions, Text.PartTypes);
        Refresh(WeaponFollowOptions, Text.WeaponFollowModes);
        Refresh(DualModeOptions, Text.DualModes);
        static void Refresh(IReadOnlyList<LocalizedOption> options, string[] labels)
        {
            for (var index = 0; index < options.Count; index++) options[index].Label = labels[index];
        }
    }
}
