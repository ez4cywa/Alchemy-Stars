using System.Numerics;
using System.Text.Json;

namespace AlchemyStars.Avalonia;

internal sealed record MergerPreviewMesh(Vector3[] Positions, uint[] Indices);
internal sealed record MergerPreviewData(string Name, long MeshCount, long VertexCount, long TriangleCount,
    int DisplayedTriangles, Vector3 Minimum, Vector3 Maximum, MergerPreviewMesh[] Meshes)
{
    internal const int TriangleLimit = 250_000;
    internal static MergerPreviewData Parse(JsonElement json)
    {
        static Vector3 Vector(JsonElement e) => new(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle());
        var meshes = new List<MergerPreviewMesh>();
        var total = 0;
        foreach (var mesh in json.GetProperty("meshes").EnumerateArray())
        {
            var positions = mesh.GetProperty("positions").EnumerateArray().Select(Vector).ToArray();
            if (positions.Any(v => !float.IsFinite(v.X) || !float.IsFinite(v.Y) || !float.IsFinite(v.Z)))
                throw new InvalidDataException("Preview contains non-finite positions.");
            var indices = mesh.GetProperty("triangle_indices").EnumerateArray().Select(v => v.GetUInt32()).ToArray();
            total = checked(total + indices.Length / 3);
            if (indices.Length % 3 != 0 || total > TriangleLimit || indices.Any(i => i >= positions.Length))
                throw new InvalidDataException("Preview geometry exceeds the limit or has invalid indices.");
            meshes.Add(new(positions, indices));
        }
        if (total == 0) throw new InvalidDataException("No previewable geometry.");
        var bounds = json.GetProperty("bounds");
        var minimum = Vector(bounds.GetProperty("minimum"));
        var maximum = Vector(bounds.GetProperty("maximum"));
        if (!float.IsFinite((maximum - minimum).Length()) || maximum.X < minimum.X || maximum.Y < minimum.Y || maximum.Z < minimum.Z)
            throw new InvalidDataException("Invalid preview bounds.");
        return new(json.GetProperty("model_name").GetString() ?? "CAST", json.GetProperty("source_mesh_count").GetInt64(),
            json.GetProperty("source_vertex_count").GetInt64(), json.GetProperty("source_triangle_count").GetInt64(),
            total, minimum, maximum, meshes.ToArray());
    }
}

internal readonly record struct MergerPreviewCamera(float Yaw, float Pitch, float Distance)
{
    internal static MergerPreviewCamera Default => new(-0.75f, 0.28f, 3.4f);
    internal MergerPreviewCamera Orbit(float x, float y) => this with { Yaw = Yaw + x, Pitch = Math.Clamp(Pitch + y, -1.5f, 1.5f) };
    internal MergerPreviewCamera Zoom(float amount) => this with { Distance = Math.Clamp(Distance * MathF.Exp(-amount * 0.12f), 1.15f, 30) };
}

