using System.Numerics;
using Cast.NET;
using Cast.NET.Nodes;
using Alchemist.UI;
using RedFox.Graphics3D;
using RedFox.Graphics3D.Skeletal;
using RedFox.Graphics3D.Translation;

namespace AlchemyStars.Engine;

public sealed record DualWieldResult(string OutputFile, int FrameCount, int BoneCount, IReadOnlyList<string> UnmappedTargets, string? ModelFile = null)
{
    public IReadOnlyList<string> OutputFiles => ModelFile is null ? [OutputFile] : [OutputFile, ModelFile];
}

/// <summary>Attached dual wield: process source tasks, compose two weapon instances, then solve mounts.</summary>
public sealed class DualWieldEngine
{
    public static IReadOnlyList<string> GetOutputFiles(WorkspaceDocument document, WorkspaceDualAnimation task, bool preview = false)
    {
        var left = document.Animations.SingleOrDefault(a => a.Id == task.LeftAnimationId)
            ?? throw new InvalidDataException("左侧动画任务不存在 / Left animation task is missing.");
        var outputFolder = WorkspacePaths.ResolveAnimationOutputFolder(left.Name, task.OutputFolder);
        var stem = (preview ? "" : document.OutputPrefix) + task.Name + (preview ? "" : document.OutputSuffix);
        if (string.IsNullOrWhiteSpace(task.Name) || stem.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || stem is "." or "..")
            throw new InvalidDataException("双持输出名称无效 / Invalid dual output name.");
        var format = preview ? ".cast" : OutputFormats.Normalize(document.OutputFormat);
        var output = Path.GetFullPath(Path.Combine(outputFolder, stem + format));
        return !preview && task.ExportWeaponModels
            ? [output, Path.GetFullPath(Path.Combine(outputFolder, stem + "_model.cast"))] : [output];
    }

