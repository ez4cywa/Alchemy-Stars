using System.Diagnostics;
using System.Numerics;
using System.Text.Json;

namespace AlchemyStars.Avalonia;

internal static class ModelMergerPreviewSmoke
{
    internal static async Task RunUiAsync(global::Avalonia.Controls.Window owner, string path, string directory)
    {
        Directory.CreateDirectory(directory);
        var window = new ModelMergerPreviewWindow(path, 0) { Width = 900, Height = 680 };
        window.Show(owner);
        try
        {
            var timeout = Stopwatch.StartNew();
            while (!window.HasRenderedModel && window.LoadError is null && timeout.Elapsed < TimeSpan.FromSeconds(30)) await Task.Delay(50);
            Require(window.HasRenderedModel, "preview window did not render: " + window.LoadError);
            for (var language = 0; language < 5; language++)
            {
                window.UpdateLanguage(language);
                window.RequestedThemeVariant = language % 2 == 0 ? global::Avalonia.Styling.ThemeVariant.Dark : global::Avalonia.Styling.ThemeVariant.Light;
                await Task.Delay(180);
                using var bitmap = new global::Avalonia.Media.Imaging.RenderTargetBitmap(new global::Avalonia.PixelSize((int)window.Bounds.Width, (int)window.Bounds.Height), new global::Avalonia.Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(Path.Combine(directory, $"model-merger-preview-{language}.png"), global::Avalonia.Media.Imaging.PngBitmapEncoderOptions.Default);
            }
        }
        finally { window.Close(); }
    }

    internal static int Run(string[] args)
    {
        try
        {
            VerifyDepth();
            using var document = Fixture(1, 65_539);
            var data = MergerPreviewData.Parse(document.RootElement);
            Require(data.Meshes[0].Indices[0] == 65_536, "32-bit mesh index was narrowed");
            var image = ModelMergerPreviewRenderer.Render(data, MergerPreviewCamera.Default, 160, 120, false, true, default);
            Require(image.Any(p => p != unchecked((int)0xff252527)), "32-bit indexed geometry not drawn");
            using var capped = Fixture(MergerPreviewData.TriangleLimit, 3);
            Require(MergerPreviewData.Parse(capped.RootElement).DisplayedTriangles == 250_000, "triangle limit boundary");
            using var oversized = Fixture(MergerPreviewData.TriangleLimit + 1, 3);
            var rejected = false;
            try { MergerPreviewData.Parse(oversized.RootElement); } catch (InvalidDataException) { rejected = true; }
            Require(rejected, "oversized preview payload must be rejected");
            var camera = MergerPreviewCamera.Default;
            Require(camera.Orbit(0.3f, 0).Yaw != camera.Yaw, "horizontal orbit");
            Require(camera.Orbit(0, 100).Pitch == 1.5f && camera.Orbit(0, -100).Pitch == -1.5f, "pole clamp");
            Require(camera.Zoom(1).Distance < camera.Distance && camera.Zoom(-1).Distance > camera.Distance, "zoom direction");
            Require(camera.Zoom(1000).Distance == 1.15f && camera.Zoom(-1000).Distance == 30, "zoom bounds");
            var grid = ModelMergerPreviewRenderer.Render(data, camera, 160, 120, true, true, default);
            Require(!grid.SequenceEqual(image), "grid toggle");
            var light = ModelMergerPreviewRenderer.Render(data, camera, 160, 120, false, false, default);
            Require(!light.SequenceEqual(image), "light/dark surface");
            using var cancelled = new CancellationTokenSource(); cancelled.Cancel();
            rejected = false;
            try { ModelMergerPreviewRenderer.Render(data, camera, 160, 120, false, true, cancelled.Token); }
            catch (OperationCanceledException) { rejected = true; }
            Require(rejected, "render cancellation");
            var index = Array.IndexOf(args, "--model-merger-preview-file");
            if (index >= 0 && index + 1 < args.Length)
            {
                var path = args[index + 1];
                var before = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
                var loaded = ModelMergerPreviewWindow.LoadDataAsync(path, default).GetAwaiter().GetResult();
                Require(loaded.DisplayedTriangles > 0 && loaded.DisplayedTriangles <= MergerPreviewData.TriangleLimit, "backend preview sampling");
                var after = System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(path));
                Require(before.SequenceEqual(after), "preview changed source file");
                Console.WriteLine($"Backend preview: {loaded.Name}, {loaded.DisplayedTriangles} / {loaded.TriangleCount} triangles.");
            }
            Console.WriteLine("ModelMerger preview smoke passed: depth order, crossing geometry, u32 indices, 250k cap, camera, grid, theme, cancellation.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static void VerifyDepth()
    {
        int[] Draw(bool reverse)
        {
            var pixels = new int[32 * 32]; var depth = new float[pixels.Length]; Array.Fill(depth, float.PositiveInfinity);
            void A() => ModelMergerPreviewRenderer.Triangle(new(2, 2, 1), new(30, 2, 8), new(2, 30, 1), 11, 32, 32, pixels, depth);
            void B() => ModelMergerPreviewRenderer.Triangle(new(2, 2, 3), new(30, 2, 3), new(2, 30, 3), 22, 32, 32, pixels, depth);
            if (reverse) { B(); A(); } else { A(); B(); }
            return pixels;
        }
        var first = Draw(false);
        Require(first.SequenceEqual(Draw(true)), "depth output depends on triangle submission order");
        Require(first.Contains(11) && first.Contains(22), "crossing triangles did not both win at different pixels");
    }

    private static JsonDocument Fixture(int triangles, int vertices)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject(); writer.WriteString("model_name", "smoke");
            writer.WriteNumber("source_mesh_count", 1); writer.WriteNumber("source_vertex_count", vertices);
            writer.WriteNumber("source_triangle_count", triangles);
            writer.WriteStartObject("bounds");
            writer.WriteStartArray("minimum"); writer.WriteNumberValue(-1); writer.WriteNumberValue(-1); writer.WriteNumberValue(0); writer.WriteEndArray();
            writer.WriteStartArray("maximum"); writer.WriteNumberValue(1); writer.WriteNumberValue(1); writer.WriteNumberValue(0); writer.WriteEndArray(); writer.WriteEndObject();
            writer.WriteStartArray("meshes"); writer.WriteStartObject(); writer.WriteStartArray("positions");
            for (var i = 0; i < vertices; i++)
            {
                var n = i - (vertices - 3);
                writer.WriteStartArray(); writer.WriteNumberValue(n == 0 ? -1 : n == 1 ? 1 : 0);
                writer.WriteNumberValue(n == 2 ? 1 : -1); writer.WriteNumberValue(0); writer.WriteEndArray();
            }
            writer.WriteEndArray(); writer.WriteStartArray("triangle_indices");
            for (var i = 0; i < triangles; i++) { writer.WriteNumberValue(vertices - 3); writer.WriteNumberValue(vertices - 2); writer.WriteNumberValue(vertices - 1); }
            writer.WriteEndArray(); writer.WriteEndObject(); writer.WriteEndArray(); writer.WriteEndObject();
        }
        return JsonDocument.Parse(stream.ToArray());
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("ModelMerger preview: " + message);
    }
}
