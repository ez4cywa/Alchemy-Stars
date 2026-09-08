using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace AlchemyStars.Avalonia;

internal static class AppearanceSmoke
{
    private static readonly (int Index, string Key)[] Styles =
    [
        (0, "apple"),
        (1, "classic-apple"),
        (2, "windows-xp"),
    ];

    private static readonly (WorkspacePage Page, string Key)[] Pages =
    [
        (WorkspacePage.Animations, "animations"),
        (WorkspacePage.ModelParts, "parts"),
        (WorkspacePage.DualAnimations, "dual"),
        (WorkspacePage.Settings, "settings"),
        (WorkspacePage.About, "about"),
    ];

    internal static async Task RunAsync(MainWindow window, MainWindowViewModel vm)
    {
        var app = Application.Current!;
        var workspace = vm.Workspace;
        var stylePicker = window.FindControl<ComboBox>("ThemeStylePicker")!;
        var modePicker = window.FindControl<ComboBox>("ThemeModePicker")!;
        vm.SelectPage(WorkspacePage.Settings);
        var originalStyle = vm.ThemeStyleIndex;
        var originalMode = vm.ThemeModeIndex;
        var directory = Path.GetDirectoryName(Program.RenderSmokePath!)!;
        Directory.CreateDirectory(directory);
        try
        {
            // Change the actual frontend controls, not only the view-model values.
            foreach (var (style, styleKey) in Styles)
            foreach (var mode in new[] { 1, 0 })
            {
                vm.SelectPage(WorkspacePage.Settings);
                stylePicker.SelectedIndex = style;
                modePicker.SelectedIndex = mode;
                await Task.Delay(160);
                Require(vm.ThemeStyleIndex == style && vm.ThemeModeIndex == mode, "Appearance picker binding failed.");
                Require(ReferenceEquals(workspace, vm.Workspace), "Theme switch recreated the workspace.");
                Require(app.ActualThemeVariant == (mode == 1 ? ThemeVariant.Dark : ThemeVariant.Light), "Fluent theme mode did not switch.");
                Require(stylePicker.Bounds.Height == 44 && modePicker.Bounds.Height == 44, "Appearance control heights drifted.");
                var shadow = (BoxShadows)app.Resources["AppearancePanelShadow"]!;
                Require(shadow.Equals(BoxShadows.Parse("none")) == (style == 0), "Style switch left stale shadows.");
                Require((bool)app.Resources["AppearanceUseClassicIcons"]! == (style == 1), "Theme icon mode did not switch.");
                var snapshot = new ApplicationPreferencesStore().Snapshot();
                Require(snapshot.ThemeStyle == styleKey
                    && snapshot.ThemeMode == (mode == 1 ? "dark" : "light"), "Appearance selection did not survive a disk reload.");
                VerifyVerticalContentAlignment(window);
                VerifyThemedIcons(window, style == 1, style == 2, false);
                await VerifyQuickToggleAsync(window, vm, style, mode);
                if (style == 1) VerifyClassicAppleHasNoBlue(app);
                window.VerifyToolbarLayout();
                await VerifyTooltipsAsync(window, directory, style, mode);
                foreach (var (page, pageKey) in Pages)
                {
                    vm.SelectPage(page);
                    await Task.Delay(40);
                    window.VerifyToolbarLayout();
                    VerifyVerticalContentAlignment(window);
                    VerifyScrubberBounds(window);
                    VerifyThemedIcons(window, style == 1, style == 2, false);
                    if (page == WorkspacePage.Settings)
                    {
                        VerifyCheckBoxSkin(window, style);
                        foreach (var card in window.GetVisualDescendants().OfType<Border>().Where(border => border.Classes.Contains("card") && border.IsEffectivelyVisible))
                        {
                            var right = card.TranslatePoint(new Point(card.Bounds.Width, 0), window)!.Value.X;
                            Require(right <= window.ClientSize.Width, $"Settings card is clipped horizontally: {right} > {window.ClientSize.Width}.");
                        }
                    }
                    if (page == WorkspacePage.DualAnimations)
                    {
                        var toggle = window.GetVisualDescendants().OfType<ToggleButton>().Single(t => t.Name == "ExportModelsSwitch");
                        Require(toggle.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "XpSwitchTrack") == (style == 2), "Dual switch retained the wrong theme template.");
                        Require(toggle.GetVisualDescendants().OfType<Border>().Any(b => b.Name == "AppleSwitchTrack") == (style == 1), "Dual switch retained the wrong Apple template.");
                    }
                    using var bitmap = new RenderTargetBitmap(new PixelSize((int)window.ClientSize.Width, (int)window.ClientSize.Height));
                    bitmap.Render(window);
                    bitmap.Save(Path.Combine(directory, $"{styleKey}-{(mode == 1 ? "dark" : "light")}-{pageKey}.png"), PngBitmapEncoderOptions.Default);
                }
            }
            RenderXpIconCatalog(directory);
            RenderClassicIconCatalog(directory);
            vm.ThemeStyleIndex = 0;
            modePicker.SelectedIndex = 2;
            await Task.Delay(160);
            Require(app.RequestedThemeVariant == ThemeVariant.Default && vm.ThemeModeIndex == 2, "System mode did not delegate to Avalonia.");
            // Verify palette updates when the actual variant changes (same event
            // path used by OS light/dark notifications), without changing Windows.
            app.RequestedThemeVariant = ThemeVariant.Dark;
            await Task.Delay(80);
            Require(((SolidColorBrush)app.Resources["AlchemySurfaceBrush"]!).Color == Color.Parse("#272729"), "Actual dark change did not update palette.");
            app.RequestedThemeVariant = ThemeVariant.Light;
            await Task.Delay(80);
            Require(((SolidColorBrush)app.Resources["AlchemySurfaceBrush"]!).Color == Colors.White, "Actual light change did not update palette.");
            vm.ThemeStyleIndex = 1;
            // A quick toggle from system mode must use the actual palette, then save an explicit preference.
            app.RequestedThemeVariant = ThemeVariant.Dark;
            await Task.Delay(80);
            window.FindControl<Button>("AppearanceToggleButton")!.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            Require(vm.ThemeModeIndex == 0 && new ApplicationPreferencesStore().Snapshot().ThemeMode == "light", "System-dark quick toggle failed.");
            vm.ThemeModeIndex = 2;
            vm.ToggleLanguage();
            await Task.Delay(80);
            Require(vm.ThemeModeIndex == 2 && vm.ThemeStyleIndex == 1, "Language refresh reset appearance selection.");
            Require(modePicker.SelectedIndex == 2 && stylePicker.SelectedIndex == 1, "Language refresh cleared an appearance picker.");
            vm.ToggleLanguage();
            await CustomAppearanceSmoke.RunAsync(window, vm, directory);
            Console.WriteLine("Appearance smoke passed: three live styles, custom imports, two palettes, all pages, XP icon catalog, quick light/dark toggle, centered text, persistence, system delegation and language refresh.");
        }
        finally
        {
            vm.ThemeStyleIndex = originalStyle;
            vm.ThemeModeIndex = originalMode;
            AppearanceTheme.Apply(originalStyle switch { 1 => "classic-apple", 2 => "windows-xp", _ => "apple" }, originalMode switch { 1 => "dark", 2 => "system", _ => "light" });
        }
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void VerifyCheckBoxSkin(MainWindow window, int style)
    {
        var check = window.GetVisualDescendants().OfType<CheckBox>().First(control => control.IsEffectivelyVisible);
        var original = check.IsChecked;
        try
        {
            foreach (var state in new[] { false, true })
            {
                check.SetCurrentValue(ToggleButton.IsCheckedProperty, state);
                window.UpdateLayout();
                var mark = check.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Path>()
                    .FirstOrDefault(path => path.Name is "AppleCheckMark" or "XpCheckMark");
                var desktopMark = check.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Path>().FirstOrDefault(path => path.Name == "CheckMark");
                Require((desktopMark is not null) == (style == 0), "Desktop checkbox template leaked across themes.");
                if (desktopMark is not null)
                    Require(desktopMark.Opacity == (state ? 1 : 0), "Desktop checkbox mark did not follow its checked state.");
                Require((mark is not null) == (style is 1 or 2), "Checkbox retained the wrong control skin.");
                if (mark is not null)
                    Require(mark.Name == (style == 1 ? "AppleCheckMark" : "XpCheckMark") && mark.IsVisible == state,
                        "Checkbox mark did not follow its checked state.");
            }
        }
        finally { check.SetCurrentValue(ToggleButton.IsCheckedProperty, original); }
    }

