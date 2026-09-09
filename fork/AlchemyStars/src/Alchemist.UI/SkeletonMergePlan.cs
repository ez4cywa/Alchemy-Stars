using Cast.NET;
using Cast.NET.Nodes;
using RedFox.Graphics3D.Skeletal;
using System.IO;
using System.Numerics;
using static Alchemist.UI.CastNodeTraversal;

namespace Alchemist.UI;

/// <summary>One identity map for sampling, model bones, skin weights and CAST references.</summary>
internal sealed class SkeletonMergePlan
{
    internal sealed record Source(string Path, byte[] Snapshot, int ModelIndex, PartType Type, int[] BoneMap);
    private sealed record Identity(string OriginalName, SkeletonBone Bone, bool IsPartRoot, PartType Type);

    public Skeleton Skeleton { get; } = new("Alchemy Stars Merged Skeleton");
    public List<Source> Sources { get; } = [];
    public Dictionary<string, string> LeftWeaponNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, string> RightWeaponNames { get; } = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<Identity> identities = [];

    public static SkeletonMergePlan Build(IEnumerable<Part> parts, bool matchOldCallOfDuty, byte[]? weaponSnapshot = null)
    {
        var plan = new SkeletonMergePlan();
        foreach (var part in PartOrdering.ForSkeletonMerge(parts))
        {
            var path = Path.GetFullPath(part.FilePath);
            var snapshot = part.Type == PartType.Weapon && weaponSnapshot is not null ? weaponSnapshot : File.ReadAllBytes(path);
            using var stream = new MemoryStream(snapshot, writable: false);
            var models = CastReader.Load(stream).RootNodes.SelectMany(DescendantsAndSelf).OfType<ModelNode>().ToArray();
            if (models.Length == 0)
                throw new InvalidDataException($"Model part has no CAST model: {path}");
            for (var index = 0; index < models.Length; index++)
                plan.AddModel(part, path, snapshot, index, models[index], matchOldCallOfDuty);
        }
        if (plan.Skeleton.Bones.Count == 0)
            throw new InvalidDataException("No model part contained a usable skeleton.");
        if (plan.Skeleton.EnumerateRoots().Count() != 1)
            throw new InvalidDataException(LocalizationManager.Get("MergeNeedsParent"));
        plan.Skeleton.AssignBoneIndices();
        plan.Skeleton.GenerateGlobalTransforms();
        return plan;
    }

    public static SkeletonMergePlan BuildAttachedDual(Part hands, Part weapon, string leftMount, string rightMount, byte[]? leftSnapshot = null, byte[]? rightSnapshot = null)
    {
        var plan = new SkeletonMergePlan();
        void AddPart(Part part, string? side, Dictionary<string, string>? names)
        {
            var path = Path.GetFullPath(part.FilePath);
            var snapshot = (side == "left" ? leftSnapshot : side == "right" ? rightSnapshot : null) ?? File.ReadAllBytes(path);
            using var stream = new MemoryStream(snapshot, writable: false);
            var models = CastReader.Load(stream).RootNodes.SelectMany(DescendantsAndSelf).OfType<ModelNode>().ToArray();
            if (models.Length != 1) throw new InvalidDataException("Dual wield requires one model node per input file.");
            plan.AddModel(part, path, snapshot, 0, models[0], false, side, names);
        }
        AddPart(hands, null, null);
        AddPart(new Part { FilePath = weapon.FilePath, Type = PartType.Weapon, ParentBoneTag = leftMount }, "left", plan.LeftWeaponNames);
        AddPart(new Part { FilePath = weapon.FilePath, Type = PartType.Weapon, ParentBoneTag = rightMount }, "right", plan.RightWeaponNames);
        if (plan.Skeleton.EnumerateRoots().Count() != 1) throw new InvalidDataException("Dual wield requires one connected skeleton.");
        plan.Skeleton.AssignBoneIndices();
        plan.Skeleton.GenerateGlobalTransforms();
        return plan;
    }

