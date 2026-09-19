using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Styling;

namespace AlchemyStars.Avalonia;

public sealed class ModelMergerPreviewWindow : Window
{
    private readonly Image image = new() { Stretch = Stretch.Uniform, Focusable = true };
    private readonly Border viewport = new() { ClipToBounds = true };
    private readonly TextBlock status = new() { TextWrapping = TextWrapping.Wrap };
    private readonly TextBlock info = new() { TextWrapping = TextWrapping.Wrap };
    private readonly CancellationTokenSource lifetime = new();
    private readonly string path;
    private readonly ModelMergerPreviewText text;
    private readonly List<Action> localizers = [];
    private string? errorMessage;
    private MergerPreviewData? data;
    private MergerPreviewCamera camera = MergerPreviewCamera.Default;
    private CancellationTokenSource? renderCancellation;
    private WriteableBitmap? bitmap;
    private Point? dragPoint;
    private bool grid = true;
    private bool closed;
    private int generation;

    public static void Open(string path, Window owner, UiText text) => new ModelMergerPreviewWindow(path, text).Show(owner);
    public static void Open(string path, Window owner, int language) => new ModelMergerPreviewWindow(path, language).Show(owner);
    internal bool HasRenderedModel => bitmap is not null;
    internal string? LoadError => errorMessage;

    public ModelMergerPreviewWindow(string path, UiText text) : this(path, text.ProductName == "炼金之星" ? 0 : 1) { }

