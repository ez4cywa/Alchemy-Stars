using Avalonia;
using System.Globalization;

namespace AlchemyStars.Avalonia;

internal static class Program
{
    internal static bool StartupSmokeRequested { get; private set; }
    internal static bool DesktopSmokeRequested { get; private set; }
    internal static bool AppearanceSmokeRequested { get; private set; }
    internal static bool AppearanceSamplesSmokeRequested { get; private set; }
    internal static string? RenderSmokePath { get; private set; }
    internal static bool UbuntuThemeSmokeRequested { get; private set; }
    internal static PixelSize? RenderSmokeSize { get; private set; }
    internal static WorkspacePage? RenderSmokePage { get; private set; }
    internal static string? RenderDialogKind { get; private set; }
    internal static string? StartupProjectPath { get; private set; }
    internal static bool AccessibilitySmokeRequested { get; private set; }
    internal static bool AccessibilityInteractionSmokeRequested { get; private set; }
    internal static string? PreviewSmokePath { get; private set; }
    internal static bool BuildPreviewSmoke { get; private set; }
    internal static bool FirstPersonPreviewRequested { get; private set; }
    internal static string? ExternalLinkLogPath { get; private set; }
    internal static string? UpdateResultPath { get; private set; }
    internal static bool UtilitiesSmokeRequested { get; private set; }
    internal static bool ShortcutsSmokeRequested { get; private set; }
    internal static bool TextMenuSmokeRequested { get; private set; }
    internal static bool InspectorSmokeRequested { get; private set; }
    internal static bool WindowChromeSmokeRequested { get; private set; }
    internal static bool SharedBaseBatchSmokeRequested { get; private set; }

