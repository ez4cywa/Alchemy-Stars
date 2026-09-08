using System.Numerics;
using RedFox.Graphics3D.Skeletal;

namespace AlchemyStars.Avalonia;

internal static class GripSmoke
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length == 0) throw new ArgumentException("Pass a project and output folder, or CAST files to inspect.");
            if (args.All(path => Path.GetExtension(path).Equals(".cast", StringComparison.OrdinalIgnoreCase)))
            {
                foreach (var path in args)
                {
                    var scene = CastPreviewScene.Load(path);
                    Console.WriteLine($"{path}: {scene.VertexCount} vertices, {scene.BoneCount} bones, {scene.FrameCount} frames");
                    foreach (var bone in scene.Skeletons.SelectMany(s => s.Bones).Where(b => b.Name is "tag_weapon" or "j_gun" or "j_wrist_ri" or "j_wrist_le"))
                        Console.WriteLine($"  {bone.Name} parent={bone.Parent?.Name} bind={bone.BaseLocalTranslation}");
                }
                return 0;
            }
            if (args.Length != 2) throw new ArgumentException("Expected project path and test output folder.");
            var store = new WorkspaceProjectStore();
            var source = store.Load(args[0]);
            foreach (var mode in new[] { 0, 1, 2 })
            {
                var document = WorkspaceProjectStore.Snapshot(source);
                document.OutputFormat = ".cast";
                document.CastAnimationOnly = false;
                document.OutputPrefix = document.OutputSuffix = "";
                foreach (var job in document.Animations)
                {
                    job.WeaponFollowMode = mode;
                    job.OutputFolder = Path.GetFullPath(args[1]);
                    job.OutputName = Path.GetFileNameWithoutExtension(job.Name) + "-follow-" + mode;
                }
                var result = new AnimationExportEngine().Export(store.CreateExportRequest(document));
                foreach (var path in result.OutputFiles)
                {
                    var scene = CastPreviewScene.Load(path);
                    Console.WriteLine($"Follow {mode}: {scene.VertexCount} vertices, {scene.BoneCount} bones, {scene.FrameCount} frames; {path}");
                    foreach (var (name, side) in new[] { (document.LeftIKEndBoneName, 1), (document.RightIKEndBoneName, 2) })
                    {
                        var skeleton = scene.Skeletons.Single();
                        var wrist = skeleton.Bones.Single(b => b.Name == name);
                        var mount = skeleton.Bones.Single(b => b.Name == "tag_weapon");
                        scene.Sample(0);
                        var reference = Relative(mount, wrist);
                        float positionError = 0, angleError = 0;
                        for (var frame = 0; frame < scene.FrameCount; frame++)
                        {
                            scene.Sample(frame);
                            var current = Relative(mount, wrist);
                            positionError = Math.Max(positionError, Vector3.Distance(current.Position, reference.Position));
                            angleError = Math.Max(angleError, 2 * MathF.Acos(Math.Clamp(MathF.Abs(Quaternion.Dot(current.Rotation, reference.Rotation)), 0, 1)) * 180 / MathF.PI);
                        }
                        Console.WriteLine($"  {name}: max grip translation drift={positionError:F6}; rotation drift={angleError:F4} degrees");
                        if (mode == side && (positionError > .001f || angleError > .1f)) throw new InvalidOperationException("Weapon follow failed to preserve the grip through the animation.");
                    }
                }
            }
            Console.WriteLine("Weapon grip: follow modes preserve wrist-relative mount across every frame PASS");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
    }

    private static (Vector3 Position, Quaternion Rotation) World(SkeletonBone bone)
    {
        var rotation = Quaternion.Normalize(bone.LocalRotation);
        if (bone.Parent is null) return (bone.LocalTranslation, rotation);
        var parent = World(bone.Parent);
        return (parent.Position + Vector3.Transform(bone.LocalTranslation, parent.Rotation), Quaternion.Normalize(parent.Rotation * rotation));
    }

    private static (Vector3 Position, Quaternion Rotation) Relative(SkeletonBone mount, SkeletonBone wrist)
    {
        var hand = World(wrist);
        var anchor = World(mount);
        var inverse = Quaternion.Inverse(hand.Rotation);
        return (Vector3.Transform(anchor.Position - hand.Position, inverse), Quaternion.Normalize(inverse * anchor.Rotation));
    }
}