// Dedicated read-only renderer. Each pixel is depth tested; input triangle order is irrelevant.
// Rendering runs on a background task and does not change the existing animation preview.
internal static class ModelMergerPreviewRenderer
{
    internal static int[] Render(MergerPreviewData data, MergerPreviewCamera camera, int width, int height,
        bool grid, bool dark, CancellationToken cancellation)
    {
        var pixels = new int[checked(width * height)];
        Array.Fill(pixels, dark ? unchecked((int)0xff252527) : unchecked((int)0xffe7e7e9));
        var depth = new float[pixels.Length];
        Array.Fill(depth, float.PositiveInfinity);
        var center = (data.Minimum + data.Maximum) * 0.5f;
        var radius = Math.Max((data.Maximum - data.Minimum).Length() * 0.5f, 0.001f);
        var direction = new Vector3(MathF.Cos(camera.Pitch) * MathF.Sin(camera.Yaw), MathF.Sin(camera.Pitch), MathF.Cos(camera.Pitch) * MathF.Cos(camera.Yaw));
        var eye = center + direction * radius * camera.Distance;
        var forward = -direction;
        var right = Vector3.Normalize(Vector3.Cross(forward, Vector3.UnitY));
        var up = Vector3.Cross(right, forward);
        var focal = Math.Min(width, height) * 0.95f;
        Vector3 Project(Vector3 p)
        {
            var local = p - eye;
            var z = Vector3.Dot(local, forward);
            if (z <= radius * 0.01f) return new(float.NaN);
            return new(width * 0.5f + Vector3.Dot(local, right) * focal / z,
                height * 0.5f - Vector3.Dot(local, up) * focal / z, z);
        }
        foreach (var mesh in data.Meshes)
        {
            cancellation.ThrowIfCancellationRequested();
            var projected = mesh.Positions.Select(Project).ToArray();
            for (var i = 0; i < mesh.Indices.Length; i += 3)
            {
                if ((i & 255) == 0) cancellation.ThrowIfCancellationRequested();
                var ia = mesh.Indices[i]; var ib = mesh.Indices[i + 1]; var ic = mesh.Indices[i + 2];
                var normal = Vector3.Cross(mesh.Positions[ib] - mesh.Positions[ia], mesh.Positions[ic] - mesh.Positions[ia]);
                var length = normal.Length();
                var shade = length > 1e-12f ? 0.4f + 0.55f * Math.Abs(Vector3.Dot(normal / length, Vector3.Normalize(new Vector3(-0.3f, 0.7f, 0.6f)))) : 0.6f;
                var value = (int)(shade * 240);
                var color = unchecked((int)0xff000000) | value << 16 | value << 8 | value;
                Triangle(projected[ia], projected[ib], projected[ic], color, width, height, pixels, depth, cancellation);
            }
        }
        if (grid)
        {
            var extent = radius * 1.5f;
            var floor = data.Minimum.Y - radius * 0.01f;
            for (var i = -10; i <= 10; i++)
            {
                var n = i * extent / 10;
                Line(Project(new(center.X + n, floor, center.Z - extent)), Project(new(center.X + n, floor, center.Z + extent)));
                Line(Project(new(center.X - extent, floor, center.Z + n)), Project(new(center.X + extent, floor, center.Z + n)));
            }
        }
        return pixels;

        void Line(Vector3 a, Vector3 b)
        {
            if (!float.IsFinite(a.X) || !float.IsFinite(b.X)) return;
            var steps = (int)Math.Min(8192, Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)));
            for (var j = 0; j <= steps; j++)
            {
                var t = steps == 0 ? 0 : j / (float)steps;
                var p = Vector3.Lerp(a, b, t);
                var x = (int)p.X; var y = (int)p.Y;
                if (x < 0 || x >= width || y < 0 || y >= height) continue;
                var z = 1 / ((1 - t) / a.Z + t / b.Z);
                if (z < depth[y * width + x]) pixels[y * width + x] = dark ? unchecked((int)0xff55555a) : unchecked((int)0xffb4b4bb);
            }
        }
    }

    internal static void Triangle(Vector3 a, Vector3 b, Vector3 c, int color, int width, int height,
        int[] pixels, float[] depth, CancellationToken cancellation = default)
    {
        if (!float.IsFinite(a.X) || !float.IsFinite(b.X) || !float.IsFinite(c.X)) return;
        static float Edge(Vector3 p, Vector3 q, float x, float y) => (x - p.X) * (q.Y - p.Y) - (y - p.Y) * (q.X - p.X);
        var area = Edge(a, b, c.X, c.Y);
        if (Math.Abs(area) < 0.0001f) return;
        var minX = (int)Math.Clamp(MathF.Min(a.X, MathF.Min(b.X, c.X)), 0, width - 1);
        var maxX = (int)Math.Clamp(MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))), 0, width - 1);
        var minY = (int)Math.Clamp(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)), 0, height - 1);
        var maxY = (int)Math.Clamp(MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))), 0, height - 1);
        for (var y = minY; y <= maxY; y++)
        {
            if ((y & 31) == 0) cancellation.ThrowIfCancellationRequested();
            for (var x = minX; x <= maxX; x++)
            {
                var wa = Edge(b, c, x + 0.5f, y + 0.5f) / area;
                var wb = Edge(c, a, x + 0.5f, y + 0.5f) / area;
                var wc = 1 - wa - wb;
                if (wa < 0 || wb < 0 || wc < 0) continue;
                var z = 1 / (wa / a.Z + wb / b.Z + wc / c.Z);
                var index = y * width + x;
                if (z >= depth[index]) continue;
                depth[index] = z;
                pixels[index] = color;
            }
        }
    }
}