    [STAThread]
    public static int Main(string[] args)
    {
        if (args.Length > 0 && args[0] == "--apply-update") return UpdateInstaller.Run(args);
        if (args.Contains("--update-self-test", StringComparer.OrdinalIgnoreCase)) return UpdateSelfTest.Run();
        if (args.Contains("--update-helper-self-test", StringComparer.OrdinalIgnoreCase)) return UpdateSelfTest.RunHelper();
        if (args.Length > 0 && args[0] == "--update-launch-self-test") return UpdateSelfTest.RunLauncher(args);
        if (args.Contains("--update-check-smoke", StringComparer.OrdinalIgnoreCase)) return UpdateSelfTest.RunLiveCheck();
        var cultureName = GetOption(args, "--culture");
        if (cultureName is not null)
        {
            var culture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentCulture = culture;
            CultureInfo.CurrentUICulture = culture;
        }

        if (args.Contains("--self-test", StringComparer.OrdinalIgnoreCase))
            return SelfTest.Run();
        var gripIndex = Array.IndexOf(args, "--grip-smoke");
        if (gripIndex >= 0) return GripSmoke.Run(args.Skip(gripIndex + 1).ToArray());
        var combinedIndex = Array.IndexOf(args, "--combined-dual-smoke");
        if (combinedIndex >= 0) return CombinedDualSmoke.Run(args.Skip(combinedIndex + 1).ToArray());
        var dualIndex = Array.IndexOf(args, "--dual-smoke");
        if (dualIndex >= 0) return DualWieldSmoke.Run(args.Skip(dualIndex + 1).ToArray());
        if (GetOption(args, "--preview-test") is { } previewTest)
            return SelfTest.RunPreview(previewTest, GetOption(args, "--skeleton-project"));

        var hawkArgumentIndex = Array.FindIndex(args, argument =>
            argument.Equals("--hawk-smoke", StringComparison.OrdinalIgnoreCase));
        if (hawkArgumentIndex >= 0)
            return SelfTest.RunHawk(args.Skip(hawkArgumentIndex + 1).ToArray());

        var projectArgumentIndex = Array.FindIndex(args, argument =>
            argument.Equals("--project-smoke", StringComparison.OrdinalIgnoreCase));
        if (projectArgumentIndex >= 0)
            return SelfTest.RunProject(args.Skip(projectArgumentIndex + 1).ToArray());

        var renderPath = GetOption(args, "--render-smoke");
        UbuntuThemeSmokeRequested = args.Contains("--ubuntu-theme-smoke", StringComparer.OrdinalIgnoreCase);
        DesktopSmokeRequested = args.Contains("--desktop-smoke", StringComparer.OrdinalIgnoreCase);
        AppearanceSmokeRequested = args.Contains("--appearance-smoke", StringComparer.OrdinalIgnoreCase);
        AppearanceSamplesSmokeRequested = args.Contains("--appearance-samples-smoke", StringComparer.OrdinalIgnoreCase);
        RenderSmokePath = renderPath is not null
            ? Path.GetFullPath(renderPath)
            : null;
        var renderSize = GetOption(args, "--window-size");
        RenderSmokeSize = renderSize is null ? null : ParseSize(renderSize);
        RenderSmokePage = ParsePage(GetOption(args, "--page"));
        RenderDialogKind = GetOption(args, "--dialog");
        PreviewSmokePath = GetOption(args, "--preview-cast");
        BuildPreviewSmoke = args.Contains("--build-preview", StringComparer.OrdinalIgnoreCase);
        FirstPersonPreviewRequested = args.Contains("--first-person-preview", StringComparer.OrdinalIgnoreCase);
        AccessibilitySmokeRequested = args.Contains("--accessibility-smoke", StringComparer.OrdinalIgnoreCase);
        AccessibilityInteractionSmokeRequested = args.Contains("--accessibility-interaction-smoke", StringComparer.OrdinalIgnoreCase);
        ExternalLinkLogPath = GetOption(args, "--external-link-log");
        UpdateResultPath = GetOption(args, "--update-result");
        UtilitiesSmokeRequested = args.Contains("--utilities-smoke", StringComparer.OrdinalIgnoreCase);
        ShortcutsSmokeRequested = args.Contains("--shortcuts-smoke", StringComparer.OrdinalIgnoreCase);
        TextMenuSmokeRequested = args.Contains("--textmenu-smoke", StringComparer.OrdinalIgnoreCase);
        InspectorSmokeRequested = args.Contains("--inspector-smoke", StringComparer.OrdinalIgnoreCase);
        WindowChromeSmokeRequested = args.Contains("--window-chrome-smoke", StringComparer.OrdinalIgnoreCase);
        SharedBaseBatchSmokeRequested = args.Contains("--shared-base-batch-smoke", StringComparer.OrdinalIgnoreCase);
        StartupProjectPath = args
            .Where(argument => !argument.StartsWith("--", StringComparison.Ordinal))
            .FirstOrDefault(argument => string.Equals(Path.GetExtension(argument), ".aprj", StringComparison.OrdinalIgnoreCase) && File.Exists(argument));
        StartupSmokeRequested = RenderSmokePath is not null
            || args.Contains("--startup-smoke", StringComparer.OrdinalIgnoreCase);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        return 0;
    }

    public static AppBuilder BuildAvaloniaApp() => AppBuilder
        .Configure<App>()
        .UsePlatformDetect()
        .LogToTrace();

    private static string? GetOption(IReadOnlyList<string> arguments, string name)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
        {
            if (arguments[index].Equals(name, StringComparison.OrdinalIgnoreCase))
                return arguments[index + 1];
        }
        return null;
    }

    private static PixelSize ParseSize(string value)
    {
        var separator = value.IndexOfAny(['x', 'X']);
        if (separator <= 0
            || !int.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var width)
            || !int.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var height)
            || width < 900
            || height < 600)
        {
            throw new ArgumentException("Window size must be WIDTHxHEIGHT and at least 900x600.", nameof(value));
        }
        return new PixelSize(width, height);
    }

    private static WorkspacePage? ParsePage(string? value) => value?.ToLowerInvariant() switch
    {
        "animations" => WorkspacePage.Animations,
        "parts" => WorkspacePage.ModelParts,
        "dual" => WorkspacePage.DualAnimations,
        "settings" => WorkspacePage.Settings,
        "about" => WorkspacePage.About,
        _ => null,
    };
}
