namespace AlchemyStars.Engine;

public sealed record AnimationBlendTemplateAnalysis(
    string BaseAnimationFile,
    bool EnableLeftHandIK,
    bool EnableRightHandIK,
    string LeftHandPoseFile,
    string RightHandPoseFile,
    string LeftIKTargetBoneName,
    string RightIKTargetBoneName,
    IReadOnlyList<string> LayerFiles,
    bool HasForegrip);

/// <summary>Reads the naming conventions used by the bundled blend examples and turns them into editable defaults.</summary>
public static class AnimationBlendTemplateAnalyzer
{
    public static AnimationBlendTemplateAnalysis Analyze(
        string sourceFile,
        IEnumerable<string>? siblingFiles = null,
        IEnumerable<string>? modelPartFiles = null)
        => AnalyzeCore(sourceFile, siblingFiles,
            new ScanContext((modelPartFiles ?? []).Select(path => new ModelPartSpec(path, ModelPartKind.Weapon))));

    public static AnimationBlendTemplateAnalysis AnalyzeWithModelParts(
        string sourceFile,
        IEnumerable<string>? siblingFiles,
        IEnumerable<ModelPartSpec>? modelParts)
        => AnalyzeCore(sourceFile, siblingFiles, new ScanContext(modelParts));

    public static IReadOnlyList<AnimationBlendTemplateAnalysis> AnalyzeBatch(IReadOnlyList<string> sources,
        IEnumerable<string>? modelPartFiles = null)
    {
        var context = new ScanContext((modelPartFiles ?? []).Select(path => new ModelPartSpec(path, ModelPartKind.Weapon)));
        return sources.Select(source => AnalyzeCore(source, sources, context)).ToArray();
    }

    public static IReadOnlyList<AnimationBlendTemplateAnalysis> AnalyzeBatchWithModelParts(IReadOnlyList<string> sources,
        IEnumerable<ModelPartSpec>? modelParts)
    {
        var context = new ScanContext(modelParts);
        return sources.Select(source => AnalyzeCore(source, sources, context)).ToArray();
    }

    private sealed class ScanContext
    {
        private readonly ModelPartSpec[] modelParts;
        internal readonly (string Left, string Right)[] Targets;
        internal readonly string[] LeftForegripTargets;
        private readonly Dictionary<string, string[]> directories = new(StringComparer.OrdinalIgnoreCase);

        internal ScanContext(IEnumerable<ModelPartSpec>? models)
        {
            modelParts = (models ?? []).Where(model => !string.IsNullOrWhiteSpace(model.FilePath)).ToArray();
            Targets = modelParts.Where(model => File.Exists(model.FilePath)).Select(model => FindForegripTargets(model.FilePath)).ToArray();
            LeftForegripTargets = modelParts.Where(model => model.Kind == ModelPartKind.Weapon && File.Exists(model.FilePath))
                .Select(model => FindExactLeftForegripTarget(model.FilePath)).Where(name => name.Length > 0).ToArray();
        }

        internal string[] Files(string directory)
        {
            if (!directories.TryGetValue(directory, out var files))
                directories[directory] = files = Directory.Exists(directory) ? Directory.GetFiles(directory) : [];
            return files;
        }
    }