    private static void VerifyScrubberBounds(MainWindow window)
    {
        foreach (var slider in window.GetVisualDescendants().OfType<Slider>().Where(s => s.Name == "FrameSlider" && s.IsEffectivelyVisible))
        {
            var thumb = slider.GetVisualDescendants().OfType<Thumb>().Single();
            var track = slider.GetVisualDescendants().OfType<Track>().Single();
            var maximum = track.Maximum;
            var value = track.Value;
            try
            {
                track.SetCurrentValue(Track.MaximumProperty, 100d);
                foreach (var position in new[] { 0d, 50d, 100d })
                {
                    track.SetCurrentValue(Track.ValueProperty, position);
                    window.UpdateLayout();
                    var origin = thumb.TranslatePoint(default, slider)!.Value;
                    Require(origin.X >= 0 && origin.Y >= 0 && origin.X + thumb.Bounds.Width <= slider.Bounds.Width
                        && origin.Y + thumb.Bounds.Height <= slider.Bounds.Height,
                        $"Playback thumb is clipped at {position}: thumb={origin}/{thumb.Bounds.Size}, slider={slider.Bounds.Size}.");
                }
            }
            finally
            {
                track.SetCurrentValue(Track.MaximumProperty, maximum);
                track.SetCurrentValue(Track.ValueProperty, value);
                window.UpdateLayout();
            }
        }
    }

