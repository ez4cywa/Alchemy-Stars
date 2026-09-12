using System.Numerics;

namespace AlchemyStars.Avalonia;

internal enum PreviewCameraMode
{
    Orbit,
    FirstPerson,
}

internal readonly record struct PreviewCamera(
    float Yaw,
    float Pitch,
    float Zoom,
    bool AllGeometry = false,
    PreviewCameraMode Mode = PreviewCameraMode.Orbit)
{
    public const float FirstPersonHorizontalFovDegrees = 90;
    public const float MayaRotationXDegrees = 90;
    public const float MayaRotationZDegrees = -90;

    public static PreviewCamera Default => new(-0.9f, 0.3f, 1.2f);
    public static PreviewCamera FirstPerson => new(0, 0, 1, Mode: PreviewCameraMode.FirstPerson);
}

internal readonly record struct PreviewView(
    Vector3 Eye,
    Vector3 Forward,
    Vector3 Right,
    Vector3 Up,
    float FocalLength,
    float NearClip,
    Vector3 SceneOffset);

// Prepares immutable shaded geometry for interactive Skia drawing and retains a
// deterministic z-buffered software rasterizer for headless regressions.
internal static class CastPreviewRenderer
{
    internal const int Background = unchecked((int)0xFF252527);
    internal const int AntiAliasingSamples = 2;

    public static CastPreviewFrame Prepare(CastPreviewScene scene, float frame, int width, int height, PreviewCamera camera, bool bones)
    {
        scene.Sample(frame);
        var view = ResolveView(scene, width, height, camera);
        var triangles = new List<PreviewTriangle>();

        foreach (var surface in scene.Surfaces)
        {
            var skinned = scene.SkinShaded(surface);
            var world = skinned.Positions;
            var projected = world.Select(point => Project(point, view, width, height)).ToArray();
            var colors = world.Select((point, index) => Pack(Shade(point + view.SceneOffset, skinned.Normals[index], view))).ToArray();
            foreach (var (ia, ib, ic) in surface.Mesh.Faces)
            {
                var a = projected[ia]; var b = projected[ib]; var c = projected[ic];
                if (!CastPreviewScene.IsFinite(a) || !CastPreviewScene.IsFinite(b) || !CastPreviewScene.IsFinite(c)) continue;
                triangles.Add(new PreviewTriangle(a, b, c, colors[ia], colors[ib], colors[ic], (a.Z + b.Z + c.Z) / 3));
            }
        }

        // Skia's vertex-color path is deliberately lightweight and does not own a
        // depth buffer. Opaque triangles are therefore submitted far-to-near.
        triangles.Sort(static (left, right) => right.Depth.CompareTo(left.Depth));
        var points = new Vector2[triangles.Count * 3];
        var triangleColors = new uint[points.Length];
        for (var i = 0; i < triangles.Count; i++)
        {
            var triangle = triangles[i];
            var offset = i * 3;
            points[offset] = new Vector2(triangle.A.X, triangle.A.Y);
            points[offset + 1] = new Vector2(triangle.B.X, triangle.B.Y);
            points[offset + 2] = new Vector2(triangle.C.X, triangle.C.Y);
            triangleColors[offset] = unchecked((uint)triangle.ColorA);
            triangleColors[offset + 1] = unchecked((uint)triangle.ColorB);
            triangleColors[offset + 2] = unchecked((uint)triangle.ColorC);
        }

        var lines = new List<Vector2>();
        if (bones || scene.VertexCount == 0)
            foreach (var skeleton in scene.Skeletons)
                foreach (var bone in skeleton.Bones)
                    if (bone.Parent is { } parent)
                    {
                        var a = Project(parent.WorldTranslation, view, width, height);
                        var b = Project(bone.WorldTranslation, view, width, height);
                        if (!CastPreviewScene.IsFinite(a) || !CastPreviewScene.IsFinite(b)) continue;
                        lines.Add(new Vector2(a.X, a.Y));
                        lines.Add(new Vector2(b.X, b.Y));
                    }

        return new CastPreviewFrame(width, height, points, triangleColors, lines.ToArray());
    }

