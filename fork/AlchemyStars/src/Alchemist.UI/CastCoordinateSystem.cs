using Cast.NET;
using Cast.NET.Nodes;
using RedFox.Graphics3D;
using RedFox.Graphics3D.Skeletal;
using System.Numerics;
using static Alchemist.UI.CastNodeTraversal;

namespace Alchemist.UI;

/// <summary>Change of basis into the selected right-handed output frame.</summary>
internal static class CastCoordinateSystem
{
    public static string ResolveOutputAxis(string? requested, Cast.NET.Cast source) => requested?.Trim().ToLowerInvariant() switch
    {
        "y" => "y",
        "z" => "z",
        "x" => "x",
        _ => ReadAxis(source),
    };

    public static string ReadAxis(Cast.NET.Cast cast)
    {
        var value = cast.RootNodes.SelectMany(root => root.Children).OfType<MetadataNode>()
            .FirstOrDefault()?.UpAxis?.Trim().ToLowerInvariant();
        // Untagged CAST historically uses Maya's Y-up default. Unknown metadata
        // cannot establish a basis; do not reject an otherwise usable model.
        return value is "x" or "z" ? value : "y";
    }

    private static Vector3 Position(Vector3 v, string axis, string outputAxis)
    {
        var z = axis switch { "y" => new Vector3(v.X, -v.Z, v.Y), "x" => new Vector3(-v.Z, v.Y, v.X), _ => v };
        return outputAxis switch { "y" => new(z.X, z.Z, -z.Y), "x" => new(z.Z, z.Y, -z.X), _ => z };
    }

    private static Vector3 Scale(Vector3 v, string axis, string outputAxis)
    {
        var z = axis switch { "y" => new Vector3(v.X, v.Z, v.Y), "x" => new Vector3(v.Z, v.Y, v.X), _ => v };
        return outputAxis switch { "y" => new(z.X, z.Z, z.Y), "x" => new(z.Z, z.Y, z.X), _ => z };
    }

    // For these proper orthogonal bases, B * R * B^-1 rotates the quaternion's
    // vector part by B and leaves W unchanged (not a root-only extra rotation).
    private static Quaternion Rotation(Quaternion q, string axis, string outputAxis)
    {
        var vector = Position(new(q.X, q.Y, q.Z), axis, outputAxis);
        return new(vector, q.W);
    }

    public static void NormalizeModels(Cast.NET.Cast cast, string outputAxis)
    {
        var axis = ReadAxis(cast);
        if (axis == outputAxis) return;
        foreach (var node in cast.RootNodes.SelectMany(DescendantsAndSelf))
        {
            if (node is BoneNode)
            {
                Vectors(node, "lp", v => Position(v, axis, outputAxis));
                Vectors(node, "wp", v => Position(v, axis, outputAxis));
                Vectors(node, "s", v => Scale(v, axis, outputAxis));
                foreach (var key in new[] { "lr", "wr" })
                    if (node.Properties.TryGetValue(key, out var property) && property is CastArrayProperty<Vector4> values)
                        for (var i = 0; i < values.Values.Count; i++)
                        {
                            var q = values.Values[i];
                            var rotated = Position(new(q.X, q.Y, q.Z), axis, outputAxis);
                            values.Values[i] = new(rotated, q.W);
                        }
            }
            else if (node is MeshNode)
                foreach (var key in new[] { "vp", "vn", "vt" })
                    Vectors(node, key, v => Position(v, axis, outputAxis));
        }
    }

    public static void NormalizeAnimation(SkeletonAnimation animation, string axis, string outputAxis)
    {
        if (axis == outputAxis) return;
        foreach (var target in animation.Targets)
        {
            Frames(target.TranslationFrames, v => Position(v, axis, outputAxis));
            Frames(target.RotationFrames, q => Rotation(q, axis, outputAxis));
            Frames(target.ScaleFrames, v => Scale(v, axis, outputAxis));
        }
    }

    private static void Vectors(CastNode node, string key, Func<Vector3, Vector3> convert)
    {
        if (node.Properties.TryGetValue(key, out var property) && property is CastArrayProperty<Vector3> values)
            for (var i = 0; i < values.Values.Count; i++) values.Values[i] = convert(values.Values[i]);
    }

    private static void Frames<T>(List<AnimationKeyFrame<float, T>>? frames, Func<T, T> convert)
    {
        if (frames is null) return;
        for (var i = 0; i < frames.Count; i++) frames[i] = new(frames[i].Frame, convert(frames[i].Value));
    }
}
