using System.Globalization;
using System.Numerics;
using System.Text;
using Alchemist.UI;
using Cast.NET;
using Cast.NET.Nodes;
using RedFox.Graphics3D;
using RedFox.Graphics3D.Skeletal;
using static Alchemist.UI.CastNodeTraversal;

namespace AlchemyStars.Engine;

internal static class SmdModelExporter
{
    internal static void Save(string output, string mergedCast, Skeleton skeleton)
    {
        var bind = new SkeletonAnimation("bind", skeleton) { TransformType = TransformType.Absolute, Framerate = 30 };
        for (var index = 0; index < skeleton.Bones.Count; index++)
        {
            bind.Targets[index].AddTranslationFrame(0, skeleton.Bones[index].BaseLocalTranslation);
            bind.Targets[index].AddRotationFrame(0, skeleton.Bones[index].BaseLocalRotation);
        }
        SmdAnimationExporter.Save(output, bind);
        using var writer = new StreamWriter(output, true, new UTF8Encoding(false));
        writer.WriteLine("triangles");
        var model = CastReader.Load(mergedCast).RootNodes.SelectMany(DescendantsAndSelf).OfType<ModelNode>().Single();
        foreach (var mesh in model.Meshes)
        {
            var positions = mesh.VertexPositionBuffer.Values;
            var normals = mesh.VertexNormalBuffer?.Values;
            var uv = (mesh.GetUVLayer(0) as CastArrayProperty<Vector2>)?.Values;
            var indices = Integers(mesh.FaceBuffer);
            var bones = mesh.VertexWeightBoneBuffer is { } boneBuffer ? Integers(boneBuffer) : [];
            var weights = mesh.VertexWeightValueBuffer?.Values;
            var material = mesh.Material?.Name;
            if (string.IsNullOrWhiteSpace(material)) material = "material";
            if (material.Contains('\n') || material.Contains('\r')) throw new InvalidDataException("Invalid material name.");
            if (indices.Length % 3 != 0) throw new InvalidDataException("Invalid triangles.");
            for (var index = 0; index < indices.Length; index += 3)
            {
                writer.WriteLine(material);
                var a = positions[indices[index]];
                var b = positions[indices[index + 1]];
                var c = positions[indices[index + 2]];
                var faceNormal = Vector3.Cross(b - a, c - a);
                faceNormal = faceNormal.LengthSquared() > 1e-12f ? Vector3.Normalize(faceNormal) : Vector3.UnitZ;
                for (var corner = 0; corner < 3; corner++)
                {
                    var vertex = indices[index + corner];
                    var position = positions[vertex];
                    var normal = normals is not null ? normals[vertex] : faceNormal;
                    normal = normal.LengthSquared() > 1e-12f ? Vector3.Normalize(normal) : faceNormal;
                    var texture = uv is not null ? uv[vertex] : Vector2.Zero;
                    var links = new Dictionary<int, float>();
                    for (var influence = 0; influence < mesh.MaximumWeightInfluence; influence++)
                    {
                        var weightIndex = vertex * mesh.MaximumWeightInfluence + influence;
                        if (weights is null || weightIndex >= weights.Count || weights[weightIndex] <= 0) continue;
                        var bone = bones[weightIndex];
                        if ((uint)bone >= skeleton.Bones.Count || !float.IsFinite(weights[weightIndex]))
                            throw new InvalidDataException("Invalid skin weights.");
                        links[bone] = links.GetValueOrDefault(bone) + weights[weightIndex];
                    }
                    if (links.Count == 0) links[0] = 1;
                    var total = links.Values.Sum();
                    var primaryBone = links.MaxBy(link => link.Value).Key;
                    writer.Write(FormattableString.Invariant($"{primaryBone} {position.X:R} {position.Y:R} {position.Z:R} {normal.X:R} {normal.Y:R} {normal.Z:R} {texture.X:R} {texture.Y:R} {links.Count}"));
                    foreach (var (bone, weight) in links)
                        writer.Write(" " + bone + " " + (weight / total).ToString("R", CultureInfo.InvariantCulture));
                    writer.WriteLine();
                }
            }
        }
        writer.WriteLine("end");
    }

    private static int[] Integers(CastProperty property) => property switch
    {
        CastArrayProperty<byte> values => values.Values.Select(value => (int)value).ToArray(),
        CastArrayProperty<ushort> values => values.Values.Select(value => (int)value).ToArray(),
        CastArrayProperty<uint> values => values.Values.Select(value => checked((int)value)).ToArray(),
        _ => throw new InvalidDataException("Unsupported mesh index buffer."),
    };
}
