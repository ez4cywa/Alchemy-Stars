using System.ComponentModel;

namespace AlchemyStars.Avalonia;

public sealed class AppearanceOption(string label) : INotifyPropertyChanged
{
    public string Label { get; private set; } = label;
    public event PropertyChangedEventHandler? PropertyChanged;
    internal void UpdateLabel(string value)
    {
        if (Label == value) return;
        Label = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Label)));
    }
}

public sealed partial class MainWindowViewModel
{
    private int themeStyleIndex;
    private int themeModeIndex;
    private AppearanceOption[]? themeStyles;

    public int ThemeStyleIndex
    {
        get => themeStyleIndex;
        set
        {
            if (value is < 0 or > 3 || value == themeStyleIndex) return;
            if (value == 3 && CustomAppearance.CurrentTheme is null) return;
            themeStyleIndex = value;
            OnPropertyChanged();
            ApplyAppearance(true);
            RefreshAppearanceLabel();
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
            RefreshAppearanceLabel();
        }
    }

    private bool IsDarkAppearance => global::Avalonia.Application.Current is { } app
        ? app.ActualThemeVariant == global::Avalonia.Styling.ThemeVariant.Dark
        : themeModeIndex == 1;

    public string ToggleAppearanceLabel => IsDarkAppearance
        ? (IsChinese ? "切换为浅色" : "Switch to light")
        : (IsChinese ? "切换为深色" : "Switch to dark");

    public void ToggleAppearance() => ThemeModeIndex = IsDarkAppearance ? 0 : 1;

    internal void RefreshAppearanceLabel()
    {
        OnPropertyChanged(nameof(ToggleAppearanceLabel));
        var labels = BuildThemeStyles();
        if (themeStyles is null || themeStyles.Length != labels.Length)
        {
            var previous = themeStyles ?? [];
            themeStyles = labels.Select((label, index) => index < previous.Length
                ? previous[index] : new AppearanceOption(label)).ToArray();
            for (var index = 0; index < labels.Length; index++)
                themeStyles[index].UpdateLabel(labels[index]);
            OnPropertyChanged(nameof(ThemeStyles));
        }
        else
            for (var index = 0; index < labels.Length; index++)
                themeStyles[index].UpdateLabel(labels[index]);
        OnPropertyChanged(nameof(CustomAppearanceStatus));
        OnPropertyChanged(nameof(HasCustomTheme));
        OnPropertyChanged(nameof(HasCustomIcons));
        OnPropertyChanged(nameof(CanResetCustomTheme));
        OnPropertyChanged(nameof(CanResetCustomIcons));
    }

    public bool HasCustomTheme => CustomAppearance.CurrentTheme is not null;
    public bool HasCustomIcons => CustomAppearance.IconCount > 0;
    public bool CanResetCustomTheme => HasCustomTheme || CustomAppearance.HasSavedTheme;
    public bool CanResetCustomIcons => HasCustomIcons || CustomAppearance.HasSavedIcons;
    public AppearanceOption[] ThemeStyles => themeStyles ??= BuildThemeStyles().Select(label => new AppearanceOption(label)).ToArray();
    private string[] BuildThemeStyles() => HasCustomTheme
        ? [.. Text.ThemeStyles, IsChinese ? $"自定义 · {CustomAppearance.CurrentTheme!.Name}" : $"Custom · {CustomAppearance.CurrentTheme!.Name}"]
        : Text.ThemeStyles;
    public string CustomAppearanceStatus => CustomAppearance.LoadError is { } error
        ? (IsChinese ? "自定义外观加载失败，已回退：" : "Custom appearance could not load; using defaults: ") + error
        : IsChinese
            ? $"自定义主题：{CustomAppearance.CurrentTheme?.Name ?? "未导入"} · 自定义图标：{CustomAppearance.IconCount}/34"
            : $"Custom theme: {CustomAppearance.CurrentTheme?.Name ?? "not imported"} · Custom icons: {CustomAppearance.IconCount}/34";

    internal void ImportAppearance(string path, bool icons)
    {
        if (icons) CustomAppearance.ImportIcons(path);
        else
        {
            CustomAppearance.ImportTheme(path);
            RefreshAppearanceLabel(); // Populate the picker before selecting its new item.
            themeStyleIndex = 3;
            OnPropertyChanged(nameof(ThemeStyleIndex));
            ApplyAppearance(true);
        }
        RefreshAppearanceLabel();
    }

    internal void ResetCustomAppearance(bool icons)
    {
        if (icons) CustomAppearance.ClearIcons();
        else
        {
            CustomAppearance.ClearTheme();
            if (themeStyleIndex == 3) ThemeStyleIndex = 0;
        }
        RefreshAppearanceLabel();
    }

    internal void ReportAppearanceError(Exception error) => ShowDialog(
        IsChinese ? "外观导入失败" : "Appearance import failed",
        (IsChinese ? "原有外观未被替换。\n" : "The existing appearance was not replaced.\n") + error.Message, true);

    private void ApplyAppearance(bool save)
    {
        var style = themeStyleIndex switch
        {
            1 => "classic-apple",
            2 => "windows-xp",
            3 => "custom",
            _ => "apple",
        };
        var mode = themeModeIndex switch { 1 => "dark", 2 => "system", _ => "light" };
        if (save) preferences.SaveAppearance(style, mode);
        AppearanceTheme.Apply(style, mode);
    }
}

public sealed partial class UiText
{
    public string DesktopWorkspace => L("工作区", "Workspace");
    public string DesktopPreferences => L("偏好设置", "Preferences");
    public string DesktopLanguage => L("语言", "Language");
    public string WindowClose => L("关闭窗口", "Close window");
    public string WindowMinimize => L("最小化窗口", "Minimize window");
    public string WindowZoom => L("最大化或还原窗口", "Maximize or restore window");
    public string AnimationBlend => L("动画混合", "Animation blend");
    public string Appearance => L("外观", "Appearance");
    public string AppearanceHelp => L("切换立即生效，并自动保存。", "Changes apply immediately and are saved automatically.");
    public string ImportTheme => L("导入主题 JSON", "Import theme JSON");
    public string ImportIcons => L("导入图标 ZIP", "Import icon ZIP");
    public string ResetTheme => L("移除自定义主题", "Remove custom theme");
    public string ResetIcons => L("恢复内置图标", "Restore built-in icons");
    public string CustomAppearanceHelp => L("主题包含浅色与深色配置；图标包使用功能名命名的 PNG，未提供的图标保留内置样式。格式示例见发行包 Samples/Appearance。", "Themes include light and dark palettes. Icon ZIPs contain PNGs named by function; missing icons keep their built-in artwork. See Samples/Appearance in the release package.");
    public string ThemeStyle => L("界面风格", "Interface style");
    public string ThemeMode => L("明暗模式", "Color mode");
    public string[] ThemeStyles =>
    [
        L("原版 · 简洁", "Original · Minimal"),
        L("经典 Apple", "Classic Apple"),
        L("Windows XP · 经典蓝", "Windows XP · Luna"),
    ];
    public string LightMode => L("浅色", "Light");
    public string DarkMode => L("深色", "Dark");
    public string SystemMode => L("跟随系统", "Use system setting");
}
