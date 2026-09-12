using System.Numerics;
using Cast.NET;
using Cast.NET.Nodes;
using RedFox.Graphics3D;
using RedFox.Graphics3D.Cast;
using RedFox.Graphics3D.Skeletal;

namespace AlchemyStars.Avalonia;

// A private, read-only copy of the exported scene. Sampling never touches export inputs.
internal sealed class CastPreviewScene
{
    internal sealed record Surface(Mesh Mesh, Skeleton? Skeleton, Matrix4x4[] InverseBind, Vector3[] BindNormals);
    internal readonly record struct ShadedSurface(Vector3[] Positions, Vector3[] Normals);
    private readonly List<SkeletonAnimationSampler> samplers = [];
    internal List<Surface> Surfaces { get; } = [];
    internal Skeleton[] Skeletons { get; private init; } = [];
    public int FrameCount { get; private init; }
    public float Framerate { get; private init; }
    public int VertexCount => Surfaces.Sum(surface => surface.Mesh.Positions?.Count ?? 0);
    public int BoneCount => Skeletons.Sum(skeleton => skeleton.Bones.Count);
    public Vector3 Center { get; private set; }
    public float Radius { get; private set; }
    public Vector3 AllCenter { get; private set; }
    public float AllRadius { get; private set; }
    public bool UsesProjectSkeleton { get; private init; }
    public string UpAxis { get; private init; } = "z";

    internal Vector3 FromZUp(Vector3 value) => FromZUp(value, UpAxis);

    internal static Vector3 FromZUp(Vector3 value, string upAxis) => upAxis switch
    {
        "y" => new(value.X, value.Z, -value.Y),
        "x" => new(value.Z, value.Y, -value.X),
        _ => value,
    };

    internal static Vector3 ToZUp(Vector3 value, string upAxis) => upAxis switch
    {
        "y" => new(value.X, -value.Z, value.Y),
        "x" => new(-value.Z, value.Y, value.X),
        _ => value,
    };

    internal static string? ResolvePreviewUpAxis(string? outputUpAxis, string? animationPath)
    {
        var selected = NormalizeUpAxis(outputUpAxis);
        if (selected is not null) return selected;
        if (string.IsNullOrWhiteSpace(animationPath) || !File.Exists(animationPath)) return null;
        try
        {
            using var stream = File.OpenRead(animationPath);
            var source = CastReader.Load(stream);
            return NormalizeUpAxis(source.RootNodes.SelectMany(root => root.Children)
                .OfType<MetadataNode>().FirstOrDefault()?.UpAxis);
        }
        catch (IOException) { return null; }
        catch (UnauthorizedAccessException) { return null; }
        catch (InvalidDataException) { return null; }
    }

    private static string? NormalizeUpAxis(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "x" => "x",
        "y" => "y",
        "z" => "z",
        _ => null,
    };

