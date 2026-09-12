using Cast.NET;
using Cast.NET.Nodes;
using RedFox.Graphics3D.Skeletal;
using RedFox.Graphics3D.Translation;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using static Alchemist.UI.CastNodeTraversal;

namespace Alchemist.UI;

internal static class MayaCastPackage
{
    private const ulong HashBase = 0x534E495752545250;

    public static void Save(
        string outputPath,
        SkeletonMergePlan plan,
        SkeletonAnimation animation,
        Graphics3DTranslatorFactory translatorFactory)
        => SavePackage(outputPath, plan, animation, translatorFactory);

    public static void SaveModel(string outputPath, SkeletonMergePlan plan, Graphics3DTranslatorFactory translatorFactory)
        => SavePackage(outputPath, plan, null, translatorFactory);

    private static void SavePackage(string outputPath, SkeletonMergePlan plan, SkeletonAnimation? animation,
        Graphics3DTranslatorFactory translatorFactory)
    {
        var fullOutputPath = Path.GetFullPath(outputPath);
        var directory = Path.GetDirectoryName(fullOutputPath)
            ?? throw new InvalidOperationException("Output path has no directory.");
        Directory.CreateDirectory(directory);

        if (animation is not null && !ReferenceEquals(animation.Skeleton, plan.Skeleton))
            throw new InvalidDataException("Animation and model must use the same skeleton merge plan.");
        if (plan.Sources.Any(source => string.Equals(source.Path, fullOutputPath, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Output CAST cannot overwrite one of its source model files.");

        var animationPath = Path.Combine(directory, $".{Guid.NewGuid():N}.animation.cast");
        var packagePath = Path.Combine(directory, $".{Guid.NewGuid():N}.package.cast");
        try
        {
            if (animation is not null) translatorFactory.Save(animationPath, animation);
            var nextHash = HashBase;
            var sourceModels = new List<(ModelNode Model, SkeletonMergePlan.Source Source)>();
            foreach (var source in plan.Sources)
            {
                using var stream = new MemoryStream(source.Snapshot, writable: false);
                var modelCast = CastReader.Load(stream);
                CastCoordinateSystem.NormalizeModels(modelCast, plan.UpAxis);
                FreshenHashes(modelCast, ref nextHash);
                var model = modelCast.RootNodes.SelectMany(DescendantsAndSelf).OfType<ModelNode>().ElementAt(source.ModelIndex);
                sourceModels.Add((model, source));
            }
            if (sourceModels.Count == 0)
                throw new InvalidDataException("No source model nodes were found for the Maya CAST package.");

            var mergedModel = MergeModels(plan, sourceModels, ref nextHash);

            var packageRoot = new CastNode(CastNodeIdentifier.Root);
            packageRoot.Hash = nextHash++;
            var metadata = new CastNode(CastNodeIdentifier.Metadata) { Hash = nextHash++, Parent = packageRoot };
            metadata.AddString("up", plan.UpAxis);
            mergedModel.Parent = packageRoot;
            if (animation is not null)
            {
                var animationCast = CastReader.Load(animationPath);
                FreshenHashes(animationCast, ref nextHash);
                foreach (var root in animationCast.RootNodes)
                {
                    foreach (var animationNode in DescendantsAndSelf(root).OfType<AnimationNode>().ToArray())
                        animationNode.Parent = packageRoot;
                }
            }

            var package = new Cast.NET.Cast([packageRoot]);
            Validate(package, animation is null ? 0 : 1);
            CastWriter.Save(packagePath, package);
            Validate(CastReader.Load(packagePath), animation is null ? 0 : 1);
            File.Move(packagePath, fullOutputPath, overwrite: true);
        }
        finally
        {
            DeleteIfPresent(animationPath);
            DeleteIfPresent(packagePath);
        }
    }

    private static ModelNode MergeModels(
        SkeletonMergePlan plan,
        IReadOnlyList<(ModelNode Model, SkeletonMergePlan.Source Source)> sources,
        ref ulong nextHash)
    {
        var skeleton = new SkeletonNode { Hash = nextHash++ };
        var mergedBones = new BoneNode?[plan.Skeleton.Bones.Count];
        var hashMap = new Dictionary<ulong, ulong>();
        var mergedModel = sources[0].Model;

        // Collect canonical bone nodes before touching any source parents or indices.
        foreach (var (model, source) in sources)
        {
            var bones = model.Skeleton!.Bones;
            for (var index = 0; index < bones.Length; index++)
            {
                var destinationIndex = source.BoneMap[index];
                mergedBones[destinationIndex] ??= bones[index];
                hashMap[bones[index].Hash] = mergedBones[destinationIndex]!.Hash;
            }
        }

        foreach (var (model, source) in sources)
        {
            var sourceSkeleton = model.Skeleton!;
            var bones = sourceSkeleton.Bones;
            var rootIndex = Array.FindIndex(bones, bone => bone.ParentIndex < 0);
            var sourceRoot = bones[rootIndex];
            var destinationRoot = plan.Skeleton.Bones[source.BoneMap[rootIndex]];
            var rotation = Quaternion.Normalize(destinationRoot.BaseWorldRotation * Quaternion.Inverse(sourceRoot.WorldRotation));
            // R * (vertex - sourceRoot) + destinationRoot, including rotated source origins.
            var translation = destinationRoot.BaseWorldTranslation - Vector3.Transform(sourceRoot.WorldPosition, rotation);
            TransformMeshes(model.Meshes, rotation, translation);
            RemapMeshWeights(model.Meshes, source.BoneMap);
            RemapHashReferences(model, hashMap);
            foreach (var auxiliary in sourceSkeleton.Children.Where(child => child.Identifier != CastNodeIdentifier.Bone).ToArray())
                auxiliary.Parent = skeleton;
            sourceSkeleton.Parent = null;
            if (!ReferenceEquals(model, mergedModel))
                foreach (var child in model.Children.ToArray())
                    child.Parent = mergedModel;
        }

        for (var index = 0; index < mergedBones.Length; index++)
        {
            var node = mergedBones[index] ?? throw new InvalidDataException("Missing canonical CAST bone.");
            var bone = plan.Skeleton.Bones[index];
            node.AddString("n", bone.Name!);
            node.AddValue("p", bone.Parent is null ? uint.MaxValue : (uint)bone.Parent.Index);
            node.AddValue("lp", bone.BaseLocalTranslation);
            node.AddValue("lr", new Vector4(bone.BaseLocalRotation.X, bone.BaseLocalRotation.Y, bone.BaseLocalRotation.Z, bone.BaseLocalRotation.W));
            node.AddValue("wp", bone.BaseWorldTranslation);
            node.AddValue("wr", new Vector4(bone.BaseWorldRotation.X, bone.BaseWorldRotation.Y, bone.BaseWorldRotation.Z, bone.BaseWorldRotation.W));
            node.AddValue("s", bone.BaseScale);
            node.Parent = skeleton;
        }
        skeleton.Parent = mergedModel;
        // The pinned Cast.NET ModelNode reader expects the skeleton to be first.
        mergedModel.Children.Remove(skeleton);
        mergedModel.Children.Insert(0, skeleton);
        return mergedModel;
    }

    private static void RemapHashReferences(CastNode node, IReadOnlyDictionary<ulong, ulong> remap)
    {
        foreach (var property in DescendantsAndSelf(node)
            .SelectMany(static descendant => descendant.Properties.Values)
            .OfType<CastArrayProperty<ulong>>())
        {
            for (var i = 0; i < property.Values.Count; i++)
            {
                if (remap.TryGetValue(property.Values[i], out var replacement))
                    property.Values[i] = replacement;
            }
        }
    }

    private static void RemapMeshWeights(IEnumerable<MeshNode> meshes, IReadOnlyList<int> sourceToDestination)
    {
        foreach (var mesh in meshes)
        {
            if (mesh.VertexWeightBoneBuffer is null)
                continue;

            // DQS is the Alchemy Stars default for merged first-person assets.
            // Writing it into the CAST keeps Maya imports and downstream FBX
            // conversion deterministic instead of depending on importer defaults.
            mesh.AddString("sm", "quaternion");

            var mapped = mesh.VertexWeightBoneBuffer switch
            {
                CastArrayProperty<byte> values => values.Values.Select(value => (uint)sourceToDestination[value]).ToArray(),
                CastArrayProperty<ushort> values => values.Values.Select(value => (uint)sourceToDestination[value]).ToArray(),
                CastArrayProperty<uint> values => values.Values.Select(value => (uint)sourceToDestination[checked((int)value)]).ToArray(),
                _ => throw new InvalidDataException("Unsupported CAST mesh bone-index buffer type."),
            };
            var maximum = mapped.Length == 0 ? 0 : mapped.Max();
            mesh.Properties["wb"] = maximum switch
            {
                <= byte.MaxValue => new CastArrayProperty<byte>(mapped.Select(static value => (byte)value)),
                <= ushort.MaxValue => new CastArrayProperty<ushort>(mapped.Select(static value => (ushort)value)),
                _ => new CastArrayProperty<uint>(mapped),
            };
        }
    }

    private static void TransformMeshes(IEnumerable<MeshNode> meshes, Quaternion rotation, Vector3 translation)
    {
        foreach (var mesh in meshes)
        {
            for (var i = 0; i < mesh.VertexPositionBuffer.Values.Count; i++)
                mesh.VertexPositionBuffer.Values[i] = Vector3.Transform(mesh.VertexPositionBuffer.Values[i], rotation) + translation;
            TransformDirections(mesh.VertexNormalBuffer, rotation);
            TransformDirections(mesh.VertexTangentBuffer, rotation);
        }
    }

    private static void TransformDirections(CastArrayProperty<Vector3>? property, Quaternion rotation)
    {
        if (property is null)
            return;
        for (var i = 0; i < property.Values.Count; i++)
        {
            var transformed = Vector3.Transform(property.Values[i], rotation);
            property.Values[i] = transformed == Vector3.Zero ? Vector3.Zero : Vector3.Normalize(transformed);
        }
    }

    private static void FreshenHashes(Cast.NET.Cast cast, ref ulong nextHash)
    {
        foreach (var root in cast.RootNodes)
        {
            var nodes = DescendantsAndSelf(root).ToArray();
            var remap = new Dictionary<ulong, ulong>();
            foreach (var node in nodes)
            {
                var replacement = nextHash++;
                if (node.Hash != 0)
                {
                    remap.TryAdd(node.Hash, replacement);
                }
                node.Hash = replacement;
            }

            foreach (var property in nodes
                .SelectMany(static x => x.Properties.Values)
                .OfType<CastArrayProperty<ulong>>())
            {
                for (var i = 0; i < property.Values.Count; i++)
                {
                    if (remap.TryGetValue(property.Values[i], out var replacement))
                    {
                        property.Values[i] = replacement;
                    }
                }
            }
        }
    }

    private static void Validate(Cast.NET.Cast cast, int expectedAnimationCount)
    {
        var nodes = cast.RootNodes.SelectMany(DescendantsAndSelf).ToArray();
        var animationCount = nodes.Count(static x => x.Identifier == CastNodeIdentifier.Animation);
        var modelCount = nodes.Count(static x => x.Identifier == CastNodeIdentifier.Model);
        if (animationCount != expectedAnimationCount)
        {
            throw new InvalidDataException($"CAST package must contain {expectedAnimationCount} animation(s); found {animationCount}.");
        }
        if (modelCount != 1)
        {
            throw new InvalidDataException($"Maya CAST package must contain exactly one merged model; found {modelCount}.");
        }
        var model = nodes.OfType<ModelNode>().Single();
        var skeleton = model.Skeleton ?? throw new InvalidDataException("Merged Maya CAST model has no skeleton.");
        var bones = skeleton.Bones;
        if (bones.Count(static bone => bone.ParentIndex < 0) != 1)
            throw new InvalidDataException("Merged Maya CAST model must contain exactly one skeleton root.");
        var duplicateBoneNames = bones
            .GroupBy(static bone => bone.Name, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToArray();
        if (duplicateBoneNames.Length != 0)
            throw new InvalidDataException("Merged Maya CAST model contains duplicate bones: " + string.Join(", ", duplicateBoneNames));
        if (nodes.Select(static x => x.Hash).Distinct().Count() != nodes.Length)
        {
            throw new InvalidDataException("Maya CAST package contains duplicate node hashes.");
        }
    }

    private static void DeleteIfPresent(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
