using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace AlchemyStars.Avalonia;

/// <summary>Native CAST operations. All callers share the two execution slots and output claims.</summary>
public sealed class ModelMergerService
{
    private static readonly SemaphoreSlim Slots = new(2, 2);
    private static readonly HashSet<string> Claims = new(StringComparer.OrdinalIgnoreCase);
    public static string FindExecutable()
    {
        var packaged = Path.Combine(AppContext.BaseDirectory, "Converters", "alchemy-model-merger.exe");
        if (File.Exists(packaged)) return packaged;
        foreach (var start in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var candidate = Path.Combine(directory.FullName, "third_party", "modelmerger", "rust", "target", "release", "alchemy-model-merger.exe");
                if (File.Exists(candidate)) return candidate;
            }
        throw new FileNotFoundException("ModelMerger native engine is missing. Restore the complete application package.", packaged);
    }

    public static string Json(Action<Utf8JsonWriter> write)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream)) { writer.WriteStartObject(); write(writer); writer.WriteEndObject(); }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static void Strings(Utf8JsonWriter writer, string name, IEnumerable<string> values)
    {
        writer.WriteStartArray(name);
        foreach (var value in values) writer.WriteStringValue(value);
        writer.WriteEndArray();
    }

    public async Task<JsonElement> RunAsync(string request, Action<JsonElement>? progress,
        Func<string, CancellationToken, Task<bool>>? confirmOverwrite, CancellationToken cancellationToken, string? claimedOutput = null)
    {
        await Slots.WaitAsync(cancellationToken);
        string? claim = null;
        try
        {
            if (claimedOutput is not null) claim = Claim(claimedOutput);
            using var process = new Process { StartInfo = new ProcessStartInfo(FindExecutable())
            {
                UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true,
                RedirectStandardOutput = true, RedirectStandardError = true,
                StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
            }};
            process.Start();
            var errors = process.StandardError.ReadToEndAsync();
            var inputLock = new SemaphoreSlim(1, 1);
            async Task Send(string line)
            {
                await inputLock.WaitAsync();
                try { await process.StandardInput.WriteLineAsync(line); await process.StandardInput.FlushAsync(); }
                finally { inputLock.Release(); }
            }
            await Send(request);
            Task cancelTask = Task.CompletedTask;
            using var cancel = cancellationToken.Register(() => cancelTask = CancelAsync());
            async Task CancelAsync()
            {
                try
                {
                    await Send("{\"command\":\"cancel\"}");
                }
                catch (InvalidOperationException) { }
                catch (IOException) { }
            }
            JsonElement? result = null;
            try
            {
                while (await process.StandardOutput.ReadLineAsync() is { } line)
                {
                    using var document = JsonDocument.Parse(line);
                    var data = document.RootElement;
                    var kind = data.GetProperty("event").GetString();
                    if (kind == "prepared")
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var output = data.GetProperty("output_path").GetString()!;
                        claim = Claim(output);
                        var overwrite = File.Exists(output);
                        if (overwrite && (confirmOverwrite is null || !await confirmOverwrite(output, cancellationToken)))
                            throw new OperationCanceledException("Overwrite declined.");
                        cancellationToken.ThrowIfCancellationRequested();
                        await Send(Json(w => { w.WriteString("command", "execute"); w.WriteBoolean("overwrite", overwrite); }));
                    }
                    else if (kind == "error")
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        throw new InvalidOperationException(data.GetProperty("message").GetString());
                    }
                    else if (kind is "completed" or "analysis") result = data.Clone();
                    else progress?.Invoke(data.Clone());
                }
                await process.WaitForExitAsync();
                if (result is null) cancellationToken.ThrowIfCancellationRequested();
                var errorText = await errors;
                if (process.ExitCode != 0 || result is null)
                    throw new InvalidOperationException($"Native engine exited ({process.ExitCode}): {errorText}");
                return result.Value;
            }
            finally
            {
                if (!process.HasExited)
                {
                    await CancelAsync();
                    process.StandardInput.Close();
                    await process.StandardOutput.ReadToEndAsync();
                    await process.WaitForExitAsync();
                }
                await cancel.DisposeAsync();
                await cancelTask;
            }
        }
        finally
        {
            if (claim is not null) lock (Claims) Claims.Remove(claim);
            Slots.Release();
        }
    }

    private static string Claim(string path)
    {
        var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        lock (Claims)
            if (!Claims.Add(full)) throw new InvalidOperationException($"Another task already owns this output: {full}");
        return full;
    }
}