    public ModelMergerPreviewWindow(string path, int language)
    {
        this.path = path;
        text = new ModelMergerPreviewText { Language = Math.Clamp(language, 0, 4) };
        localizers.Add(() => Title = $"{text.MergerPreviewTitle} — {System.IO.Path.GetFileName(path)}");
        Width = 1000; Height = 720; MinWidth = 620; MinHeight = 440;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        this.Bind(BackgroundProperty, this.GetResourceObservable("AlchemySurfaceBrush"));
        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(16) };
        var heading = new TextBlock { Text = System.IO.Path.GetFileName(path), FontSize = 18, FontWeight = FontWeight.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 0, 0, 12) };
        ToolTip.SetTip(heading, path);
        layout.Children.Add(heading);
        viewport.Child = image;
        var canvas = viewport;
        Grid.SetRow(canvas, 1); layout.Children.Add(canvas);
        var controls = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 12, 0, 8) };
        void Button(Func<string> label, Action action, Func<string>? accessibleName = null)
        {
            var button = new Button { Margin = new Thickness(0, 0, 8, 8), MinHeight = 32 };
            localizers.Add(() => { button.Content = label(); AutomationProperties.SetName(button, (accessibleName ?? label)()); ToolTip.SetTip(button, (accessibleName ?? label)()); });
            button.Click += (_, _) => action();
            controls.Children.Add(button);
        }
        Button(() => text.MergerPreviewReset, () => { camera = MergerPreviewCamera.Default; QueueRender(); });
        Button(() => "−", () => { camera = camera.Zoom(-1); QueueRender(); }, () => text.MergerPreviewZoomOut);
        Button(() => "+", () => { camera = camera.Zoom(1); QueueRender(); }, () => text.MergerPreviewZoomIn);
        Button(() => "←", () => { camera = camera.Orbit(-0.15f, 0); QueueRender(); }, () => text.MergerPreviewLeft);
        Button(() => "→", () => { camera = camera.Orbit(0.15f, 0); QueueRender(); }, () => text.MergerPreviewRight);
        Button(() => "↑", () => { camera = camera.Orbit(0, 0.15f); QueueRender(); }, () => text.MergerPreviewUp);
        Button(() => "↓", () => { camera = camera.Orbit(0, -0.15f); QueueRender(); }, () => text.MergerPreviewDown);
        var gridLabel = new TextBlock();
        gridLabel.Bind(TextBlock.ForegroundProperty, this.GetResourceObservable("AlchemyTextBrush"));
        var gridToggle = new CheckBox { Content = gridLabel, IsChecked = true, VerticalAlignment = VerticalAlignment.Center };
        localizers.Add(() => { gridLabel.Text = text.MergerPreviewGrid; AutomationProperties.SetName(gridToggle, text.MergerPreviewGrid); });
        gridToggle.IsCheckedChanged += (_, _) => { grid = gridToggle.IsChecked == true; QueueRender(); };
        controls.Children.Add(gridToggle);
        Grid.SetRow(controls, 2); layout.Children.Add(controls);
        var footer = new StackPanel { Spacing = 6 };
        status.Text = text.MergerPreviewLoading;
        footer.Children.Add(status);
        footer.Children.Add(info);
        var help = new TextBlock { FontSize = 12, TextWrapping = TextWrapping.Wrap };
        localizers.Add(() => help.Text = text.MergerPreviewHelp);
        footer.Children.Add(help);
        Grid.SetRow(footer, 3); layout.Children.Add(footer);
        Content = layout;
        image.PointerPressed += (_, e) =>
        {
            if (!e.GetCurrentPoint(image).Properties.IsLeftButtonPressed) return;
            image.Focus(); dragPoint = e.GetPosition(image); e.Pointer.Capture(image); e.Handled = true;
        };
        image.PointerMoved += (_, e) =>
        {
            if (dragPoint is not { } previous) return;
            var next = e.GetPosition(image);
            camera = camera.Orbit((float)(next.X - previous.X) * 0.008f, (float)(next.Y - previous.Y) * 0.008f);
            dragPoint = next; QueueRender();
        };
        image.PointerReleased += (_, e) => { dragPoint = null; e.Pointer.Capture(null); };
        image.PointerCaptureLost += (_, _) => dragPoint = null;
        image.PointerWheelChanged += (_, e) => { camera = camera.Zoom((float)e.Delta.Y); QueueRender(); e.Handled = true; };
        KeyDown += (_, e) =>
        {
            switch (e.Key)
            {
                case Key.Escape: Close(); break;
                case Key.R: camera = MergerPreviewCamera.Default; break;
                case Key.G: gridToggle.IsChecked = !grid; break;
                case Key.Left: camera = camera.Orbit(-0.15f, 0); break;
                case Key.Right: camera = camera.Orbit(0.15f, 0); break;
                case Key.Up: camera = camera.Orbit(0, 0.15f); break;
                case Key.Down: camera = camera.Orbit(0, -0.15f); break;
                case Key.Add: case Key.OemPlus: camera = camera.Zoom(1); break;
                case Key.Subtract: case Key.OemMinus: camera = camera.Zoom(-1); break;
                default: return;
            }
            e.Handled = true; QueueRender();
        };
        canvas.SizeChanged += (_, _) => QueueRender();
        ActualThemeVariantChanged += (_, _) => QueueRender();
        Opened += async (_, _) => { RestorePlacement(); await LoadAsync(); };
        Closing += (_, _) => SavePlacement();
        Closed += (_, _) =>
        {
            closed = true; lifetime.Cancel(); renderCancellation?.Cancel();
            image.Source = null; bitmap?.Dispose(); lifetime.Dispose();
        };
        UpdateLanguage(language);
    }

    public void UpdateLanguage(int language)
    {
        text.Language = Math.Clamp(language, 0, 4);
        foreach (var localize in localizers) localize();
        RefreshStatus();
    }

    private void RefreshStatus()
    {
        if (errorMessage is not null) { status.Text = text.MergerPreviewError + " " + errorMessage; return; }
        if (data is null) { status.Text = text.MergerPreviewLoading; return; }
        var size = data.Maximum - data.Minimum;
        status.Text = $"{text.MergerPreviewStats}: {data.MeshCount:N0} / {data.VertexCount:N0} / {data.TriangleCount:N0}";
        info.Text = $"{text.MergerPreviewDisplayed}: {data.DisplayedTriangles:N0} · {text.MergerPreviewBounds}: {size.X:G5} × {size.Y:G5} × {size.Z:G5}";
    }

    private async Task LoadAsync()
    {
        try
        {
            data = await LoadDataAsync(path, lifetime.Token);
            if (closed) return;
            RefreshStatus();
            QueueRender(); image.Focus();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!closed) { errorMessage = error.Message; RefreshStatus(); } }
    }

    internal static async Task<MergerPreviewData> LoadDataAsync(string path, CancellationToken cancellation)
    {
        var executable = ModelMergerExecutable();
        using var process = new Process { StartInfo = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8 } };
        if (!process.Start()) throw new IOException("Could not start ModelMerger preview backend.");
        using var registration = cancellation.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } });
        var errors = process.StandardError.ReadToEndAsync(cancellation);
        using var requestStream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(requestStream))
        {
            writer.WriteStartObject(); writer.WriteString("command", "preview"); writer.WriteString("file_path", path);
            writer.WriteNumber("triangle_limit", MergerPreviewData.TriangleLimit); writer.WriteEndObject();
        }
        await process.StandardInput.WriteLineAsync(Encoding.UTF8.GetString(requestStream.ToArray()).AsMemory(), cancellation);
        await process.StandardInput.FlushAsync(cancellation);
        try
        {
            while (await process.StandardOutput.ReadLineAsync(cancellation).ConfigureAwait(false) is { } line)
            {
                using var document = JsonDocument.Parse(line);
                var root = document.RootElement;
                var kind = root.GetProperty("event").GetString();
                if (kind == "error") throw new InvalidDataException(root.GetProperty("message").GetString());
                if (kind == "preview")
                {
                    var result = await Task.Run(() => MergerPreviewData.Parse(root), cancellation);
                    await process.WaitForExitAsync(cancellation);
                    if (process.ExitCode != 0) throw new InvalidDataException("Preview backend failed: " + await errors);
                    return result;
                }
            }
            cancellation.ThrowIfCancellationRequested();
            throw new InvalidDataException("Preview backend ended without a result. " + await errors);
        }
        finally { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } }
    }

    private static string ModelMergerExecutable()
    {
        var configured = Environment.GetEnvironmentVariable("ALCHEMY_MODEL_MERGER_PATH");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured)) return configured;
        var bundled = System.IO.Path.Combine(AppContext.BaseDirectory, "Converters", "alchemy-model-merger.exe");
        if (File.Exists(bundled)) return bundled;
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var development = System.IO.Path.Combine(directory.FullName, "third_party", "modelmerger", "rust", "target", "release", "alchemy-model-merger.exe");
            if (File.Exists(development)) return development;
        }
        throw new FileNotFoundException("ModelMerger backend is missing. Reinstall the complete release package.", bundled);
    }

    private static string PlacementPath => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ALCHEMY_STARS_SETTINGS_PATH"))
        ? System.IO.Path.Combine(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(Environment.GetEnvironmentVariable("ALCHEMY_STARS_SETTINGS_PATH")!))!, "model-merger-preview-window.json")
        : System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Alchemy Stars", "model-merger-preview-window.json");

    private void RestorePlacement()
    {
        try
        {
            if (!File.Exists(PlacementPath)) return;
            using var json = JsonDocument.Parse(File.ReadAllText(PlacementPath));
            var root = json.RootElement;
            var width = root.GetProperty("width").GetDouble(); var height = root.GetProperty("height").GetDouble();
            if (double.IsFinite(width) && double.IsFinite(height)) { Width = Math.Clamp(width, MinWidth, 3840); Height = Math.Clamp(height, MinHeight, 2160); }
            var point = new PixelPoint(root.GetProperty("x").GetInt32(), root.GetProperty("y").GetInt32());
            if (Screens.All.Any(screen => screen.WorkingArea.Contains(point) && screen.WorkingArea.Contains(new PixelPoint(point.X + 120, point.Y + 80)))) Position = point;
        }
        catch (Exception error) when (error is IOException or JsonException or InvalidOperationException or UnauthorizedAccessException or KeyNotFoundException or FormatException or OverflowException) { }
    }

    private void SavePlacement()
    {
        if (WindowState != WindowState.Normal) return;
        try
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject(); writer.WriteNumber("width", Width); writer.WriteNumber("height", Height);
                writer.WriteNumber("x", Position.X); writer.WriteNumber("y", Position.Y); writer.WriteEndObject();
            }
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PlacementPath)!);
            var temporary = PlacementPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try { File.WriteAllBytes(temporary, stream.ToArray()); File.Move(temporary, PlacementPath, true); }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException) { }
    }

    private async void QueueRender()
    {
        if (closed || data is null || viewport.Bounds.Width < 2 || viewport.Bounds.Height < 2) return;
        renderCancellation?.Cancel();
        var cancellation = new CancellationTokenSource();
        renderCancellation = cancellation;
        var id = ++generation;
        // Bound pixel cost, while preserving aspect ratio; mesh sampling is exclusively performed by Rust.
        var scale = Math.Min(1, 1200 / Math.Max(viewport.Bounds.Width, viewport.Bounds.Height));
        var width = Math.Max(1, (int)(viewport.Bounds.Width * scale));
        var height = Math.Max(1, (int)(viewport.Bounds.Height * scale));
        var capturedData = data; var capturedCamera = camera; var capturedGrid = grid;
        var dark = ActualThemeVariant == ThemeVariant.Dark;
        try
        {
            await Task.Delay(20, cancellation.Token);
            var pixels = await Task.Run(() => ModelMergerPreviewRenderer.Render(capturedData, capturedCamera, width, height, capturedGrid, dark, cancellation.Token), cancellation.Token);
            if (closed || generation != id) return;
            var next = new WriteableBitmap(new PixelSize(width, height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Opaque);
            using (var framebuffer = next.Lock())
                for (var y = 0; y < height; y++) Marshal.Copy(pixels, y * width, framebuffer.Address + y * framebuffer.RowBytes, width);
            var old = bitmap; bitmap = next; image.Source = next; old?.Dispose();
        }
        catch (OperationCanceledException) { }
        catch (Exception error) { if (!closed) { errorMessage = error.Message; RefreshStatus(); } }
        finally { if (ReferenceEquals(renderCancellation, cancellation)) renderCancellation = null; cancellation.Dispose(); }
    }
}

