namespace AlchemyStars.Avalonia;

public sealed partial class MainWindowViewModel
{
    private int themeStyleIndex;
    private int themeModeIndex;

    public int ThemeStyleIndex
    {
        get => themeStyleIndex;
        set
        {
            if (value is < 0 or > 2 || value == themeStyleIndex) return;
            themeStyleIndex = value;
            OnPropertyChanged();
            ApplyAppearance(true);
        }
    }

    public int ThemeModeIndex
    {
        get => themeModeIndex;
        set
        {
            if (value is < 0 or > 2 || value == themeModeIndex) return;
            themeModeIndex = value;
            OnPropertyChanged();
            ApplyAppearance(true);
        }
    }

    private void ApplyAppearance(bool save)
    {
        var style = themeStyleIndex switch
        {
            1 => "classic-apple",
            2 => "neumorphic",
            _ => "apple",
        };
        var mode = themeModeIndex switch { 1 => "dark", 2 => "system", _ => "light" };
        if (save) preferences.SaveAppearance(style, mode);
        AppearanceTheme.Apply(style, mode);
    }
}

public sealed partial class UiText
{
    public string Appearance => L("外观", "Appearance");
    public string AppearanceHelp => L("切换立即生效，并自动保存。", "Changes apply immediately and are saved automatically.");
    public string ThemeStyle => L("界面风格", "Interface style");
    public string ThemeMode => L("明暗模式", "Color mode");
    public string[] ThemeStyles =>
    [
        L("原版 · 简洁", "Original · Minimal"),
        L("经典 Apple", "Classic Apple"),
        L("拟物化 · 柔和浮雕", "Neumorphic · Soft relief"),
    ];
    public string[] ThemeModes => [L("浅色", "Light"), L("深色", "Dark"), L("跟随系统", "Use system setting")];
}