    public static int[] Render(CastPreviewScene scene, float frame, int width, int height, PreviewCamera camera, bool bones)
    {
        scene.Sample(frame);
        var pixels = new int[checked(width * height)];
        var samples = new int[checked(pixels.Length * AntiAliasingSamples)];
        Array.Fill(samples, Background);
        var depth = new float[samples.Length];
        Array.Fill(depth, float.PositiveInfinity);
        var view = ResolveView(scene, width, height, camera);
        foreach (var surface in scene.Surfaces)
        {
            var skinned = scene.SkinShaded(surface);
            var world = skinned.Positions;
            var projected = world.Select(point => Project(point, view, width, height)).ToArray();
            var colors = world.Select((point, index) => Shade(point + view.SceneOffset, skinned.Normals[index], view)).ToArray();
            foreach (var (ia, ib, ic) in surface.Mesh.Faces)
            {
                var a = projected[ia]; var b = projected[ib]; var c = projected[ic];
                if (!CastPreviewScene.IsFinite(a) || !CastPreviewScene.IsFinite(b) || !CastPreviewScene.IsFinite(c)) continue;
                Triangle(a, b, c, colors[ia], colors[ib], colors[ic], width, height, samples, depth);
            }
        }
        for (var i = 0; i < pixels.Length; i++)
            pixels[i] = Resolve(samples[i * AntiAliasingSamples], samples[i * AntiAliasingSamples + 1]);
        if (bones || scene.VertexCount == 0)
            foreach (var skeleton in scene.Skeletons)
                foreach (var bone in skeleton.Bones)
                {
                    var p = Project(bone.WorldTranslation, view, width, height);
                    if (bone.Parent is { } parent) Line(Project(parent.WorldTranslation, view, width, height), p, width, height, pixels);
                }
        return pixels;
    }

    private static Vector3 Project(Vector3 point, PreviewView view, int width, int height)
    {
        var delta = point + view.SceneOffset - view.Eye;
        var z = Vector3.Dot(delta, view.Forward);
        if (z < view.NearClip) return new(float.NaN);
        return new(width * 0.5f + Vector3.Dot(delta, view.Right) * view.FocalLength / z,
            height * 0.5f - Vector3.Dot(delta, view.Up) * view.FocalLength / z, z);
    }

    private static Vector3 Shade(Vector3 point, Vector3 normal, PreviewView view)
    {
        var toEye = view.Eye - point;
        var viewDirection = toEye.LengthSquared() > 1e-12f ? Vector3.Normalize(toEye) : -view.Forward;
        if (Vector3.Dot(normal, viewDirection) < 0) normal = -normal;

        var key = Vector3.Normalize(-view.Forward + view.Up * 0.75f - view.Right * 0.45f);
        var fill = Vector3.Normalize(-view.Forward - view.Up * 0.25f + view.Right * 0.7f);
        var diffuse = MathF.Max(0, Vector3.Dot(normal, key));
        var fillLight = MathF.Max(0, Vector3.Dot(normal, fill));
        var facing = MathF.Max(0, Vector3.Dot(normal, viewDirection));
        var halfVector = Vector3.Normalize(key + viewDirection);
        var specular = MathF.Pow(MathF.Max(0, Vector3.Dot(normal, halfVector)), 36) * 0.3f;
        var rim = MathF.Pow(1 - facing, 2.4f) * 0.18f;

        var clay = new Vector3(0.64f, 0.69f, 0.75f);
        var intensity = 0.24f + diffuse * 0.62f + fillLight * 0.14f;
        var rgb = clay * intensity + new Vector3(specular) + new Vector3(0.22f, 0.5f, 0.68f) * rim;
        return Vector3.Clamp(rgb, Vector3.Zero, Vector3.One) * 255;
    }