    private static AnimationBlendTemplateAnalysis AnalyzeCore(string sourceFile, IEnumerable<string>? siblingFiles, ScanContext context)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sourceFile);
        var source = Path.GetFullPath(sourceFile);
        var directoryFiles = context.Files(Path.GetDirectoryName(source)!);
        var files = (siblingFiles ?? []).Concat(directoryFiles)
            .Select(Path.GetFullPath)
            .Where(WorkspacePaths.IsCastAnimationFile)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!files.Contains(source, StringComparer.OrdinalIgnoreCase)) files = [source, .. files];

        var stem = Path.GetFileNameWithoutExtension(source);
        var left = IsLeft(stem);
        var right = IsRight(stem);
        var hasForegrip = context.LeftForegripTargets.Length > 0
            || context.Targets.Any(target => target.Left.Length > 0 || target.Right.Length > 0)
            || ContainsAny(stem, "grip", "vertgrip", "foregrip");

        WorkspacePaths.RequireCastAnimation(source);
        var leftPose = FindPose(files, source, true, hasForegrip);
        var rightPose = FindPose(files, source, false, hasForegrip);
        var baseFile = source;
        IReadOnlyList<string> layers = [];
        var idle = FindIdle(files, source);
        if (TryGetAutomaticBlendKind(stem, out var blendKind) && idle is not null)
        {
            baseFile = idle;
            layers = blendKind is "walk" or "jog" or "sprint"
                ? [source, .. FindMatchingOffset(files, source, blendKind)]
                : [source];
        }
        var leftTarget = string.Empty;
        var rightTarget = string.Empty;
        if (hasForegrip)
        {
            foreach (var targets in context.Targets)
            {
                rightTarget = targets.Right;
                if (rightTarget.Length > 0) break;
            }
        }
        leftTarget = context.LeftForegripTargets.FirstOrDefault() ?? string.Empty;

        return new(
            baseFile,
            EnableLeftHandIK: !right,
            EnableRightHandIK: !left,
            leftPose,
            rightPose,
            leftTarget,
            rightTarget,
            layers,
            hasForegrip);
    }

    private static string FindPose(IReadOnlyList<string> files, string source, bool left, bool grip)
    {
        var sourceStem = Path.GetFileNameWithoutExtension(source);
        var family = PrefixBeforeMovement(sourceStem);
        var candidates = files.Where(path => SameDirectory(path, source) && !IsSameStem(path, sourceStem) && IsPose(Path.GetFileNameWithoutExtension(path)))
            .Where(path => PrefixBeforeMovement(Path.GetFileNameWithoutExtension(path)) is var prefix
                && (prefix.Length == 0 || prefix.Equals(family, StringComparison.OrdinalIgnoreCase))).ToArray();
        var scored = candidates
            .Select(path => (path, score: PoseScore(Path.GetFileNameWithoutExtension(path), left, grip)))
            .Where(item => item.score > 0)
            .OrderByDescending(item => item.score)
            .ToArray();
        return scored.Length == 0 || scored.Length > 1 && scored[0].score == scored[1].score ? string.Empty : scored[0].path;
    }

    private static int PoseScore(string stem, bool left, bool grip)
    {
        var lower = stem.ToLowerInvariant();
        var side = left
            ? ContainsAny(lower, "_pose_l", "pose_l", "_l_pose", "left")
            : ContainsAny(lower, "_pose_r", "pose_r", "_r_pose", "right");
        var opposite = left
            ? ContainsAny(lower, "_pose_r", "pose_r", "_r_pose", "right")
            : ContainsAny(lower, "_pose_l", "pose_l", "_l_pose", "left");
        if (opposite) return 0;
        // An unsuffixed pose is the left pose in MP5Base/MP5Grip.
        if (!side && !left) return 0;
        var score = side ? 100 : 45;
        if (grip && ContainsAny(lower, "grip", "vert", "foregrip", "ubgl")) score += 40;
        if (!grip && ContainsAny(lower, "grip", "vert", "foregrip", "ubgl")) score -= 20;
        return score;
    }

    private static string? FindIdle(IReadOnlyList<string> files, string source)
    {
        var sourceStem = Path.GetFileNameWithoutExtension(source);
        if (IsIdleBase(sourceStem)) return source;
        var prefix = PrefixBeforeMovement(sourceStem);
        var sourceName = ParseName(sourceStem);
        var candidates = files.Where(path => SameDirectory(path, source))
            .Where(path => IsIdleBase(Path.GetFileNameWithoutExtension(path)))
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (prefix.Length > 0)
        {
            var sameFamily = candidates.Where(path =>
                PrefixBeforeMovement(Path.GetFileNameWithoutExtension(path))
                    .Equals(prefix, StringComparison.OrdinalIgnoreCase)).ToArray();
            var exact = sameFamily.Where(path => ParseName(Path.GetFileNameWithoutExtension(path)).Variant == sourceName.Variant).ToArray();
            if (exact.Length == 1) return exact[0];
            if (exact.Length > 1) return null;
            var standard = sameFamily.Where(path => ParseName(Path.GetFileNameWithoutExtension(path)).Variant.Length == 0).ToArray();
            if (standard.Length == 1) return standard[0];
            if (sameFamily.Length > 0) return null;
        }

        // Some exports contain a shared idle_active while the idle file carries a weapon prefix.
        // Only use a keyword fallback when it is unambiguous (or the canonical bare "idle" exists).
        var canonical = candidates.FirstOrDefault(path =>
            Path.GetFileNameWithoutExtension(path).Equals("idle", StringComparison.OrdinalIgnoreCase));
        var compatible = candidates.Where(path => IsLeft(Path.GetFileNameWithoutExtension(path)) == IsLeft(sourceStem)
            && IsRight(Path.GetFileNameWithoutExtension(path)) == IsRight(sourceStem)).ToArray();
        if (sourceName.Action.Length > 0)
            return canonical ?? (compatible.Length == 1 ? compatible[0] : null);
        return null;
    }

    private static IReadOnlyList<string> FindMatchingOffset(IReadOnlyList<string> files, string source, string kind)
    {
        var sourceStem = Path.GetFileNameWithoutExtension(source);
        var prefix = PrefixBeforeMovement(sourceStem);
        if (prefix.Length == 0) return [];
        var variant = ParseName(sourceStem).Variant;
        var expected = $"{prefix}_{kind}_offset_additive" + (variant.Length == 0 ? "" : "_" + variant);
        return files.Where(path => !string.Equals(path, source, StringComparison.OrdinalIgnoreCase))
            .Where(path => SameDirectory(path, source))
            .Where(path => Path.GetFileNameWithoutExtension(path).Equals(expected, StringComparison.OrdinalIgnoreCase))
            .Take(1)
            .ToArray();
    }

    private static bool TryGetAutomaticBlendKind(string stem, out string kind)
    {
        kind = ParseName(stem).Action;
        if (kind is "walk_loop" or "jog_loop" or "sprint_loop") kind = kind[..^5];
        return kind is "walk" or "jog" or "sprint" or "idle_active" or "ads_up" or "ads_down"
            or "sprint_to_walk" or "sprint_to_jog" or "walk_to_sprint" or "walk_to_jog" or "jog_to_sprint" or "jog_to_walk";
    }

    private static bool SameFamily(string left, string right)
    {
        var a = PrefixBeforeMovement(Path.GetFileNameWithoutExtension(left));
        var b = PrefixBeforeMovement(Path.GetFileNameWithoutExtension(right));
        return a.Length == 0 || b.Length == 0 || a.Equals(b, StringComparison.OrdinalIgnoreCase);
    }

    private static string PrefixBeforeMovement(string stem)
    {
        return ParseName(stem).Family;
    }

    private static (string Family, string Action, string Variant) ParseName(string stem)
    {
        var tokens = stem.ToLowerInvariant().Split('_', StringSplitOptions.RemoveEmptyEntries);
        var index = Array.FindIndex(tokens, token => token is "idle" or "walk" or "jog" or "sprint" or "super" or "ads"
            or "reload" or "fire" or "raise" or "lower" or "melee" or "inspect" or "pose" or "hand");
        if (index < 0) return (string.Empty, string.Empty, string.Empty);
        var variantIndex = Array.IndexOf(tokens, "bp", index);
        var end = variantIndex < 0 ? tokens.Length : variantIndex;
        return (string.Join('_', tokens[..index]), string.Join('_', tokens[index..end]),
            variantIndex < 0 ? string.Empty : string.Join('_', tokens[variantIndex..]));
    }

    private static (string Left, string Right) FindForegripTargets(string path)
    {
        try
        {
            foreach (var model in Cast.NET.CastReader.Load(path).RootNodes.SelectMany(Walk).OfType<Cast.NET.Nodes.ModelNode>())
            {
                if (model.Skeleton is null) continue;
                var bones = model.Skeleton.Bones;
                var attachIndex = Array.FindIndex(bones, bone => bone.Name.Equals("tag_grip_attach", StringComparison.OrdinalIgnoreCase));
                if (attachIndex < 0) continue;
                var children = bones.Where(bone => bone.ParentIndex == attachIndex).Select(bone => bone.Name)
                    .Where(name => !string.IsNullOrWhiteSpace(name)).ToArray();
                return (
                    children.FirstOrDefault(name => name!.Contains("ik_loc_le", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("foregrip", StringComparison.OrdinalIgnoreCase) && !name.Contains("_ri", StringComparison.OrdinalIgnoreCase)) ?? string.Empty,
                    children.FirstOrDefault(name => name!.Contains("ik_loc_ri", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("foregrip", StringComparison.OrdinalIgnoreCase) && !name.Contains("_le", StringComparison.OrdinalIgnoreCase)) ?? string.Empty);
            }
            return (string.Empty, string.Empty);
        }
        catch { return (string.Empty, string.Empty); }
    }

    private static string FindExactLeftForegripTarget(string path)
    {
        try
        {
            foreach (var model in Cast.NET.CastReader.Load(path).RootNodes.SelectMany(Walk).OfType<Cast.NET.Nodes.ModelNode>())
            {
                var target = model.Skeleton?.Bones.FirstOrDefault(bone =>
                    bone.Name.Equals("tag_ik_loc_le_foregrip", StringComparison.OrdinalIgnoreCase));
                if (target is not null) return target.Name;
            }
        }
        catch { }
        return string.Empty;
    }

    private static bool ContainsForegripTag(string path) => FindForegripTargets(path) is { Left.Length: > 0 } or { Right.Length: > 0 };
    private static bool IsIdleBase(string stem) => ParseName(stem).Action == "idle";
    private static bool SameDirectory(string left, string right) =>
        string.Equals(Path.GetDirectoryName(left), Path.GetDirectoryName(right), StringComparison.OrdinalIgnoreCase);
    private static bool IsPose(string stem) => stem.Split('_').Any(token => token.Equals("pose", StringComparison.OrdinalIgnoreCase) || token.Equals("hand", StringComparison.OrdinalIgnoreCase));
    private static bool IsSameStem(string path, string stem) => Path.GetFileNameWithoutExtension(path).Equals(stem, StringComparison.OrdinalIgnoreCase);
    private static bool IsLeft(string stem) => HasToken(stem, "l", "left", "le") && !HasToken(stem, "r", "right", "ri");
    private static bool IsRight(string stem) => HasToken(stem, "r", "right", "ri") && !HasToken(stem, "l", "left", "le");
    private static bool HasToken(string stem, params string[] names) => stem.Split('_').Any(token => names.Contains(token, StringComparer.OrdinalIgnoreCase));
    private static bool ContainsAny(string value, params string[] fragments) =>
        fragments.Any(fragment => value.Contains(fragment, StringComparison.OrdinalIgnoreCase));
    private static IEnumerable<Cast.NET.CastNode> Walk(Cast.NET.CastNode node)
    {
        yield return node;
        foreach (var child in node.Children) foreach (var item in Walk(child)) yield return item;
    }
}
