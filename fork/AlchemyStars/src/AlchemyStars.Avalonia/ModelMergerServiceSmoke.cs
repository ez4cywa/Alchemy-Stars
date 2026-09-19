using System.Security.Cryptography;
using System.Text.Json;

namespace AlchemyStars.Avalonia;

internal static class ModelMergerServiceSmoke
{
    public static async Task RunAsync(string outputDirectory)
    {
        var fixture = FindFixtures();
        var parts = new[] { Path.Combine(fixture, "part-00.cast"), Path.Combine(fixture, "part-01.cast") };
        var service = new ModelMergerService();
        Directory.CreateDirectory(outputDirectory);
        var sourceHashes = parts.Select(p => SHA256.HashData(File.ReadAllBytes(p))).ToArray();
        string Request(string name, bool manual = false) => ModelMergerService.Json(w =>
        {
            w.WriteString("command", "merge"); ModelMergerService.Strings(w, "input_files", parts);
            w.WriteString("output_directory", outputDirectory); w.WriteString("output_file_name", name);
            w.WriteString("manual_root_file", manual ? parts[0] : null); w.WriteBoolean("overwrite", true);
        });
        var auto = await service.RunAsync(Request("automatic.cast"), null, null, CancellationToken.None);
        Require(File.Exists(auto.GetProperty("output_path").GetString()), "Automatic merge did not publish output.");
        Require(auto.GetProperty("part_count").GetInt32() == 2, "Incorrect merged part count.");
        var manual = await service.RunAsync(Request("manual.cast", true), null, null, CancellationToken.None);
        Require(File.Exists(manual.GetProperty("output_path").GetString()), "Manual merge did not publish output.");
        var originalOutput = File.ReadAllBytes(Path.Combine(outputDirectory, "automatic.cast"));
        var confirms = 0;
        try
        {
            await service.RunAsync(Request("automatic.cast"), null, (_, _) => { confirms++; return Task.FromResult(false); }, CancellationToken.None);
            throw new InvalidOperationException("Declined overwrite was executed.");
        }
        catch (OperationCanceledException) { }
        Require(confirms == 1 && originalOutput.SequenceEqual(File.ReadAllBytes(Path.Combine(outputDirectory, "automatic.cast"))), "Declining overwrite changed output.");
        await service.RunAsync(Request("automatic.cast"), null, (_, _) => Task.FromResult(true), CancellationToken.None);

        // Keep a resolved output claimed while the UI confirmation is pending.
        var prepared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = service.RunAsync(Request("automatic.cast"), null, (_, _) => { prepared.TrySetResult(); return release.Task; }, CancellationToken.None);
        await prepared.Task.WaitAsync(TimeSpan.FromSeconds(30));
        try
        {
            await service.RunAsync(Request("automatic.cast"), null, (_, _) => Task.FromResult(true), CancellationToken.None);
            throw new InvalidOperationException("Concurrent output claim was not rejected.");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("owns this output", StringComparison.Ordinal)) { }
        finally { release.TrySetResult(true); }
        await first;

        // Both active slots are held at confirmation, making queued cancellation deterministic.
        var heldCount = 0;
        var bothHeld = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var releaseBoth = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        Task<bool> Hold(string _, CancellationToken __) { if (Interlocked.Increment(ref heldCount) == 2) bothHeld.TrySetResult(); return releaseBoth.Task; }
        var held1 = service.RunAsync(Request("automatic.cast"), null, Hold, CancellationToken.None);
        var held2 = service.RunAsync(Request("manual.cast"), null, Hold, CancellationToken.None);
        await bothHeld.Task.WaitAsync(TimeSpan.FromSeconds(30));
        using var queuedCancel = new CancellationTokenSource();
        var queued = service.RunAsync(Request("cancelled-queued.cast"), null, null, queuedCancel.Token);
        queuedCancel.Cancel();
        try { await queued; throw new InvalidOperationException("Queued cancellation did not cancel."); }
        catch (OperationCanceledException) { }
        finally { releaseBoth.TrySetResult(true); }
        await Task.WhenAll(held1, held2);
        Require(!File.Exists(Path.Combine(outputDirectory, "cancelled-queued.cast")), "Queued cancellation created output.");

        using var runningCancel = new CancellationTokenSource();
        try
        {
            await service.RunAsync(Request("cancelled-running.cast"), _ => runningCancel.Cancel(), null, runningCancel.Token);
            throw new InvalidOperationException("Running cancellation did not cancel.");
        }
        catch (OperationCanceledException) { }
        Require(!File.Exists(Path.Combine(outputDirectory, "cancelled-running.cast")), "Cancellation during preparation created output.");
        Require(!Directory.EnumerateFiles(outputDirectory).Any(p => p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)), "A cancelled merge left temporary output.");
        for (var i = 0; i < parts.Length; i++) Require(sourceHashes[i].SequenceEqual(SHA256.HashData(File.ReadAllBytes(parts[i]))), "Source CAST was changed.");
        Console.WriteLine("MODEL_MERGER_SERVICE_SMOKE_OK: automatic/manual merge, overwrite approval/decline, claims, 2 slots, queued/running cancellation, unchanged inputs.");
    }

    internal static string FindFixtures()
    {
        foreach (var start in new[] { Environment.CurrentDirectory, AppContext.BaseDirectory })
            for (var directory = new DirectoryInfo(start); directory is not null; directory = directory.Parent)
            {
                var path = Path.Combine(directory.FullName, "third_party", "modelmerger", "tests", "fixtures", "rust-migration", "golden-small");
                if (File.Exists(Path.Combine(path, "part-00.cast"))) return path;
            }
        throw new DirectoryNotFoundException("ModelMerger golden fixtures are unavailable.");
    }
    internal static void Require(bool value, string message) { if (!value) throw new InvalidOperationException(message); }
}
