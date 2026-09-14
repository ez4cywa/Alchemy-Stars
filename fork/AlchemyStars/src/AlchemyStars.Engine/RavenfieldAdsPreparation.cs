using System.Numerics;
using Alchemist.UI;
using RedFox.Graphics3D;
using RedFox.Graphics3D.Skeletal;

namespace AlchemyStars.Engine;

/// <summary>RF-only completion of controller-only ADS clips; ordinary exports are untouched.</summary>
internal static class RavenfieldAdsPreparation
{
    internal static bool IsControllerOnly(string path, string axis)
    {
        var clip = AnimationConverter.LoadAtStandardFramerate(path, axis);
        var animated = clip.Targets.Where(t => t.TranslationFrameCount > 0 || t.RotationFrameCount > 0 || t.ScaleFrameCount > 0).ToArray();
        return animated.Length > 0 && animated.All(t => string.Equals(t.BoneName, "tag_ads", StringComparison.OrdinalIgnoreCase));
    }

    internal static AnimationExportJob Prepare(AnimationExportRequest request, AnimationExportJob job,
        int referenceIndex, int referenceFrame, SkeletonMergePlan plan, string temporary)
    {
        // An explicitly authored layer stack owns its base/offset semantics.
        if (job.Layers?.Count > 0 || !IsControllerOnly(job.SourceFile, plan.UpAxis)) return job;
        var reference = request.Animations[referenceIndex];
        if (IsControllerOnly(reference.SourceFile, plan.UpAxis))
            throw new InvalidDataException("ADS 仅包含控制骨骼，请选择完整 Idle 动画作为 RF 参考 / Select a full Idle pose as the RF reference for controller-only ADS.");
        var ads = plan.Skeleton.Bones.SingleOrDefault(b => string.Equals(b.Name, "tag_ads", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException("ADS 控制骨骼不存在 / The model has no tag_ads controller.");
        bool BelowAds(string name)
        {
            var bone = plan.Skeleton.Bones.SingleOrDefault(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase));
            for (; bone is not null; bone = bone.Parent) if (ReferenceEquals(bone, ads)) return true;
            return false;
        }
        if (!BelowAds("tag_weapon") || !BelowAds("j_wrist_le") || !BelowAds("j_wrist_ri"))
            throw new InvalidDataException("ADS 必须驱动双手与武器 / tag_ads must be an ancestor of both wrists and the weapon.");
        var baked = AnimationConverter.Bake(plan, AnimationExportEngine.ToCompatibilityAnimation(reference with { Framerate = 30 }),
            AnimationExportEngine.ToIkSettings(request.Options.LeftHandIk, reference.LeftIkTargetOverride),
            AnimationExportEngine.ToIkSettings(request.Options.RightHandIk, reference.RightIkTargetOverride), false, false, reference.WeaponFollowMode);
        if (referenceFrame < 0 || referenceFrame >= baked.GetAnimationFrameCount())
            throw new InvalidDataException("ADS 参考帧超出范围 / ADS reference frame is outside the reference clip.");
        var pose = new SkeletonAnimation("RF_ADS_reference", plan.Skeleton) { Framerate = 30, TransformType = TransformType.Absolute };
        for (var i = 0; i < pose.Targets.Count; i++)
        {
            pose.Targets[i].AddTranslationFrame(0, baked.Targets[i].SampleTranslation(referenceFrame));
            pose.Targets[i].AddRotationFrame(0, Quaternion.Normalize(baked.Targets[i].SampleRotation(referenceFrame)));
        }
        var path = Path.Combine(temporary, "ads-reference-" + Guid.NewGuid().ToString("N") + ".cast");
        AnimationConverter.SaveBaked(plan, pose, path, ".cast", true);
        return job with { SourceFile = path, Layers = [new AnimationLayerSpec(job.SourceFile, AnimationLayerKind.Additive)] };
    }
}