    public static CastPreviewScene Load(string path, IReadOnlyList<ModelPartSpec>? parts = null, bool legacy = false, string? upAxisOverride = null)
    {
        if (!string.Equals(Path.GetExtension(path), ".cast", StringComparison.OrdinalIgnoreCase))
            throw new NotSupportedException("The preview reads CAST files only.");
        var scene = new Graphics3DScene();
        using var stream = File.OpenRead(path);
        var cast = CastReader.Load(stream);
        stream.Position = 0;
        new CastTranslator().Read(stream, path, scene);
        var animationNodes = cast.RootNodes.SelectMany(root => root.EnumerateChildrenOfType<AnimationNode>()).ToArray();
        var animations = scene.EnumerateObjectsOfType<SkeletonAnimation>().ToArray();
        if (animations.Length > 1) throw new NotSupportedException("Preview requires one merged animation per CAST.");
        var animation = animations.FirstOrDefault();
        var usesProjectSkeleton = false;
        if (animation is not null)
        {
            // The legacy translator does not transfer animation-node FPS or its embedded
            // skeleton. Fill those preview-only fields without changing conversion behavior.
            animation.Framerate = animationNodes[0].Framerate;
            if (!float.IsFinite(animation.Framerate) || animation.Framerate <= 0)
                throw new InvalidDataException("Invalid animation framerate.");
            if (!scene.EnumerateObjectsOfType<Skeleton>().Any())
            {
                if (animationNodes[0].Children.OfType<SkeletonNode>().FirstOrDefault() is { } node)
                    scene.Objects.Add(ReadSkeleton(node));
                else if (parts is { Count: > 0 })
                {
                    var axis = cast.RootNodes.SelectMany(root => root.Children).OfType<MetadataNode>().FirstOrDefault()?.UpAxis;
                    var skeleton = AnimationExportEngine.CreatePreviewSkeleton(parts, legacy, axis is "x" or "z" ? axis : "y");
                    if (animation.Targets.Any(target => !skeleton.ContainsBone(target.BoneName)))
                        throw new InvalidDataException("The project skeleton does not contain all animation targets.");
                    scene.Objects.Add(skeleton);
                    usesProjectSkeleton = true;
                }
            }
        }
        var skeletons = scene.EnumerateObjectsOfType<Skeleton>().Distinct().ToArray();
        var preview = new CastPreviewScene
        {
            Skeletons = skeletons,
            UsesProjectSkeleton = usesProjectSkeleton,
            UpAxis = NormalizeUpAxis(upAxisOverride)
                ?? NormalizeUpAxis(cast.RootNodes.SelectMany(root => root.Children).OfType<MetadataNode>().FirstOrDefault()?.UpAxis)
                ?? "y",
            FrameCount = Math.Max(1, (int)(animation?.GetAnimationFrameCount() ?? 1)),
            Framerate = animation is { Framerate: > 0 } ? animation.Framerate : 30,
        };
        foreach (var skeleton in skeletons)
        {
            skeleton.InitializeAnimationTransforms();
            if (animation is not null) preview.samplers.Add(new SkeletonAnimationSampler("Preview", animation, skeleton));
        }
        foreach (var model in scene.EnumerateObjectsOfType<Model>())
        {
            var inverseBind = model.Skeleton?.Bones.Select(bone =>
            {
                var matrix = Matrix4x4.CreateFromQuaternion(bone.BaseWorldRotation) * Matrix4x4.CreateTranslation(bone.BaseWorldTranslation);
                if (!Matrix4x4.Invert(matrix, out var inverse)) throw new InvalidDataException("Invalid bone bind transform.");
                return inverse;
            }).ToArray() ?? [];
            foreach (var mesh in model.Meshes)
            {
                if (mesh.Positions is null || mesh.Positions.Count == 0) continue;
                if (mesh.Positions.Any(p => !IsFinite(p))) throw new InvalidDataException("Non-finite mesh coordinates.");
                foreach (var (a, b, c) in mesh.Faces)
                    if ((uint)a >= mesh.Positions.Count || (uint)b >= mesh.Positions.Count || (uint)c >= mesh.Positions.Count)
                        throw new InvalidDataException("Invalid triangle index.");
                if (mesh.Influences is { Count: > 0 } weights)
                {
                    if (weights.ElementCount != mesh.Positions.Count) throw new InvalidDataException("Invalid skin weight count.");
                    foreach (var (bone, weight) in weights)
                        if (!float.IsFinite(weight) || weight < 0 || (weight > 0 && (uint)bone >= inverseBind.Length))
                            throw new InvalidDataException("Invalid skin weight.");
                }
                preview.Surfaces.Add(new(mesh, model.Skeleton, inverseBind, ReadBindNormals(mesh)));
            }
        }
        if (preview.VertexCount == 0 && preview.BoneCount == 0)
            throw new InvalidDataException("This CAST has no mesh or skeleton to preview.");
        preview.Sample(0);
        var points = preview.Surfaces.SelectMany(preview.Skin).ToArray();
        if (points.Length == 0) points = skeletons.SelectMany(s => s.Bones.Select(b => b.WorldTranslation)).ToArray();
        var minimum = points.Aggregate(new Vector3(float.MaxValue), Vector3.Min);
        var maximum = points.Aggregate(new Vector3(float.MinValue), Vector3.Max);
        preview.Center = (minimum + maximum) * 0.5f;
        preview.Radius = Math.Max(1, Vector3.Distance(minimum, maximum) * 0.5f);
        preview.AllCenter = preview.Center;
        preview.AllRadius = preview.Radius;
        if (preview.VertexCount > 0)
        {
            // Games park spare magazines/hidden variants far away. Frame the dense subject
            // initially, without removing any geometry; Fit all still exposes every vertex.
            static float Median(float[] values) { Array.Sort(values); return values[values.Length / 2]; }
            var median = new Vector3(Median(points.Select(p => p.X).ToArray()), Median(points.Select(p => p.Y).ToArray()), Median(points.Select(p => p.Z).ToArray()));
            var distances = points.Select(p => Vector3.Distance(p, median)).Order().ToArray();
            preview.Center = median;
            preview.Radius = Math.Max(1, distances[distances.Length * 3 / 4]);
        }
        return preview;
    }

    internal void Sample(float frame)
    {
        foreach (var skeleton in Skeletons) skeleton.InitializeAnimationTransforms();
        foreach (var sampler in samplers) sampler.Update(Math.Clamp(frame, 0, FrameCount - 1), AnimationSampleType.AbsoluteFrameTime);
    }