    public DualWieldResult Export(WorkspaceDocument document, WorkspaceDualAnimation task, bool preview = false)
    {
        var left = document.Animations.SingleOrDefault(a => a.Id == task.LeftAnimationId)
            ?? throw new InvalidDataException("左侧动画任务不存在 / Left animation task is missing.");
        var right = document.Animations.SingleOrDefault(a => a.Id == task.RightAnimationId)
            ?? throw new InvalidDataException("右侧动画任务不存在 / Right animation task is missing.");
        if (left.Id == right.Id) throw new InvalidDataException("请选择两个独立任务 / Select two different source tasks.");
        if (document.Parts.Count != 2 || document.Parts.Count(p => p.Type == ModelPartKind.ViewHands) != 1
            || document.Parts.Count(p => p.Type == ModelPartKind.Weapon) != 1)
            throw new InvalidDataException("挂点模式需要一个手臂模型和一个武器模型 / Attached mode requires one hands model and one weapon model.");
        if (document.MatchOldCallOfDuty)
            throw new InvalidDataException("双持任务请关闭旧版 COD 兼容 / Disable legacy COD transforms for dual wield.");
        var format = preview ? ".cast" : OutputFormats.Normalize(document.OutputFormat);
        var outputs = GetOutputFiles(document, task, preview);
        foreach (var path in new[] { left, right }.SelectMany(a =>
            new[] { a.Name, a.LeftHandPoseFile, a.RightHandPoseFile }.Concat(a.Layers.Select(l => l.Name)))
            .Where(path => !string.IsNullOrWhiteSpace(path))) WorkspacePaths.RequireCastAnimation(path);
        var output = outputs[0];
        var modelOutput = outputs.Count > 1 ? outputs[1] : null;
        var inputs = document.Parts.Select(p => p.FilePath).Concat(document.Animations.SelectMany(a =>
            new[] { a.Name, a.LeftHandPoseFile, a.RightHandPoseFile }.Concat(a.Layers.Select(l => l.Name))))
            .Where(p => !string.IsNullOrWhiteSpace(p)).Select(Path.GetFullPath).ToArray();
        if (outputs.Any(path => inputs.Contains(path, StringComparer.OrdinalIgnoreCase)))
            throw new InvalidDataException("输出不能覆盖任何源素材 / Output would overwrite an input asset.");
        foreach (var path in document.Parts.Select(p => p.FilePath).Concat(new[] { left, right }.SelectMany(a =>
            new[] { a.Name, a.LeftHandPoseFile, a.RightHandPoseFile }.Concat(a.Layers.Select(l => l.Name)))).Where(p => !string.IsNullOrWhiteSpace(p)))
            if (!File.Exists(path)) throw new FileNotFoundException("输入素材不存在 / Input asset missing", path);

        // The inherited CAST translator ignores scale channels. Inspect them before
        // conversion so unsupported input cannot silently produce a wrong bake.
        foreach (var path in new[] { left, right }.SelectMany(a => new[] { a.Name, a.LeftHandPoseFile, a.RightHandPoseFile }
            .Concat(a.Layers.Select(l => l.Name))).Where(p => Path.GetExtension(p).Equals(".cast", StringComparison.OrdinalIgnoreCase)).Distinct())
            if (CastReader.Load(path).RootNodes.SelectMany(Walk).OfType<CurveNode>()
                .Any(c => c.KeyPropertyName is "sx" or "sy" or "sz"))
                throw new InvalidDataException("双持求解暂不支持缩放曲线 / Scale curves are not supported by dual solving.");

        var hands = document.Parts.Single(p => p.Type == ModelPartKind.ViewHands);
        var weapon = document.Parts.Single(p => p.Type == ModelPartKind.Weapon);
        if (!Enum.IsDefined(task.Mode)) throw new InvalidDataException("未知双持模式 / Unknown dual model mode.");
        CombinedWeaponSplitter.Result? split = null;
        if (task.Mode == DualModelMode.CombinedWeapons)
        {
            HashSet<string> Targets(WorkspaceAnimation selected, string marker)
            {
                var file = Path.GetFileName(selected.Name);
                var markerIndex = file.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
                var prefix = markerIndex >= 0 ? file[..(markerIndex + marker.Length)] : null;
                var paths = document.Animations.Where(a => a.Id == selected.Id || prefix is not null && Path.GetFileName(a.Name).StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    .Select(a => a.Name).Where(p => Path.GetExtension(p).Equals(".cast", StringComparison.OrdinalIgnoreCase)).Distinct();
                return paths.SelectMany(p => CastReader.Load(p).RootNodes.SelectMany(Walk).OfType<CurveNode>().Select(c => c.NodeName))
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
            }
            var automatic = string.IsNullOrEmpty(task.LeftWeaponBranch) || string.IsNullOrEmpty(task.RightWeaponBranch);
            split = CombinedWeaponSplitter.Split(weapon.FilePath, task.LeftWeaponBranch, task.RightWeaponBranch,
                automatic ? Targets(left, "_l_") : new(StringComparer.OrdinalIgnoreCase),
                automatic ? Targets(right, "_r_") : new(StringComparer.OrdinalIgnoreCase));
        }
        var h = new Part { FilePath = hands.FilePath, Type = PartType.ViewHands };
        var w = new Part { FilePath = weapon.FilePath, Type = PartType.Weapon, ParentBoneTag = task.SourceMount };
        var leftPlan = SkeletonMergePlan.Build([h, w], false, split?.Left, document.OutputUpAxis);
        var rightPlan = SkeletonMergePlan.Build([h, w], false, split?.Right, document.OutputUpAxis);
        var finalPlan = SkeletonMergePlan.BuildAttachedDual(h, w, task.LeftMount, task.RightMount, split?.Left, split?.Right, document.OutputUpAxis);
        if (task.LeftMount.Equals(task.RightMount, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("左右挂点必须不同 / Left and right mounts must differ.");
        foreach (var bone in finalPlan.Skeleton.Bones)
            if (Vector3.DistanceSquared(bone.BaseScale, Vector3.One) > 1e-8f)
                throw new InvalidDataException("当前双持求解要求单位绑定缩放 / Dual solving currently requires unit bind scale.");
        var request = new WorkspaceProjectStore().CreateExportRequest(document);
        var unknown = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        SkeletonAnimation Bake(WorkspaceAnimation source, SkeletonMergePlan plan)
        {
            var job = request.Animations[document.Animations.IndexOf(source)];
            // All source clips are resampled into the shared 30 FPS timeline before binding.
            foreach (var path in new[] { job.SourceFile }.Concat((job.Layers ?? []).Select(l => l.FilePath)))
            {
                var clip = AnimationConverter.LoadAtStandardFramerate(path, plan.UpAxis, plan.NormalizeInputAxes);
                plan.BindAnimation(clip);
                var known = plan.Skeleton.Bones.Select(b => b.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                foreach (var target in clip.Targets.Where(t => !known.Contains(t.BoneName))) unknown.Add(target.BoneName);
                if (clip.Targets.Any(t => t.ScaleFrameCount > 0 && t.ScaleFrames!.Any(f => Vector3.DistanceSquared(f.Value, Vector3.One) > 1e-8f)))
                    throw new InvalidDataException("双持求解暂不支持缩放动画 / Animated scale is not supported by dual solving.");
            }
            return AnimationConverter.Bake(plan, AnimationExportEngine.ToCompatibilityAnimation(job),
                AnimationExportEngine.ToIkSettings(request.Options.LeftHandIk, job.LeftIkTargetOverride),
                AnimationExportEngine.ToIkSettings(request.Options.RightHandIk, job.RightIkTargetOverride), false, false, job.WeaponFollowMode, task.SourceMount);
        }
        var leftClip = Bake(left, leftPlan);
        var rightClip = Bake(right, rightPlan);
        var count = (int)leftClip.GetAnimationFrameCount();
        if (count <= 0 || count != (int)rightClip.GetAnimationFrameCount())
            throw new InvalidDataException(IsChinese
                ? $"左右处理结果帧数不同或为空。\n左侧：{Path.GetFileName(left.Name)} — {count} 帧\n右侧：{Path.GetFileName(right.Name)} — {(int)rightClip.GetAnimationFrameCount()} 帧\n\n请检查左右源动作长度，以及叠加层的起始偏移和长度，使处理后的总帧数一致。"
                : $"Processed source durations differ or are empty.\nLeft: {Path.GetFileName(left.Name)} — {count} frames\nRight: {Path.GetFileName(right.Name)} — {(int)rightClip.GetAnimationFrameCount()} frames\n\nCheck source durations and layer offsets/durations so both processed tasks have the same frame count.");
        var skeleton = finalPlan.Skeleton;
        SkeletonBone Find(Skeleton s, string name) => s.Bones.SingleOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("挂点不存在 / Mount is missing: " + name);
        var leftMount = Find(skeleton, task.LeftMount);
        var rightMount = Find(skeleton, task.RightMount);
        var sourceLeftMount = Find(leftPlan.Skeleton, task.SourceMount);
        var sourceRightMount = Find(rightPlan.Skeleton, task.SourceMount);
        if (leftMount.Parent is null || rightMount.Parent is null) throw new InvalidDataException("挂点必须具有父骨骼 / Mounts require parent bones.");
        for (var p = leftMount.Parent; p is not null; p = p.Parent)
            if (ReferenceEquals(p, rightMount)) throw new InvalidDataException("挂点不能互为祖先 / Mounts cannot be ancestors of each other.");
        for (var p = rightMount.Parent; p is not null; p = p.Parent)
            if (ReferenceEquals(p, leftMount)) throw new InvalidDataException("挂点不能互为祖先 / Mounts cannot be ancestors of each other.");
        var baked = new SkeletonAnimation(task.Name, skeleton) { Framerate = WorkspacePaths.StandardAnimationFramerate, TransformType = TransformType.Absolute };
        for (var frame = 0; frame < count; frame++)
        {
            Sample(leftClip, frame); Sample(rightClip, frame);
            skeleton.InitializeAnimationTransforms();
            foreach (var index in finalPlan.Sources[0].BoneMap)
            {
                var bone = skeleton.Bones[index];
                var sourcePlan = IsLeft(bone.Name!) ? leftPlan : rightPlan;
                var sourceBone = Find(sourcePlan.Skeleton, bone.Name!);
                bone.LocalTranslation = sourceBone.LocalTranslation; bone.LocalRotation = sourceBone.LocalRotation;
            }
            CopyWeapon(leftPlan, 1); CopyWeapon(rightPlan, 2);
            skeleton.Update();
            SolveMount(leftMount, sourceLeftMount); SolveMount(rightMount, sourceRightMount);
            skeleton.Update();
            for (var i = 0; i < skeleton.Bones.Count; i++)
            {
                baked.Targets[i].AddTranslationFrame(frame, skeleton.Bones[i].LocalTranslation);
                baked.Targets[i].AddRotationFrame(frame, Quaternion.Normalize(skeleton.Bones[i].LocalRotation));
            }
        }
        // Preserve side-specific events without making simultaneous names ambiguous.
        foreach (var (clip, side) in new[] { (leftClip, "left/"), (rightClip, "right/") })
            if (clip.Actions is not null)
                foreach (var action in clip.Actions) baked.CreateAction(side + action.Name, action.KeyFrames);
        AnimationConverter.SaveBaked(finalPlan, baked, output, format, !preview && document.CastAnimationOnly);
        if (modelOutput is not null) MayaCastPackage.SaveModel(modelOutput, finalPlan, AnimationConverter.TranslatorFactory);
        return new(output, count, skeleton.Bones.Count, unknown.Order().ToArray(), modelOutput);

        void CopyWeapon(SkeletonMergePlan source, int destinationSource)
        {
            var from = source.Sources[1].BoneMap; var to = finalPlan.Sources[destinationSource].BoneMap;
            var indices = to.ToHashSet();
            for (var i = 0; i < to.Length; i++)
            {
                var destination = skeleton.Bones[to[i]];
                // Instance root follows its mount with its original bind-local grip offset.
                if (destination.Parent is null || !indices.Contains(destination.Parent.Index)) continue;
                destination.LocalTranslation = source.Skeleton.Bones[from[i]].LocalTranslation;
                destination.LocalRotation = source.Skeleton.Bones[from[i]].LocalRotation;
            }
        }
    }

    internal static bool IsLeft(string name) => name.Contains("_le", StringComparison.OrdinalIgnoreCase)
        || name.EndsWith("_left", StringComparison.OrdinalIgnoreCase);

    private static bool IsChinese => System.Globalization.CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);

    private static IEnumerable<CastNode> Walk(CastNode node)
    {
        yield return node;
        foreach (var child in node.Children) foreach (var descendant in Walk(child)) yield return descendant;
    }

    private static void Sample(SkeletonAnimation clip, int frame)
    {
        var skeleton = clip.Skeleton!;
        skeleton.InitializeAnimationTransforms();
        for (var i = 0; i < skeleton.Bones.Count; i++)
        {
            skeleton.Bones[i].LocalTranslation = clip.Targets[i].SampleTranslation(frame);
            skeleton.Bones[i].LocalRotation = Quaternion.Normalize(clip.Targets[i].SampleRotation(frame));
        }
        skeleton.Update();
    }

    private static void SolveMount(SkeletonBone target, SkeletonBone desired)
    {
        var inverse = Quaternion.Inverse(target.Parent!.WorldRotation);
        target.LocalTranslation = Vector3.Transform(desired.WorldTranslation - target.Parent.WorldTranslation, inverse);
        target.LocalRotation = Quaternion.Normalize(inverse * desired.WorldRotation);
    }
}