    private static void VerifyVerticalContentAlignment(MainWindow window)
    {
        foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(button => button is not RepeatButton && button.IsEffectivelyVisible))
            Require(button.VerticalContentAlignment == global::Avalonia.Layout.VerticalAlignment.Center,
                $"Button text is not vertically centered: {AutomationProperties.GetName(button) ?? button.GetType().Name}.");
        foreach (var toggle in window.GetVisualDescendants().OfType<ToggleButton>().Where(control => control.IsEffectivelyVisible))
            Require(toggle.VerticalContentAlignment == global::Avalonia.Layout.VerticalAlignment.Center,
                $"Toggle text is not vertically centered: {AutomationProperties.GetName(toggle) ?? toggle.GetType().Name}.");
        foreach (var textBox in window.GetVisualDescendants().OfType<TextBox>().Where(control => !control.AcceptsReturn && control.IsEffectivelyVisible))
            Require(textBox.VerticalContentAlignment == global::Avalonia.Layout.VerticalAlignment.Center,
                $"TextBox text is not vertically centered: {AutomationProperties.GetName(textBox) ?? textBox.GetType().Name}.");
        foreach (var comboBox in window.GetVisualDescendants().OfType<ComboBox>().Where(control => control.IsEffectivelyVisible))
            Require(comboBox.VerticalContentAlignment == global::Avalonia.Layout.VerticalAlignment.Center,
                $"ComboBox text is not vertically centered: {AutomationProperties.GetName(comboBox) ?? comboBox.GetType().Name}.");
        foreach (var item in window.GetVisualDescendants().OfType<ListBoxItem>().Where(control => control.IsEffectivelyVisible))
            Require(item.VerticalContentAlignment == global::Avalonia.Layout.VerticalAlignment.Center,
                "List item text is not vertically centered.");
    }

    private static void VerifyThemedIcons(MainWindow window, bool classic, bool xp, bool desktop)
    {
        // Styles are applied lazily to hidden pages; visit every page and inspect its visible controls.
        var icons = window.GetVisualDescendants().OfType<ThemedIcon>().Where(icon => icon.IsEffectivelyVisible).ToArray();
        Require(icons.All(icon => icon.HasOriginalIcon == (!classic && !xp && !desktop)), "Original theme did not retain its bitmap icons.");
        Require(icons.All(icon => icon.UseDesktopGlyph == desktop && icon.HasDesktopIcon == desktop), "Desktop icons did not follow the selected theme.");
        Require(icons.Length >= 5, "The visible icon system was not instantiated.");
        Require(icons.All(icon => icon.UseClassicGlyph == classic), "A screen retained the wrong theme icon family.");
        Require(icons.All(icon => icon.UseWindowsXpGlyph == xp), "A screen retained the wrong XP icon mode.");
        Require(icons.All(icon => icon.HasClassicIcon == classic), "Classic Apple icon artwork did not follow its mode.");
        foreach (var icon in icons)
        {
            if (classic)
            {
                var expected = (icon.IconBrush as ISolidColorBrush)?.Color;
                // Other families can remain cached in hidden hosts after a round trip through XP.
                var visiblePaths = icon.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Path>()
                    .Where(path => path.IsEffectivelyVisible).ToArray();
                Require(visiblePaths.Length > 0, $"Classic Apple icon has no visible paths: {icon.Glyph}.");
                foreach (var path in visiblePaths)
                {
                    var brush = path.Stroke ?? path.Fill;
                    Require(brush is ISolidColorBrush solid && solid.Color == expected,
                        $"Classic Apple icon tint is stale: {icon.Glyph}, expected {expected}, actual {brush}.");
                }
            }
            var button = icon.GetVisualAncestors().OfType<Button>().FirstOrDefault();
            if (button is null) continue;
            var origin = icon.TranslatePoint(default, button)
                ?? throw new InvalidOperationException($"Theme icon '{icon.Glyph}' is detached from its button.");
            Require(origin.X >= 2 && origin.Y >= 2
                && origin.X + icon.Bounds.Width <= button.Bounds.Width - 2
                && origin.Y + icon.Bounds.Height <= button.Bounds.Height - 2,
                $"Theme icon '{icon.Glyph}' is obscured or clipped by its button.");
        }
    }

    private static async Task VerifyQuickToggleAsync(MainWindow window, MainWindowViewModel vm, int style, int mode)
    {
        var button = window.FindControl<Button>("AppearanceToggleButton")!;
        var label = button.Content?.ToString();
        for (var i = 0; i < 2; i++)
        {
            button.RaiseEvent(new global::Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
            await Task.Delay(80);
            var expected = i == 0 ? 1 - mode : mode;
            Require(vm.ThemeModeIndex == expected && vm.ThemeStyleIndex == style, "Quick toggle changed the style or failed to change mode.");
            Require(Application.Current!.ActualThemeVariant == (expected == 1 ? ThemeVariant.Dark : ThemeVariant.Light), "Quick toggle did not update the actual palette.");
            Require(new ApplicationPreferencesStore().Snapshot().ThemeMode == (expected == 1 ? "dark" : "light"), "Quick toggle was not persisted.");
            Require(button.Content?.ToString() == vm.ToggleAppearanceLabel, "Quick toggle label is stale.");
        }
        Require(button.Content?.ToString() == label, "Quick toggle label did not return to its original action.");
    }

    private static void RenderXpIconCatalog(string directory)
    {
        var panel = new Grid { Width = 840, Height = 550, Background = new SolidColorBrush(Color.Parse("#ece9d8")) };
        var index = 0;
        foreach (var glyph in ThemedIcon.GlyphNames)
        {
            var canvas = WindowsXpIcons.Create(glyph);
            foreach (var path in canvas.Children.OfType<global::Avalonia.Controls.Shapes.Path>())
            {
                var b = path.Data!.Bounds;
                Require(b.Left - path.StrokeThickness / 2 >= 0 && b.Top - path.StrokeThickness / 2 >= 0
                    && b.Right + path.StrokeThickness / 2 <= 24 && b.Bottom + path.StrokeThickness / 2 <= 24,
                    $"XP glyph clips its canvas: {glyph}, {b}");
            }
            var item = new StackPanel { Width = 140, Height = 90, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(index % 6 * 140, index / 6 * 90, 0, 0), Spacing = 6 };
            item.Children.Add(new Viewbox { Width = 40, Height = 40, Child = canvas, Margin = new Thickness(0, 10, 0, 0) });
            item.Children.Add(new TextBlock { Text = glyph, FontSize = 10, Foreground = Brushes.Black, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center });
            panel.Children.Add(item);
            index++;
        }
        panel.Measure(new Size(840, 550));
        panel.Arrange(new Rect(0, 0, 840, 550));
        using var bitmap = new RenderTargetBitmap(new PixelSize(840, 550));
        bitmap.Render(panel);
        bitmap.Save(Path.Combine(directory, "windows-xp-icon-catalog.png"), PngBitmapEncoderOptions.Default);
    }

    private static void RenderClassicIconCatalog(string directory)
    {
        var panel = new Grid { Width = 840, Height = 550, Background = new SolidColorBrush(Color.Parse("#eeece7")) };
        var index = 0;
        foreach (var glyph in ThemedIcon.GlyphNames)
        {
            var canvas = (Canvas)ClassicAppleIcons.Create(glyph, Brushes.DimGray);
            Require(canvas.Children.Count > 0, $"Classic Apple glyph missing: {glyph}");
            foreach (var path in canvas.Children.OfType<global::Avalonia.Controls.Shapes.Path>())
            {
                var b = path.Data!.Bounds;
                var stroke = path.Stroke is null ? 0 : path.StrokeThickness / 2;
                Require(b.Left - stroke >= 0 && b.Top - stroke >= 0 && b.Right + stroke <= 24 && b.Bottom + stroke <= 24,
                    $"Classic Apple glyph clips its canvas: {glyph}, {b}");
            }
            var item = new StackPanel { Width = 140, Height = 90, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Left,
                VerticalAlignment = global::Avalonia.Layout.VerticalAlignment.Top, Margin = new Thickness(index % 6 * 140, index / 6 * 90, 0, 0), Spacing = 6 };
            item.Children.Add(new Viewbox { Width = 40, Height = 40, Child = canvas, Margin = new Thickness(0, 10, 0, 0) });
            item.Children.Add(new TextBlock { Text = glyph, FontSize = 10, Foreground = Brushes.Black, HorizontalAlignment = global::Avalonia.Layout.HorizontalAlignment.Center });
            panel.Children.Add(item);
            index++;
        }
        panel.Measure(new Size(840, 550));
        panel.Arrange(new Rect(0, 0, 840, 550));
        using var bitmap = new RenderTargetBitmap(new PixelSize(840, 550));
        bitmap.Render(panel);
        bitmap.Save(Path.Combine(directory, "classic-apple-icon-catalog.png"), PngBitmapEncoderOptions.Default);
    }

    private static void VerifyClassicAppleHasNoBlue(Application app)
    {
        foreach (var key in new[]
        {
            "AlchemyAccentBrush",
            "AlchemyActionBrush",
            "AlchemyFocusBrush",
            "AlchemyAccentHoverBrush",
            "AlchemySelectedBrush",
            "AlchemyIconBrush",
        })
        {
            var color = ((SolidColorBrush)app.Resources[key]!).Color;
            Require(color.B <= color.R + 12 || color.B <= color.G + 6,
                $"Classic Apple contains a blue-dominant token: {key}={color}.");
        }
    }

    private static async Task VerifyTooltipsAsync(MainWindow window, string directory, int style, int mode)
    {
        var index = 0;
        foreach (var button in window.GetVisualDescendants().OfType<Button>().Where(b => b.Classes.Contains("toolbar")))
        {
            ToolTip.SetIsOpen(button, true);
            try
            {
                await Task.Delay(100);
                var tip = button.GetValue(global::Avalonia.Controls.Diagnostics.ToolTipDiagnostics.ToolTipProperty) as ToolTip
                    ?? throw new InvalidOperationException("Toolbar tooltip failed to open.");
                var text = tip.GetVisualDescendants().OfType<TextBlock>().Single(t => !string.IsNullOrEmpty(t.Text));
                var expected = ((SolidColorBrush)Application.Current!.Resources["AlchemyTextBrush"]!).Color;
                Require(text.Foreground is ISolidColorBrush foreground && foreground.Color == expected,
                    "Toolbar text color leaked into its tooltip.");
                Require(tip.Background is ISolidColorBrush background && background.Color != expected,
                    "Tooltip surface hides its text.");
                Require(text.Text == ToolTip.GetTip(button)?.ToString(), "Tooltip label changed.");
                using var bitmap = new RenderTargetBitmap(new PixelSize((int)Math.Ceiling(tip.Bounds.Width), (int)Math.Ceiling(tip.Bounds.Height)));
                bitmap.Render(tip);
                bitmap.Save(Path.Combine(directory, $"tooltip-{style}-{mode}-{index++}.png"), PngBitmapEncoderOptions.Default);
            }
            finally { ToolTip.SetIsOpen(button, false); }
        }
    }
}