    internal Vector3[] Skin(Surface surface)
    {
        var mesh = surface.Mesh;
        var positions = mesh.Positions!;
        var output = new Vector3[positions.Count];
        var transforms = surface.Skeleton?.Bones.Select((bone, index) => surface.InverseBind[index]
            * Matrix4x4.CreateFromQuaternion(bone.WorldRotation) * Matrix4x4.CreateTranslation(bone.WorldTranslation)).ToArray() ?? [];
        for (var i = 0; i < positions.Count; i++)
        {
            var sum = Vector3.Zero;
            var total = 0f;
            if (mesh.Influences is { Count: > 0 } weights && transforms.Length > 0)
                for (var j = 0; j < weights.Dimension; j++)
                {
                    var (bone, weight) = weights[i, j];
                    if (weight <= 0) continue;
                    sum += Vector3.Transform(positions[i], transforms[bone]) * weight;
                    total += weight;
                }
            output[i] = total > 0 ? sum / total : positions[i];
        }
        return output;
    }

    internal ShadedSurface SkinShaded(Surface surface)
    {
        var mesh = surface.Mesh;
        var positions = mesh.Positions!;
        var outputPositions = new Vector3[positions.Count];
        var outputNormals = new Vector3[positions.Count];
        var transforms = surface.Skeleton?.Bones.Select((bone, index) => surface.InverseBind[index]
            * Matrix4x4.CreateFromQuaternion(bone.WorldRotation) * Matrix4x4.CreateTranslation(bone.WorldTranslation)).ToArray() ?? [];
        for (var i = 0; i < positions.Count; i++)
        {
            var positionSum = Vector3.Zero;
            var normalSum = Vector3.Zero;
            var total = 0f;
            if (mesh.Influences is { Count: > 0 } weights && transforms.Length > 0)
                for (var j = 0; j < weights.Dimension; j++)
                {
                    var (bone, weight) = weights[i, j];
                    if (weight <= 0) continue;
                    positionSum += Vector3.Transform(positions[i], transforms[bone]) * weight;
                    normalSum += Vector3.TransformNormal(surface.BindNormals[i], transforms[bone]) * weight;
                    total += weight;
                }
            outputPositions[i] = total > 0 ? positionSum / total : positions[i];
            var normal = total > 0 ? normalSum / total : surface.BindNormals[i];
            outputNormals[i] = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : Vector3.UnitZ;
        }
        return new(outputPositions, outputNormals);
    }

    internal static bool IsFinite(Vector3 point) => float.IsFinite(point.X) && float.IsFinite(point.Y) && float.IsFinite(point.Z);

    private static Vector3[] ReadBindNormals(Mesh mesh)
    {
        var positions = mesh.Positions!;
        if (mesh.Normals is { ElementCount: var count, Dimension: > 0 } source && count == positions.Count)
        {
            var imported = new Vector3[count];
            var valid = true;
            for (var i = 0; i < imported.Length; i++)
            {
                var normal = source[i, 0];
                if (!IsFinite(normal) || normal.LengthSquared() < 1e-12f) { valid = false; break; }
                imported[i] = Vector3.Normalize(normal);
            }
            if (valid) return imported;
        }

        // Some animation-only or legacy CAST meshes omit normals. Generate a stable,
        // area-weighted fallback so the preview still receives smooth clay shading.
        var generated = new Vector3[positions.Count];
        foreach (var (a, b, c) in mesh.Faces)
        {
            var normal = Vector3.Cross(positions[b] - positions[a], positions[c] - positions[a]);
            if (normal.LengthSquared() < 1e-12f) continue;
            generated[a] += normal;
            generated[b] += normal;
            generated[c] += normal;
        }
        for (var i = 0; i < generated.Length; i++)
            generated[i] = generated[i].LengthSquared() > 1e-12f ? Vector3.Normalize(generated[i]) : Vector3.UnitZ;
        return generated;
    }

    private static Skeleton ReadSkeleton(SkeletonNode node)
    {
        var skeleton = new Skeleton();
        var bones = node.GetChildrenOfType<BoneNode>();
        foreach (var bone in bones)
        {
            if (!bone.TryGetLocalPosition(out var position) || !bone.TryGetLocalRotation(out var rotation))
                throw new InvalidDataException("The CAST skeleton needs local bind transforms.");
            skeleton.AddBone(new SkeletonBone(bone.Name) { BaseLocalTranslation = position, BaseLocalRotation = rotation });
        }
        for (var i = 0; i < bones.Length; i++)
        {
            var parent = bones[i].ParentIndex;
            if (parent < -1 || parent >= bones.Length || parent == i) throw new InvalidDataException("Invalid skeleton parent.");
            skeleton.Bones[i].Parent = parent < 0 ? null : skeleton.Bones[parent];
        }
        foreach (var bone in skeleton.Bones)
        {
            var depth = 0;
            for (var parent = bone.Parent; parent is not null; parent = parent.Parent)
                if (++depth > bones.Length) throw new InvalidDataException("Cyclic skeleton hierarchy.");
        }
        foreach (var root in skeleton.EnumerateRoots()) root.GenerateWorldTransforms();
        return skeleton;
    }
}