internal sealed class ModelMergerPreviewText
{
    internal int Language { get; set; }
    private string L(string zh, string en, string fr, string ru, string es) => Language switch { 0 => zh, 2 => fr, 3 => ru, 4 => es, _ => en };
    public string MergerPreviewTitle => L("模型合并 · 只读预览", "Model merger · Read-only preview", "Fusion · Aperçu en lecture seule", "Объединение · Просмотр без изменений", "Fusión · Vista de solo lectura");
    public string MergerPreviewReset => L("重置视角", "Reset view", "Réinitialiser la vue", "Сбросить вид", "Restablecer vista");
    public string MergerPreviewGrid => L("网格", "Grid", "Grille", "Сетка", "Cuadrícula");
    public string MergerPreviewZoomOut => L("缩小", "Zoom out", "Dézoomer", "Отдалить", "Alejar");
    public string MergerPreviewZoomIn => L("放大", "Zoom in", "Zoomer", "Приблизить", "Acercar");
    public string MergerPreviewLeft => L("向左旋转", "Orbit left", "Tourner à gauche", "Повернуть влево", "Girar a la izquierda");
    public string MergerPreviewRight => L("向右旋转", "Orbit right", "Tourner à droite", "Повернуть вправо", "Girar a la derecha");
    public string MergerPreviewUp => L("向上旋转", "Orbit up", "Tourner vers le haut", "Повернуть вверх", "Girar hacia arriba");
    public string MergerPreviewDown => L("向下旋转", "Orbit down", "Tourner vers le bas", "Повернуть вниз", "Girar hacia abajo");
    public string MergerPreviewLoading => L("正在读取 CAST 模型…", "Reading CAST model…", "Lecture du modèle CAST…", "Чтение модели CAST…", "Leyendo modelo CAST…");
    public string MergerPreviewStats => L("网格 / 顶点 / 三角面", "Meshes / vertices / triangles", "Maillages / sommets / triangles", "Сетки / вершины / треугольники", "Mallas / vértices / triángulos");
    public string MergerPreviewDisplayed => L("预览三角面", "Displayed triangles", "Triangles affichés", "Показано треугольников", "Triángulos mostrados");
    public string MergerPreviewBounds => L("尺寸", "Dimensions", "Dimensions", "Размеры", "Dimensiones");
    public string MergerPreviewError => L("预览失败，请检查 CAST 文件或重新打开：", "Preview failed. Check the CAST file or reopen it:", "Échec de l’aperçu. Vérifiez le fichier CAST ou rouvrez-le :", "Ошибка просмотра. Проверьте CAST или откройте его снова:", "Error de vista previa. Revise el CAST o vuelva a abrirlo:");
    public string MergerPreviewHelp => L("拖动旋转 · 滚轮或 +/− 缩放 · 方向键旋转 · R 重置 · G 网格 · Esc 关闭。软件深度缓冲；最多显示 250,000 个三角面，不修改原文件。", "Drag to orbit · Wheel or +/− to zoom · Arrows to orbit · R reset · G grid · Esc close. Software depth buffer; at most 250,000 preview triangles; source files remain unchanged.", "Glisser : rotation · Molette ou +/− : zoom · Flèches : rotation · R : réinitialiser · G : grille · Échap : fermer. Tampon de profondeur logiciel ; 250 000 triangles maximum ; sources inchangées.", "Перетаскивание: вращение · Колесо или +/−: масштаб · Стрелки: вращение · R: сброс · G: сетка · Esc: закрыть. Программный буфер глубины; до 250 000 треугольников; исходники не изменяются.", "Arrastrar: girar · Rueda o +/−: zoom · Flechas: girar · R: restablecer · G: cuadrícula · Esc: cerrar. Búfer de profundidad por software; hasta 250 000 triángulos; fuentes sin cambios.");
}