    private void AddModel(Part part, string path, byte[] snapshot, int modelIndex, ModelNode model, bool legacy,
        string? instance = null, Dictionary<string, string>? instanceNames = null)
    {
        var bones = model.Skeleton?.Bones ?? [];
        if (bones.Length == 0)
            throw new InvalidDataException($"Model has no skeleton: {path}");
        var roots = Enumerable.Range(0, bones.Length).Where(i => bones[i].ParentIndex < 0).ToArray();
        if (roots.Length != 1)
            throw new InvalidDataException($"Model must have exactly one root: {path}");
        var rootIndex = roots[0];
        var parent = ResolveParent(part, bones[rootIndex]);
        var map = Enumerable.Repeat(-1, bones.Length).ToArray();
        var visiting = new HashSet<int>();
        int Add(int index)
        {
            if (index < 0 || index >= bones.Length)
                throw new InvalidDataException($"Invalid parent index in {path}");
            if (map[index] >= 0)
                return map[index];
            if (!visiting.Add(index))
                throw new InvalidDataException($"Cyclic skeleton in {path}");
            var source = bones[index];
            if (string.IsNullOrWhiteSpace(source.Name))
                throw new InvalidDataException($"Unnamed bone in {path}");
            var targetParent = source.ParentIndex < 0 ? parent : Skeleton.Bones[Add(source.ParentIndex)];
            var identityName = instance is null ? source.Name : source.Name + "__" + instance;
            var reset = part.Type == PartType.ViewHands && legacy;
            var position = reset ? Vector3.Zero : source.LocalPosition;
            var rotation = reset ? Quaternion.Identity : source.LocalRotation;
            ValidateTransform(position, rotation, source.Scale, path, source.Name);
            var matching = identities.Where(identity =>
                string.Equals(identity.OriginalName, identityName, StringComparison.OrdinalIgnoreCase)
                && ReferenceEquals(identity.Bone.Parent, targetParent)
                && Vector3.DistanceSquared(identity.Bone.BaseLocalTranslation, position) < 1e-10f
                && 1 - MathF.Abs(Quaternion.Dot(Quaternion.Normalize(identity.Bone.BaseLocalRotation), Quaternion.Normalize(rotation))) < 1e-6f
                && Vector3.DistanceSquared(identity.Bone.BaseScale, source.Scale) < 1e-10f).ToArray();
            if (matching.Length > 1)
                throw new InvalidDataException($"Ambiguous matching bone: {source.Name}");
            if (matching.Length == 1)
            {
                map[index] = matching[0].Bone.Index;
            }
            else
            {
                var name = identityName;
                if (Skeleton.Bones.Any(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    var stem = name + "__" + part.Type.ToString().ToLowerInvariant();
                    name = stem;
                    for (var suffix = 2; Skeleton.Bones.Any(b => string.Equals(b.Name, name, StringComparison.OrdinalIgnoreCase)); suffix++)
                        name = stem + "_" + suffix;
                    Logging.Logger.Info($"Separate same-name bone: {source.Name} -> {name}; parent {targetParent?.Name}; source {path}");
                }
                var bone = new SkeletonBone(name)
                {
                    Parent = targetParent,
                    BaseLocalTranslation = position,
                    BaseLocalRotation = Quaternion.Normalize(rotation),
                    BaseScale = source.Scale,
                    Scale = source.Scale,
                };
                Skeleton.AddBone(bone);
                identities.Add(new(identityName, bone, index == rootIndex, part.Type));
                map[index] = bone.Index;
            }
            if (instanceNames is not null) instanceNames[source.Name] = Skeleton.Bones[map[index]].Name!;
            visiting.Remove(index);
            return map[index];
        }
        for (var index = 0; index < bones.Length; index++)
            Add(index);
        Sources.Add(new(path, snapshot, modelIndex, part.Type, map));
    }

    private SkeletonBone? ResolveParent(Part part, BoneNode root)
    {
        if (!string.IsNullOrWhiteSpace(part.ParentBoneTag))
        {
            var named = Skeleton.Bones.Where(b => string.Equals(b.Name, part.ParentBoneTag, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (named.Length != 1)
                throw new InvalidDataException(string.Format(LocalizationManager.Get("MergeUnknownParent"), part.ParentBoneTag));
            return named[0];
        }
        if (part.Type == PartType.Weapon && identities.Any(i => i.Type == PartType.ViewHands))
        {
            var anchors = identities.Where(i => i.OriginalName.Equals("tag_weapon", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (anchors.Length != 1)
                throw new InvalidDataException(LocalizationManager.Get("MergeNeedsParent"));
            Logging.Logger.Info($"Resolved empty weapon parent to {anchors[0].Bone.Name}: {part.FilePath}");
            return anchors[0].Bone;
        }
        if (part.Type == PartType.Attachment && Skeleton.Bones.Count > 0)
        {
            var anchors = identities.Where(i => i.Type != PartType.ViewHands
                && i.OriginalName.Equals(root.Name, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (anchors.Length != 1)
                throw new InvalidDataException(LocalizationManager.Get("MergeNeedsParent"));
            var anchor = anchors[0].Bone;
            // Some parts author the attachment root in the matching weapon tag's parent
            // frame, carrying the tag's own bind transform so the bone can be reused verbatim.
            // Others author the root at the origin and expect to hang directly off the tag;
            // parenting those to the tag's parent places the part at the wrong origin.
            var reusableFrame = Vector3.DistanceSquared(anchor.BaseLocalTranslation, root.LocalPosition) < 1e-10f
                && 1 - MathF.Abs(Quaternion.Dot(Quaternion.Normalize(anchor.BaseLocalRotation), Quaternion.Normalize(root.LocalRotation))) < 1e-6f
                && Vector3.DistanceSquared(anchor.BaseScale, root.Scale) < 1e-10f;
            return reusableFrame ? anchor.Parent : anchor;
        }
        return null;
    }

    public void BindAnimation(SkeletonAnimation animation)
    {
        foreach (var target in animation.Targets)
        {
            var matches = identities.Where(i => i.OriginalName.Equals(target.BoneName, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (matches.Length == 1)
                target.BoneName = matches[0].Bone.Name!;
            else if (matches.Length > 1)
            {
                // Upstream isolates attached model roots from unqualified animation tracks.
                // j_gun tracks in the view animation belong to the wrist helper, not the
                // zero-local weapon root that inherits tag_weapon's motion.
                var nonRoots = matches.Where(i => !i.IsPartRoot).ToArray();
                if (nonRoots.Length != 1 || matches.Any(i => i != nonRoots[0] && i.Type == PartType.ViewHands))
                    throw new InvalidDataException(string.Format(LocalizationManager.Get("MergeAmbiguousAnimation"), target.BoneName));
                target.BoneName = nonRoots[0].Bone.Name!;
            }
        }
    }

    private static void ValidateTransform(Vector3 position, Quaternion rotation, Vector3 scale, string path, string name)
    {
        if (!float.IsFinite(position.LengthSquared()) || !float.IsFinite(rotation.LengthSquared())
            || rotation.LengthSquared() < 1e-12f || !float.IsFinite(scale.LengthSquared()))
            throw new InvalidDataException($"Invalid bind transform for {name}: {path}");
    }

}