    internal static PreviewCamera SnapToAxis(PreviewCamera camera, string upAxis, char axis, bool positive)
    {
        var direction = char.ToLowerInvariant(axis) switch
        {
            'x' => Vector3.UnitX,
            'y' => Vector3.UnitY,
            'z' => Vector3.UnitZ,
            _ => throw new ArgumentOutOfRangeException(nameof(axis)),
        };
        if (!positive) direction = -direction;
        var zUpDirection = CastPreviewScene.ToZUp(direction, upAxis);
        return camera with
        {
            Yaw = MathF.Atan2(zUpDirection.Y, zUpDirection.X),
            Pitch = MathF.Asin(Math.Clamp(zUpDirection.Z, -1, 1)),
            Mode = PreviewCameraMode.Orbit,
        };
    }

    internal static (Vector3 Forward, Vector3 Right, Vector3 Up) ResolveOrbitBasis(string upAxis, PreviewCamera camera)
    {
        var eyeDirection = CastPreviewScene.FromZUp(new Vector3(
            MathF.Cos(camera.Yaw) * MathF.Cos(camera.Pitch),
            MathF.Sin(camera.Yaw) * MathF.Cos(camera.Pitch),
            MathF.Sin(camera.Pitch)), upAxis);
        var forward = Vector3.Normalize(-eyeDirection);
        var worldUp = CastPreviewScene.FromZUp(Vector3.UnitZ, upAxis);
        var right = Vector3.Cross(forward, worldUp);
        if (right.LengthSquared() < 1e-8f)
            right = Vector3.Cross(forward, CastPreviewScene.FromZUp(Vector3.UnitY, upAxis));
        if (right.LengthSquared() < 1e-8f)
            right = Vector3.Cross(forward, CastPreviewScene.FromZUp(Vector3.UnitX, upAxis));
        right = Vector3.Normalize(right);
        return (forward, right, Vector3.Normalize(Vector3.Cross(right, forward)));
    }

