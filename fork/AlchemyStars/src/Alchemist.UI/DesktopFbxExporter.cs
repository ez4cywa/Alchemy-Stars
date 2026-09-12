using System.Diagnostics;
using System.IO;

namespace Alchemist.UI;

/// <summary>Use an installed DCC; Blender is preferred for an otherwise unconfigured workstation.</summary>
internal static class DesktopFbxExporter
{
    public static void Export(string castPath, string fbxPath, float framerate)
    {
        var backend = (Environment.GetEnvironmentVariable("ALCHEMY_STARS_FBX_BACKEND") ?? "auto").ToLowerInvariant();
        if (backend is not ("auto" or "blender" or "maya"))
            throw new InvalidOperationException("ALCHEMY_STARS_FBX_BACKEND must be auto, blender or maya.");
        var blender = backend == "maya" ? null : FindBlender();
        if (backend == "maya" || (backend == "auto" && blender is null))
        {
            MayaFbxExporter.Export(castPath, fbxPath, framerate);
            return;
        }
        if (blender is null) throw new FileNotFoundException("Blender was not found. Set ALCHEMY_STARS_BLENDER to blender.exe.");
        var script = FindScript() ?? throw new FileNotFoundException("Blender converter script is missing.");
        var start = new ProcessStartInfo(blender)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
        };
        foreach (var argument in new[] { "--background", "--factory-startup", "--python-exit-code", "1", "--python", script,
            "--", Path.GetFullPath(castPath), Path.GetFullPath(fbxPath) }) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Could not start Blender.");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(600_000))
        {
            process.Kill(entireProcessTree: true); process.WaitForExit();
            throw new TimeoutException("Blender FBX conversion timed out.");
        }
        var log = stdout.GetAwaiter().GetResult() + "\n" + stderr.GetAwaiter().GetResult();
        if (process.ExitCode != 0 || !File.Exists(fbxPath) || new FileInfo(fbxPath).Length == 0)
            throw new IOException("Blender FBX conversion failed: " + log[^Math.Min(log.Length, 8000)..]);
        Logging.Logger.Info("FBX exported using Blender: " + blender);
    }

    internal static string? FindBlender()
    {
        var configured = Environment.GetEnvironmentVariable("ALCHEMY_STARS_BLENDER");
        if (!string.IsNullOrWhiteSpace(configured))
            return File.Exists(configured) ? Path.GetFullPath(configured) : throw new FileNotFoundException("Configured Blender executable is missing.", configured);
        var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Blender Foundation");
        var preferred = Path.Combine(root, "Blender 4.3", "blender.exe");
        if (File.Exists(preferred)) return preferred;
        return Directory.Exists(root) ? Directory.EnumerateDirectories(root, "Blender *")
            .OrderByDescending(path => Version.TryParse(Path.GetFileName(path)[8..], out var version) ? version : new Version())
            .Select(path => Path.Combine(path, "blender.exe")).FirstOrDefault(File.Exists) : null;
    }

    private static string? FindScript()
    {
        foreach (var seed in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var directory = new DirectoryInfo(seed); directory is not null; directory = directory.Parent)
                foreach (var relative in new[] { "Converters/convert_cast.py", "blender/convert_cast.py" })
                {
                    var path = Path.Combine(directory.FullName, relative);
                    if (File.Exists(path)) return path;
                }
        return null;
    }
}
