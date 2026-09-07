using System.Numerics;
using Cast.NET;
using Cast.NET.Nodes;

namespace AlchemyStars.Engine;

/// <summary>Partitions geometry, never mirrors or duplicates it. Source files remain untouched.</summary>
internal static class CombinedWeaponSplitter
{
    internal sealed record Result(byte[] Left, byte[] Right, string LeftBranch, string RightBranch);

    internal static Result Split(string path, string leftBranch, string rightBranch,
        HashSet<string> leftTargets, HashSet<string> rightTargets)
    {
        var bytes = File.ReadAllBytes(path);
        var model = Read(bytes).RootNodes.SelectMany(Walk).OfType<ModelNode>().Single();
        var bones = model.Skeleton?.Bones ?? throw Error("模型没有骨架 / Model has no skeleton");
        var names = bones.Select(b => b.Name).ToArray();
        if (names.Distinct(StringComparer.OrdinalIgnoreCase).Count() != names.Length)
            throw Error("骨骼名称重复 / Duplicate bone names");
        bool Under(int bone, int root)
        {
            var visited = new HashSet<int>();
            while (bone >= 0)
            {
                if (bone >= bones.Length || !visited.Add(bone)) throw Error("骨架父级无效 / Invalid skeleton hierarchy");
                if (bone == root) return true;
                bone = bones[bone].ParentIndex;
            }
            return false;
        }
        var used = new HashSet<int>();
        foreach (var mesh in model.Meshes)
        {
            var wb = Integers(mesh.VertexWeightBoneBuffer); var wv = mesh.VertexWeightValueBuffer?.Values;
            if (wv is null || wb.Length != wv.Count) throw Error("缺少有效蒙皮权重 / Missing skin weights");
            for (var i = 0; i < wb.Length; i++)
            {
                if (!float.IsFinite(wv[i]) || wv[i] < 0 || wb[i] >= bones.Length) throw Error("蒙皮权重无效 / Invalid skin weights");
                if (wv[i] > 0) used.Add(wb[i]);
            }
        }
        int Resolve(string name, bool left)
        {
            if (!string.IsNullOrEmpty(name))
            {
                var index = Array.FindIndex(names, n => n.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (index < 0) throw Error("分支骨骼不存在 / Branch bone missing: " + name);
                return index;
            }
            var own = left ? leftTargets : rightTargets; var other = left ? rightTargets : leftTargets;
            // A valid automatic branch has side-exclusive animated descendants, no opposite
            // descendants, and carries geometry. Select its highest unambiguous ancestor.
            var candidates = Enumerable.Range(0, bones.Length).Where(root =>
                used.Any(b => Under(b, root)) &&
                Enumerable.Range(0, bones.Length).Any(b => Under(b, root) && own.Contains(names[b]) && !other.Contains(names[b])) &&
                !Enumerable.Range(0, bones.Length).Any(b => Under(b, root) && other.Contains(names[b]) && !own.Contains(names[b]))).ToArray();
            var tops = candidates.Where(c => !candidates.Any(p => p != c && Under(c, p))).ToArray();
            if (tops.Length != 1) throw Error("无法自动确定左右分支，请填写左右武器分支骨骼 / Specify left and right weapon branch bones");
            return tops[0];
        }
        var l = Resolve(leftBranch, true); var r = Resolve(rightBranch, false);
        if (Under(l, r) || Under(r, l)) throw Error("左右分支不能相同或互为祖先 / Weapon branches must be disjoint");
        var sides = Enumerable.Range(0, bones.Length).Select(b => Under(b, l) ? 1 : Under(b, r) ? 2 : 0).ToArray();
        byte[] Partition(int side)
        {
            var cast = Read(bytes);
            var current = cast.RootNodes.SelectMany(Walk).OfType<ModelNode>().Single();
            var keptFaces = 0;
            // Retain the complete source rig for source animation/IK sampling. Only geometry
            // is partitioned; unused helper bones are harmless and keep source identity stable.
            foreach (var mesh in current.Meshes.ToArray())
            {
                var count = mesh.VertexPositionBuffer.Values.Count; var influences = mesh.MaximumWeightInfluence;
                var wb = Integers(mesh.VertexWeightBoneBuffer); var wv = mesh.VertexWeightValueBuffer!.Values;
                if (influences <= 0 || wb.Length != checked(count * influences)) throw Error("蒙皮缓冲区长度不匹配 / Invalid weight buffer length");
                var vertexSides = new int[count];
                for (var v = 0; v < count; v++)
                {
                    for (var k = 0; k < influences; k++)
                    {
                        var i = v * influences + k;
                        if (wv[i] == 0) continue;
                        var s = sides[wb[i]];
                        if (s == 0 || (vertexSides[v] != 0 && vertexSides[v] != s))
                            throw Error("顶点跨左右分支或绑定公共骨骼，请先修正蒙皮 / Vertex crosses weapon branches or uses a shared bone");
                        vertexSides[v] = s;
                    }
                    if (vertexSides[v] == 0) throw Error("存在未蒙皮顶点 / Unweighted vertex");
                }
                var faces = Integers(mesh.FaceBuffer); var selectedFaces = new List<int>();
                if (faces.Length % 3 != 0 || faces.Any(v => v < 0 || v >= count)) throw Error("三角面索引无效 / Invalid triangle indices");
                for (var i = 0; i < faces.Length; i += 3)
                {
                    if (vertexSides[faces[i]] != vertexSides[faces[i + 1]] || vertexSides[faces[i]] != vertexSides[faces[i + 2]])
                        throw Error("三角面跨越左右武器，请先拆开连接面 / Triangle connects both weapons");
                    if (vertexSides[faces[i]] == side) selectedFaces.AddRange(faces.AsSpan(i, 3).ToArray());
                }
                if (selectedFaces.Count == 0) { mesh.Parent = null; continue; }
                keptFaces += selectedFaces.Count;
                var vertices = Enumerable.Range(0, count).Where(v => vertexSides[v] == side).ToArray();
                var map = vertices.Select((v, i) => (v, i)).ToDictionary(x => x.v, x => x.i);
                mesh.Properties["f"] = new CastArrayProperty<uint>(selectedFaces.Select(v => (uint)map[v]));
                foreach (var key in mesh.Properties.Keys.ToArray())
                {
                    if (!(key is "vp" or "vn" or "vt" or "vc" or "wb" or "wv" ||
                        (key.StartsWith('u') || key.StartsWith('c')) && int.TryParse(key.AsSpan(1), out _))) continue;
                    var stride = key is "wb" or "wv" ? influences : 1;
                    if (mesh.Properties[key].ValueCount != count * stride) throw Error("顶点属性长度不匹配 / Invalid vertex attribute: " + key);
                    mesh.Properties[key] = Select(mesh.Properties[key], vertices, stride);
                }
                mesh.AddString("n", (string.IsNullOrEmpty(mesh.Name) ? "weapon" : mesh.Name) + (side == 1 ? "__left" : "__right"));
                if (mesh.Children.Count != 0) throw Error("暂不支持带子节点的网格拆分 / Mesh child data is unsupported");
            }
            if (keptFaces == 0) throw Error("分支没有几何 / Branch contains no geometry");
            if (current.Children.Any(n => n.Identifier == CastNodeIdentifier.BlendShape))
                throw Error("暂不支持带形态键的左右拆分 / Blend shapes are unsupported");
            using var output = new MemoryStream(); CastWriter.Save(output, cast); return output.ToArray();
        }
        return new(Partition(1), Partition(2), names[l], names[r]);
    }

    private static Cast.NET.Cast Read(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return CastReader.Load(stream);
    }
    internal static IEnumerable<CastNode> Walk(CastNode n) { yield return n; foreach (var c in n.Children) foreach (var d in Walk(c)) yield return d; }
    internal static int[] Integers(CastProperty? p) => p switch
    {
        CastArrayProperty<byte> a => a.Values.Select(v => (int)v).ToArray(),
        CastArrayProperty<ushort> a => a.Values.Select(v => (int)v).ToArray(),
        CastArrayProperty<uint> a => a.Values.Select(v => checked((int)v)).ToArray(),
        _ => throw Error("不支持的索引格式 / Unsupported index format")
    };
    private static CastProperty Select(CastProperty p, int[] vertices, int stride) => p switch
    {
        CastArrayProperty<byte> a => Take(a, vertices, stride), CastArrayProperty<ushort> a => Take(a, vertices, stride),
        CastArrayProperty<uint> a => Take(a, vertices, stride), CastArrayProperty<ulong> a => Take(a, vertices, stride),
        CastArrayProperty<float> a => Take(a, vertices, stride), CastArrayProperty<Vector2> a => Take(a, vertices, stride),
        CastArrayProperty<Vector3> a => Take(a, vertices, stride), CastArrayProperty<Vector4> a => Take(a, vertices, stride),
        _ => throw Error("不支持的顶点属性格式 / Unsupported vertex attribute")
    };
    private static CastArrayProperty<T> Take<T>(CastArrayProperty<T> p, int[] vertices, int stride) where T : unmanaged =>
        new(vertices.SelectMany(v => Enumerable.Range(v * stride, stride).Select(i => p.Values[i])));
    private static InvalidDataException Error(string message) => new("组合双武器模型 / Combined weapons: " + message);
}