    internal static PreviewView ResolveView(CastPreviewScene scene, int width, int height, PreviewCamera camera)
    {
        if (camera.Mode == PreviewCameraMode.FirstPerson)
        {
            // A newly created Maya camera starts at the world origin and looks down local -Z
            // with local +Y as up. Apply the requested Maya XYZ Euler rotation (90, 0, -90)
            // to obtain world forward +X, right -Y and up +Z.
            var radians = MathF.PI / 180;
            var rotation = Matrix4x4.CreateRotationX(PreviewCamera.MayaRotationXDegrees * radians)
                * Matrix4x4.CreateRotationZ(PreviewCamera.MayaRotationZDegrees * radians);
            var forward = scene.FromZUp(Vector3.Normalize(Vector3.TransformNormal(-Vector3.UnitZ, rotation)));
            var right = scene.FromZUp(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, rotation)));
            var up = scene.FromZUp(Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, rotation)));
            var focal = width * 0.5f / MathF.Tan(PreviewCamera.FirstPersonHorizontalFovDegrees * radians * 0.5f);
            // Keep the requested Maya camera transform at the world origin. Apply a
            // preview-only scene translation so geometry crossing/behind the camera
            // is framed in front of the lens instead of being cut by the viewport.
            var verticalTangent = height / (float)Math.Max(1, width);
            var fitTangent = Math.Max(0.1f, Math.Min(1, verticalTangent));
            var fitDistance = scene.AllRadius * 1.15f / fitTangent;
            var sceneOffset = forward * fitDistance - scene.AllCenter;
            return new PreviewView(Vector3.Zero, forward, right, up, focal, 0.1f, sceneOffset);
        }

        var (orbitForward, orbitRight, orbitUp) = ResolveOrbitBasis(scene.UpAxis, camera);
        var radius = camera.AllGeometry ? scene.AllRadius : scene.Radius;
        var center = camera.AllGeometry ? scene.AllCenter : scene.Center;
        var distance = radius * 3.2f / camera.Zoom;
        var eye = center - orbitForward * distance;
        return new PreviewView(eye, orbitForward, orbitRight, orbitUp, Math.Min(width, height) * 1.15f,
            Math.Max(0.001f, scene.Radius * 0.001f), Vector3.Zero);
    }

    private static void Triangle(Vector3 a, Vector3 b, Vector3 c, Vector3 colorA, Vector3 colorB, Vector3 colorC,
        int width, int height, int[] samples, float[] depth)
    {
        var area = Edge(a, b, c.X, c.Y);
        if (MathF.Abs(area) < 0.001f) return;
        var minX = (int)Math.Clamp(MathF.Min(a.X, MathF.Min(b.X, c.X)), 0, width - 1);
        var maxX = (int)Math.Clamp(MathF.Ceiling(MathF.Max(a.X, MathF.Max(b.X, c.X))), 0, width - 1);
        var minY = (int)Math.Clamp(MathF.Min(a.Y, MathF.Min(b.Y, c.Y)), 0, height - 1);
        var maxY = (int)Math.Clamp(MathF.Ceiling(MathF.Max(a.Y, MathF.Max(b.Y, c.Y))), 0, height - 1);
        for (var y = minY; y <= maxY; y++)
            for (var x = minX; x <= maxX; x++)
                for (var sample = 0; sample < AntiAliasingSamples; sample++)
            {
                var offsetX = sample == 0 ? 0.25f : 0.75f;
                var offsetY = sample == 0 ? 0.75f : 0.25f;
                var wa = Edge(b, c, x + offsetX, y + offsetY) / area;
                var wb = Edge(c, a, x + offsetX, y + offsetY) / area;
                var wc = 1 - wa - wb;
                if (wa < 0 || wb < 0 || wc < 0) continue;
                var z = 1 / (wa / a.Z + wb / b.Z + wc / c.Z);
                var index = (y * width + x) * AntiAliasingSamples + sample;
                if (z >= depth[index]) continue;
                depth[index] = z;
                var color = (colorA * (wa / a.Z) + colorB * (wb / b.Z) + colorC * (wc / c.Z)) * z;
                samples[index] = Pack(color);
            }
    }

    private static int Pack(Vector3 color)
    {
        var red = (int)Math.Clamp(color.X, 0, 255);
        var green = (int)Math.Clamp(color.Y, 0, 255);
        var blue = (int)Math.Clamp(color.Z, 0, 255);
        return unchecked((int)0xFF000000) | (red << 16) | (green << 8) | blue;
    }

    private static int Resolve(int first, int second)
    {
        if (first == second) return first;
        var red = (((first >> 16) & 0xFF) + ((second >> 16) & 0xFF)) >> 1;
        var green = (((first >> 8) & 0xFF) + ((second >> 8) & 0xFF)) >> 1;
        var blue = ((first & 0xFF) + (second & 0xFF)) >> 1;
        return unchecked((int)0xFF000000) | (red << 16) | (green << 8) | blue;
    }

    private static float Edge(Vector3 a, Vector3 b, float x, float y) => (x - a.X) * (b.Y - a.Y) - (y - a.Y) * (b.X - a.X);

    private static void Line(Vector3 a, Vector3 b, int width, int height, int[] pixels)
    {
        if (!CastPreviewScene.IsFinite(a) || !CastPreviewScene.IsFinite(b)) return;
        // Clip to the viewport before stepping, so offscreen helper bones cannot stall rendering.
        var delta = b - a;
        var low = 0f; var high = 1f;
        bool Clip(float p, float q)
        {
            if (MathF.Abs(p) < 1e-7f) return q >= 0;
            var r = q / p;
            if (p < 0) low = Math.Max(low, r); else high = Math.Min(high, r);
            return low <= high;
        }
        if (!Clip(-delta.X, a.X) || !Clip(delta.X, width - 1 - a.X) || !Clip(-delta.Y, a.Y) || !Clip(delta.Y, height - 1 - a.Y)) return;
        b = a + delta * high; a += delta * low;
        var steps = Math.Max(1, (int)MathF.Ceiling(MathF.Max(MathF.Abs(b.X - a.X), MathF.Abs(b.Y - a.Y))));
        for (var i = 0; i <= steps; i++)
        {
            var point = Vector3.Lerp(a, b, (float)i / steps);
            var x = (int)point.X; var y = (int)point.Y;
            if ((uint)x < width && (uint)y < height) pixels[y * width + x] = unchecked((int)0xFF64D8EC);
        }
    }

    private readonly record struct PreviewTriangle(
        Vector3 A, Vector3 B, Vector3 C,
        int ColorA, int ColorB, int ColorC,
        float Depth);
}
