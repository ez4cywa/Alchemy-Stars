using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;

namespace AlchemyStars.Avalonia;

/// <summary>Runs from a copied Native AOT executable outside the installation directory.</summary>
internal static class UpdateInstaller
{
    internal const string ExecutableName = "AlchemyStars.Avalonia.exe";
    private const long MaximumExpandedSize = 2_147_483_648;

    internal static int Run(string[] args)
    {
        string? work = null;
        string? target = null;
        bool parentExited = false;
        try
        {
            if (args.Length is not (7 or 8) || !int.TryParse(args[4], out var processId)
                || !long.TryParse(args[5], out var startTicks) || args[6] is not ("restart" or "exit"))
                throw new ArgumentException("Invalid update-helper arguments.");
            var archive = Path.GetFullPath(args[1]);
            work = Path.GetDirectoryName(archive)!;
            var helperPath = Path.GetFullPath(Environment.ProcessPath!);
            if (!string.Equals(Path.GetDirectoryName(helperPath), work, StringComparison.OrdinalIgnoreCase)
                || !Path.GetFileName(work).StartsWith("AlchemyStars-Update-", StringComparison.Ordinal)
                || !work.StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The update helper must run in its own temporary directory.");
            target = Path.GetFullPath(args[3]);
            EnsureNoLinks(target);
            if (!File.Exists(Path.Combine(target, ExecutableName)) || target.Equals(work, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The installation directory is invalid.");
            GitHubUpdateService.VerifyArchive(archive, args[2]);
            ValidateArchive(archive);
            try
            {
                using var parent = Process.GetProcessById(processId);
                if (parent.StartTime.ToUniversalTime().Ticks != startTicks)
                    throw new InvalidOperationException("The original application process no longer matches.");
                File.WriteAllText(Path.Combine(work, "ready"), Environment.ProcessId.ToString(CultureInfo.InvariantCulture));
                if (!parent.WaitForExit(120_000))
                    throw new TimeoutException("The application did not exit; no files were changed.");
            }
            catch (ArgumentException) { File.WriteAllText(Path.Combine(work, "ready"), Environment.ProcessId.ToString(CultureInfo.InvariantCulture)); }
            parentExited = true;

            var stage = Path.Combine(work, "staged");
            Directory.CreateDirectory(stage);
            ExtractArchive(archive, stage);
            ApplyFiles(stage, target, Path.Combine(work, "backup"));
            File.WriteAllText(Path.Combine(work, "result.txt"), "SUCCESS\nUpdate installed successfully.");
            // Cleanup is best-effort and must never turn a successful installation into a second restart.
            try
            {
                File.Delete(archive);
                Directory.Delete(stage, true);
                Directory.Delete(Path.Combine(work, "backup"), true);
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
            if (args[6] == "restart")
            {
                var restart = new ProcessStartInfo(Path.Combine(target, ExecutableName)) { UseShellExecute = true, WorkingDirectory = target };
                if (args.Length == 8) restart.ArgumentList.Add(Path.GetFullPath(args[7]));
                restart.ArgumentList.Add("--update-result");
                restart.ArgumentList.Add(Path.Combine(work, "result.txt"));
                using (Process.Start(restart)) { }
            }
            // The running helper cannot delete itself on Windows; the result remains available to the UI.
            return 0;
        }
        catch (Exception exception)
        {
            if (work is not null)
            {
                try
                {
                    var result = Path.Combine(work, "result.txt");
                    File.WriteAllText(result, "ERROR\n" + exception);
                    if (parentExited && target is not null)
                    {
                        try
                        {
                            var restart = new ProcessStartInfo(Path.Combine(target, ExecutableName)) { UseShellExecute = true, WorkingDirectory = target };
                            if (args.Length == 8) restart.ArgumentList.Add(Path.GetFullPath(args[7]));
                            restart.ArgumentList.Add("--update-result");
                            restart.ArgumentList.Add(result);
                            using (Process.Start(restart)) { }
                        }
                        catch (Exception) { using (Process.Start(new ProcessStartInfo(result) { UseShellExecute = true })) { } }
                    }
                }
                catch (Exception) { /* Preserve the backup even if Windows cannot display the failure. */ }
            }
            return 1;
        }
    }

    internal static void ValidateArchive(string archivePath)
    {
        using var archive = ZipFile.OpenRead(archivePath);
        var paths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long total = 0;
        var executableFound = false;
        foreach (var entry in archive.Entries)
        {
            var path = ValidateEntryPath(entry.FullName);
            if (!paths.Add(path.TrimEnd('/'))) throw new InvalidDataException("The archive contains duplicate paths.");
            if (((entry.ExternalAttributes >> 16) & 0xF000) == 0xA000
                || (entry.ExternalAttributes & (int)FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Links are not permitted in update archives.");
            total = checked(total + entry.Length);
            if (total > MaximumExpandedSize || archive.Entries.Count > 20000)
                throw new InvalidDataException("The expanded update is too large.");
            if (path == ExecutableName && entry.Length > 0) executableFound = true;
        }
        if (!executableFound) throw new InvalidDataException("The archive does not contain the application executable at its root.");
    }

    internal static string ValidateEntryPath(string path)
    {
        path = path.Replace('\\', '/');
        var segments = path.TrimEnd('/').Split('/');
        if (path.Length == 0 || path.StartsWith('/') || segments.Any(segment =>
            segment.Length == 0 || segment is "." or ".." || segment.EndsWith(' ') || segment.EndsWith('.')
            || segment.IndexOfAny([':', '*', '?', '"', '<', '>', '|']) >= 0 || segment.Any(char.IsControl)
            || IsDeviceName(segment)))
            throw new InvalidDataException("The archive contains an unsafe path.");
        return path;
    }

    private static bool IsDeviceName(string segment)
    {
        var name = segment.Split('.')[0].ToUpperInvariant();
        return name is "CON" or "PRN" or "AUX" or "NUL"
            || name.Length == 4 && (name.StartsWith("COM") || name.StartsWith("LPT")) && name[3] is >= '1' and <= '9';
    }

    internal static void ExtractArchive(string archivePath, string destination)
    {
        ValidateArchive(archivePath);
        EnsureNoLinks(destination);
        using var archive = ZipFile.OpenRead(archivePath);
        foreach (var entry in archive.Entries)
        {
            var relative = ValidateEntryPath(entry.FullName);
            var path = ChildPath(destination, relative);
            EnsureNoLinks(path);
            if (relative.EndsWith('/')) { Directory.CreateDirectory(path); continue; }
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            entry.ExtractToFile(path, false);
        }
    }

    /// <summary>Only changes shipped paths; projects, preferences and existing examples are never overwritten.</summary>
    internal static void ApplyFiles(string stage, string target, string backup, Action<int>? afterFile = null)
    {
        EnsureNoLinks(stage);
        EnsureNoLinks(target);
        EnsureNoLinks(backup);
        Directory.CreateDirectory(backup);
        var changes = new List<(string Destination, string? Backup)>();
        try
        {
            var files = Directory.GetFiles(stage, "*", SearchOption.AllDirectories);
            // Put the executable last; it should never be a half-written launch target.
            foreach (var source in files.OrderBy(path => Path.GetFileName(path) == ExecutableName ? 1 : 0))
            {
                EnsureNoLinks(source);
                var relative = ValidateEntryPath(Path.GetRelativePath(stage, source));
                var destination = ChildPath(target, relative);
                EnsureNoLinks(destination);
                if (!IsApplicationFile(relative) || File.Exists(destination) && IsPreservedContent(relative)) continue;
                if (Directory.Exists(destination)) throw new IOException("An update file conflicts with an existing directory.");
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                string? saved = null;
                if (File.Exists(destination))
                {
                    saved = ChildPath(backup, relative);
                    Directory.CreateDirectory(Path.GetDirectoryName(saved)!);
                    File.Copy(destination, saved, false);
                }
                // Record before replacement so a failure after replacement is always recoverable.
                changes.Add((destination, saved));
                ReplaceFile(source, destination);
                afterFile?.Invoke(changes.Count);
            }
        }
        catch (Exception installError)
        {
            var rollbackErrors = new List<Exception>();
            foreach (var change in changes.AsEnumerable().Reverse())
            {
                try
                {
                    if (change.Backup is not null) ReplaceFile(change.Backup, change.Destination);
                    else File.Delete(change.Destination);
                }
                catch (Exception error) { rollbackErrors.Add(error); }
            }
            if (rollbackErrors.Count > 0)
                throw new AggregateException($"The update failed and rollback is incomplete. Original files remain in {backup}.", [installError, .. rollbackErrors]);
            throw new IOException("The update failed; original application files were restored.", installError);
        }
    }

    private static bool IsPreservedContent(string relative) => relative.StartsWith("Example/", StringComparison.OrdinalIgnoreCase)
        || relative.StartsWith("Samples/", StringComparison.OrdinalIgnoreCase);

    private static bool IsApplicationFile(string relative)
    {
        if (relative.StartsWith("Example/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith("Samples/", StringComparison.OrdinalIgnoreCase)) return true;
        if (Path.GetExtension(relative).Equals(".aprj", StringComparison.OrdinalIgnoreCase)) return false;
        if (new[] { "Converters/", "BlenderPlugin/", "MayaPlugin/", "Docs/", "Licenses/" }.Any(prefix => relative.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return true;
        return !relative.Contains('/') && (relative == ExecutableName || Path.GetExtension(relative).Equals(".dll", StringComparison.OrdinalIgnoreCase)
            || relative is "README.md" or "README.zh-CN.md" or "CHANGELOG.md" or "LICENSE.txt" or "THIRD_PARTY_NOTICES.md");
    }

    private static void ReplaceFile(string source, string destination)
    {
        var temporary = destination + ".update-" + Guid.NewGuid().ToString("N");
        try { File.Copy(source, temporary, false); File.Move(temporary, destination, true); }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static string ChildPath(string root, string relative)
    {
        var prefix = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(prefix, relative));
        if (!path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Path escapes the expected directory.");
        return path;
    }

    private static void EnsureNoLinks(string path)
    {
        for (var current = Path.GetFullPath(path); current is not null; current = Path.GetDirectoryName(current))
            if ((File.Exists(current) || Directory.Exists(current)) && (File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Updates cannot traverse symbolic links or junctions.");
    }
}
