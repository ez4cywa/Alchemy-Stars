using System.Diagnostics;
using System.Text.Json;
using Cast.NET;
using Cast.NET.Nodes;
using Alchemist.UI;
using static Alchemist.UI.CastNodeTraversal;

namespace AlchemyStars.Engine;

public sealed record RavenfieldAdaptationResult(string BlendPath, string FbxPath, string ReportPath, string PreviewPath, IReadOnlyList<string> Warnings)
{
    public bool? PalmFitPassed { get; init; }
}

/// <summary>Owns processed CAST generation, RF input protection and the isolated Blender run.</summary>
public sealed class RavenfieldAdaptationEngine
{
    public static IReadOnlyList<string> Validate(AnimationExportRequest request, int animationIndex,
        RavenfieldAdaptationOptions options, string? projectPath = null)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (options.Mode is not ("pose" or "animation" or "library"))
            throw new InvalidDataException("请选择有效的 RF 输出模式 / Select a valid RF output mode.");
        if (animationIndex < 0 || animationIndex >= request.Animations.Count)
            throw new InvalidDataException("请选择参考动画 / Select a reference animation.");
        if (!File.Exists(options.RfSourcePath) || Path.GetExtension(options.RfSourcePath).ToLowerInvariant() is not (".blend" or ".unitypackage"))
            throw new InvalidDataException("请选择 RF .blend 或 .unitypackage / Select an RF .blend or .unitypackage.");
        if (options.SourceUnit is not ("ft" or "m" or "cm") || options.UntaggedModelUpAxis is not ("hands" or "x" or "y" or "z") || options.IdleFrame < 0 ||
            !double.IsFinite(options.HandScale) || options.HandScale < 0.1 || options.HandScale > 10)
            throw new InvalidDataException("RF 单位、帧或比例无效 / Invalid RF unit, frame or scale.");
        foreach (var hand in new[] { options.Left, options.Right })
            if (hand is null || new[] { hand.PositionX, hand.PositionY, hand.PositionZ, hand.RotationX,
                hand.RotationY, hand.RotationZ, hand.ElbowSwivel, hand.FingerCurl }.Any(v => !double.IsFinite(v))
                || Math.Abs(hand.ElbowSwivel) > 180 || Math.Abs(hand.FingerCurl) > 90
                || new[] { hand.PositionX, hand.PositionY, hand.PositionZ, hand.RotationX, hand.RotationY, hand.RotationZ }.Any(v => Math.Abs(v) > 10000))
                throw new InvalidDataException("RF 微调超出范围：位置/旋转 ±10000，肘部 ±180°，手指 ±90° / RF adjustment out of range: position/rotation ±10000, elbow ±180°, fingers ±90°.");
        if (!request.Parts.Any(p => p.Kind == ModelPartKind.ViewHands) || !request.Parts.Any(p => p.Kind == ModelPartKind.Weapon))
            throw new InvalidDataException("需要 COD 手臂与武器模型 / COD hands and weapon model parts are required.");
        var job = request.Animations[animationIndex];
        if (options.Mode == "library") BuildClipNames(request.Animations.Select(a => a.OutputName));
        var name = request.Options.OutputPrefix + job.OutputName + request.Options.OutputSuffix
            + (options.Mode switch { "animation" => "_rf_anim", "library" => "_rf_library", _ => "_rf_idle" });
        if (string.IsNullOrWhiteSpace(job.OutputName) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || Path.GetFileName(name) != name)
            throw new InvalidDataException("RF 输出名无效 / Invalid RF output name.");
        var stem = Path.GetFullPath(Path.Combine(WorkspacePaths.ResolveAnimationOutputFolder(job.SourceFile, job.OutputFolder), name));
        var outputs = new[] { ".blend", ".fbx", ".report.json", ".preview.png" }.Select(ext => stem + ext).ToArray();
        var inputs = request.Parts.Select(p => p.FilePath)
            .Concat(request.Animations.SelectMany(a => new[] { a.SourceFile, a.LeftHandPoseFile, a.RightHandPoseFile }
                .Concat((a.Layers ?? []).Select(l => l.FilePath))))
            .Append(options.RfSourcePath).Append(projectPath ?? "")
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var output in outputs)
        {
            if (inputs.Contains(output)) throw new InvalidDataException("RF 输出会覆盖输入 / RF output would overwrite an input: " + output);
            if (Directory.Exists(output)) throw new IOException("RF 输出路径是文件夹 / RF output path is a directory: " + output);
        }
        return outputs;
    }

    public RavenfieldAdaptationResult Adapt(AnimationExportRequest request, int animationIndex,
        RavenfieldAdaptationOptions options, string? projectPath = null)
    {
        var outputs = Validate(request, animationIndex, options, projectPath);
        var blender = DesktopFbxExporter.FindBlender() ?? throw new FileNotFoundException(
            "未找到 Blender，请设置 ALCHEMY_STARS_BLENDER / Blender not found; set ALCHEMY_STARS_BLENDER.");
        var script = FindScript();
        foreach (var output in outputs)
            if (File.Exists(output))
            {
                using var probe = new FileStream(output, FileMode.Open, FileAccess.Write, FileShare.None);
            }
        var temporary = Path.Combine(Path.GetTempPath(), "AlchemyStars-RF-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporary);
        try
        {
            var (axisRequest, axisAssumptions) = PrepareModelAxes(request, options.UntaggedModelUpAxis, temporary);
            var plan = SkeletonMergePlan.Build(axisRequest.Parts.Select(AnimationExportEngine.ToCompatibilityPart), request.Options.MatchOldCallOfDuty, outputAxis: "z");
            var handIndices = new List<int>();
            var meshIndex = 0;
            foreach (var source in plan.Sources)
            {
                using var stream = new MemoryStream(source.Snapshot, writable: false);
                var model = CastReader.Load(stream).RootNodes.SelectMany(DescendantsAndSelf).OfType<ModelNode>().ElementAt(source.ModelIndex);
                foreach (var mesh in model.Meshes)
                {
                    if (source.Type == PartType.ViewHands) handIndices.Add(meshIndex);
                    meshIndex++;
                }
            }
            if (handIndices.Count == 0) throw new InvalidDataException("COD 手臂没有网格 / COD hands contain no mesh.");
            var weaponNames = plan.Sources.Where(s => s.Type != PartType.ViewHands).SelectMany(s => s.BoneMap)
                .Distinct().Select(i => plan.Skeleton.Bones[i].Name!).ToArray();
            var indices = options.Mode == "library" ? Enumerable.Range(0, request.Animations.Count).ToArray() : [animationIndex];
            var clipNames = BuildClipNames(indices.Select(i => request.Animations[i].OutputName));
            var clips = new List<(string Name, string Path)>();
            foreach (var index in indices)
            {
                var job = request.Animations[index] with { OutputFolder = Path.Combine(temporary, "clip-" + index), OutputName = "processed", Framerate = 30 };
                var prepared = axisRequest with { Animations = [job], Options = request.Options with {
                    Format = ExportFormat.Cast, OutputPrefix = "", OutputSuffix = "", OutputUpAxis = "z",
                    CastAnimationOnly = false, BakeRelevantBonesOnly = false } };
                clips.Add((clipNames[clips.Count], new AnimationExportEngine().Export(prepared).OutputFiles.Single()));
            }
            var referenceClip = options.Mode == "library" ? animationIndex : 0;
            var cast = clips[referenceClip].Path;
            var config = Path.Combine(temporary, "config.json");
            using (var stream = File.Create(config))
            using (var json = new Utf8JsonWriter(stream))
            {
                json.WriteStartObject();
                json.WriteString("mode", options.Mode);
                json.WriteBoolean("contactFit", options.ContactFit);
                json.WriteString("clipName", request.Animations[animationIndex].OutputName);
                json.WriteNumber("referenceClip", referenceClip);
                json.WriteStartArray("clips");
                foreach (var clip in clips)
                {
                    json.WriteStartObject();
                    json.WriteString("name", clip.Name); json.WriteString("path", Path.GetFullPath(clip.Path));
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteString("sourceUnit", options.SourceUnit);
                json.WriteStartArray("sourceAxisAssumptions");
                foreach (var assumption in axisAssumptions)
                {
                    json.WriteStartObject();
                    json.WriteString("path", assumption.Path); json.WriteString("axis", assumption.Axis);
                    json.WriteString("reason", options.UntaggedModelUpAxis);
                    json.WriteEndObject();
                }
                json.WriteEndArray();
                json.WriteNumber("idleFrame", options.IdleFrame);
                json.WriteNumber("handScale", options.HandScale);
                WriteHand(json, "left", options.Left); WriteHand(json, "right", options.Right);
                json.WriteStartArray("handMeshIndices"); foreach (var i in handIndices) json.WriteNumberValue(i); json.WriteEndArray();
                json.WriteStartArray("weaponBoneNames"); foreach (var n in weaponNames) json.WriteStringValue(n); json.WriteEndArray();
                json.WriteStartArray("sourceDirectories");
                foreach (var directory in request.Parts.Select(p => Path.GetDirectoryName(Path.GetFullPath(p.FilePath))!)
                    .Distinct(StringComparer.OrdinalIgnoreCase)) json.WriteStringValue(directory);
                json.WriteEndArray();
                json.WriteEndObject();
            }
            var stagedStem = Path.Combine(temporary, "result");
            var start = new ProcessStartInfo(blender) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var arg in new[] { "--background", "--factory-startup", "--disable-autoexec", "--python-exit-code", "1", "--python", script,
                "--", "--input", cast, "--rf", Path.GetFullPath(options.RfSourcePath), "--output", stagedStem, "--config", config }) start.ArgumentList.Add(arg);
            using var process = Process.Start(start) ?? throw new IOException("Could not start Blender.");
            var stdout = process.StandardOutput.ReadToEndAsync(); var stderr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(600_000)) { process.Kill(entireProcessTree: true); process.WaitForExit(); throw new TimeoutException("RF Blender adaptation timed out."); }
            var log = stdout.GetAwaiter().GetResult() + "\n" + stderr.GetAwaiter().GetResult();
            var staged = new[] { ".blend", ".fbx", ".report.json", ".preview.png" }.Select(ext => stagedStem + ext).ToArray();
            if (process.ExitCode != 0 || staged.Any(p => !File.Exists(p) || new FileInfo(p).Length == 0))
                throw new IOException("RF 适配失败 / RF adaptation failed: " + log[^Math.Min(log.Length, 8000)..]);
            using var report = JsonDocument.Parse(File.ReadAllText(staged[2]));
            var warnings = report.RootElement.TryGetProperty("warnings", out var values) && values.ValueKind == JsonValueKind.Array
                ? values.EnumerateArray().Select(v => v.ToString()).ToArray() : [];
            var publishWarnings = PublishOutputs(staged, outputs);
            return new(outputs[0], outputs[1], outputs[2], outputs[3], warnings.Concat(publishWarnings).ToArray())
            {
                PalmFitPassed = ReadPalmFitPassed(report.RootElement)
            };
        }
        finally { Directory.Delete(temporary, recursive: true); }
    }

    internal static bool? ReadPalmFitPassed(JsonElement report)
        => report.TryGetProperty("palmContact", out var contact) && contact.ValueKind == JsonValueKind.Object
            && contact.TryGetProperty("passed", out var passed) && passed.ValueKind is JsonValueKind.True or JsonValueKind.False
            ? passed.GetBoolean() : null;

    // The internal move seam makes a mid-publication failure reproducible without Blender.
    // Backups live beside the final files, outside Adapt's disposable processing directory.
    internal static IReadOnlyList<string> PublishOutputs(IReadOnlyList<string> staged, IReadOnlyList<string> outputs,
        Action<string, string, bool>? move = null)
    {
        if (staged.Count != 4 || outputs.Count != 4) throw new ArgumentException("Four RF outputs are required.");
        move ??= File.Move;
        var destination = Path.GetDirectoryName(Path.GetFullPath(outputs[0]))!;
        Directory.CreateDirectory(destination);
        var recovery = Path.Combine(destination, ".AlchemyStars-RF-publish-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(recovery);
        var existed = outputs.Select(File.Exists).ToArray();
        var committed = 0;
        try
        {
            // Copy every new file and backup every old file before touching any destination.
            for (var i = 0; i < outputs.Count; i++)
            {
                File.Copy(staged[i], Path.Combine(recovery, "new-" + i));
                if (existed[i]) File.Copy(outputs[i], Path.Combine(recovery, "old-" + i));
            }
            for (var i = 0; i < outputs.Count; i++)
            {
                move(Path.Combine(recovery, "new-" + i), outputs[i], true);
                committed++;
            }
        }
        catch (Exception failure)
        {
            var rollbackFailures = new List<Exception>();
            for (var i = committed - 1; i >= 0; i--)
                try
                {
                    if (existed[i]) File.Copy(Path.Combine(recovery, "old-" + i), outputs[i], overwrite: true);
                    else File.Delete(outputs[i]);
                }
                catch (Exception error) { rollbackFailures.Add(error); }
            if (rollbackFailures.Count > 0)
                throw new AggregateException("RF 发布失败，部分文件无法恢复；原结果备份保留于 / RF publication and rollback failed; original backups retained at: " + recovery,
                    new[] { failure }.Concat(rollbackFailures));
            try { Directory.Delete(recovery, recursive: true); }
            catch (Exception cleanup) { throw new AggregateException("RF 发布失败，原结果已恢复；临时目录保留于 / RF publication failed; originals restored; temporary directory retained at: " + recovery, failure, cleanup); }
            throw;
        }
        try { Directory.Delete(recovery, recursive: true); }
        catch { return ["RF 结果已保存，但发布临时目录未能清理 / RF results saved, but temporary publication directory remains: " + recovery]; }
        return [];
    }

    internal sealed record SourceAxisAssumption(string Path, string Axis);

    internal static IReadOnlyList<string> BuildClipNames(IEnumerable<string> outputNames)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var names = new List<string>();
        foreach (var outputName in outputNames)
        {
            if (string.IsNullOrWhiteSpace(outputName)) throw new InvalidDataException("动画输出名不能为空 / Animation output name cannot be empty.");
            var name = string.Concat(outputName.Trim().Select(c => char.IsControl(c) || "/\\:*?\"<>|".Contains(c) ? '_' : c));
            var unique = name;
            var suffix = names.Count + 1;
            while (!used.Add(unique)) unique = name + "_" + suffix++;
            names.Add(unique);
        }
        return names;
    }

    internal static (AnimationExportRequest Request, IReadOnlyList<SourceAxisAssumption> Assumptions)
        PrepareModelAxes(AnimationExportRequest request, string fallback, string temporary)
    {
        if (fallback is not ("hands" or "x" or "y" or "z")) throw new InvalidDataException("Invalid untagged model axis.");
        var axis = fallback == "hands"
            ? CastCoordinateSystem.ReadAxis(CastReader.Load(request.Parts.First(p => p.Kind == ModelPartKind.ViewHands).FilePath))
            : fallback;
        var parts = request.Parts.ToArray();
        var assumptions = new List<SourceAxisAssumption>();
        for (var i = 0; i < parts.Length; i++)
        {
            if (parts[i].Kind == ModelPartKind.ViewHands) continue;
            var cast = CastReader.Load(parts[i].FilePath);
            var metadata = cast.RootNodes.SelectMany(r => r.Children).OfType<MetadataNode>().FirstOrDefault();
            if (metadata?.UpAxis?.Trim().ToLowerInvariant() is "x" or "y" or "z") continue;
            // RF-only interpretation of untagged attached models. Never change the
            // ordinary exporter's Y-up default or the user's source model on disk.
            var node = (CastNode?)metadata ?? new CastNode(CastNodeIdentifier.Metadata) { Parent = cast.RootNodes[0] };
            node.AddString("up", axis);
            var copy = Path.Combine(temporary, $"axis-part-{i}.cast");
            CastWriter.Save(copy, cast);
            assumptions.Add(new(Path.GetFullPath(parts[i].FilePath), axis));
            parts[i] = parts[i] with { FilePath = copy };
        }
        return (request with { Parts = parts }, assumptions);
    }

    private static void WriteHand(Utf8JsonWriter json, string name, RavenfieldHandAdjustment hand)
    {
        json.WriteStartObject(name);
        json.WriteStartArray("position"); foreach (var v in new[] { hand.PositionX, hand.PositionY, hand.PositionZ }) json.WriteNumberValue(v); json.WriteEndArray();
        json.WriteStartArray("rotation"); foreach (var v in new[] { hand.RotationX, hand.RotationY, hand.RotationZ }) json.WriteNumberValue(v); json.WriteEndArray();
        json.WriteNumber("elbowSwivel", hand.ElbowSwivel); json.WriteNumber("fingerCurl", hand.FingerCurl);
        json.WriteEndObject();
    }

    private static string FindScript()
    {
        foreach (var seed in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory })
            for (var dir = new DirectoryInfo(seed); dir is not null; dir = dir.Parent)
                foreach (var relative in new[] { "Converters/ravenfield_adapter.py", "blender/ravenfield_adapter.py" })
                {
                    var path = Path.Combine(dir.FullName, relative);
                    if (File.Exists(path)) return path;
                }
        throw new FileNotFoundException("RF Blender adapter script is missing.");
    }
}
