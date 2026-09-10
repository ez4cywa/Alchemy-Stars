using System.Numerics;
using System.Security.Cryptography;
using Cast.NET;
using Cast.NET.Nodes;
using RedFox.Graphics3D;
using RedFox.Graphics3D.Skeletal;
using RedFox.Graphics3D.Translation;

namespace AlchemyStars.Avalonia;

internal static class DualWieldSmoke
{
    public static int Run(string[] args)
    {
        try
        {
            if (args.Length is < 2 or > 3 || (args.Length == 3 && args[2] != "--fbx"))
                throw new ArgumentException("--dual-smoke <Scarab fixture directory> <output directory> [--fbx]");
            var source = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]);
            if (output.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || output == source)
                throw new ArgumentException("Smoke outputs must be separate from source fixtures.");
            Directory.CreateDirectory(output);
            var inputs = Directory.GetFiles(source, "*.cast", SearchOption.AllDirectories);
            var hashes = inputs.ToDictionary(p => p, p => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(p))));
            var models = Directory.GetFiles(source, "*.cast");
            var document = new WorkspaceDocument();
            foreach (var model in models)
            {
                var result = ModelPartClassifier.Classify(model);
                document.Parts.Add(new WorkspacePart { FilePath = model, Type = result.Kind, ParentBoneTag = result.RecommendedParentBone });
            }
            var translator = new Graphics3DTranslatorFactory().WithDefaultTranslators();
            foreach (var left in Directory.GetFiles(Path.Combine(source, "anims"), "*_l_*.cast").Order())
            {
                var right = left.Replace("_l_", "_r_", StringComparison.Ordinal);
                var l = new WorkspaceAnimation { Name = left, EnableLeftHandIK = false, EnableRightHandIK = false };
                var r = new WorkspaceAnimation { Name = right, EnableLeftHandIK = false, EnableRightHandIK = false };
                document.Animations.Add(l); document.Animations.Add(r);
                document.DualAnimations.Add(new WorkspaceDualAnimation { Name = Path.GetFileNameWithoutExtension(left).Replace("_l_", "_") + "_dual",
                    LeftAnimationId = l.Id, RightAnimationId = r.Id, OutputFolder = output });
            }
            Require(document.DualAnimations.Count == 9, "Scarab must contain nine pairs.");
            var store = new WorkspaceProjectStore();
            var project = Path.Combine(output, "Scarab-Dual.aprj");
            store.Save(document, project); document = store.Load(project);
            Require(document.SchemaVersion == 2 && document.DualAnimations.All(t => document.Animations.Any(a => a.Id == t.LeftAnimationId)
                && document.Animations.Any(a => a.Id == t.RightAnimationId)), "Project references did not survive round-trip.");
            var engine = new DualWieldEngine();
            foreach (var task in document.DualAnimations)
            {
                var result = engine.Export(document, task);
                var nodes = CastReader.Load(result.OutputFile).RootNodes.SelectMany(Walk).ToArray();
                var model = nodes.OfType<ModelNode>().Single();
                Require(nodes.OfType<AnimationNode>().Count() == 1, "Output must contain one animation.");
                Require(model.Skeleton!.Bones.Length == 221 && model.Meshes.Count() == 39, "Missing source bones or meshes.");
                Require(model.Skeleton.Bones.Count(b => b.ParentIndex < 0) == 1, "Output is not one connected skeleton.");
                Require(model.Skeleton.Bones.Select(b => b.Name).Distinct().Count() == 221, "Weapon identities collided.");
                var original = translator.Load<SkeletonAnimation>(document.Animations.Single(a => a.Id == task.LeftAnimationId).Name);
                Require(result.FrameCount == (int)original.GetAnimationFrameCount(), "Output duration changed.");
                Require(result.UnmappedTargets.Contains("j_gun1613684"), "Unmapped source target was not reported.");
                VerifyCompanionModel(result);
                VerifyMountWorlds(document, task, model, translator.Load<SkeletonAnimation>(result.OutputFile), translator, result.FrameCount);
                Console.WriteLine($"PASS {task.Name}: {result.FrameCount} frames, 221 bones, 39 meshes");
            }
            var fire = document.DualAnimations.Single(t => t.Name.EndsWith("_fire_dual"));
            var originalFire = translator.Load<SkeletonAnimation>(Path.Combine(output, fire.Name + ".cast"));
            var layer = new SkeletonAnimation("left-layer") { Framerate = 30, TransformType = TransformType.Additive };
            var target = new SkeletonAnimationTarget("j_wrist_le") { TransformType = TransformType.Additive };
            target.AddTranslationFrame(0, new Vector3(2, 0, 0)); target.AddRotationFrame(0, Quaternion.Identity); layer.Targets.Add(target);
            var layerPath = Path.Combine(output, "left-layer.cast"); translator.Save(layerPath, layer);
            var leftTask = document.Animations.Single(a => a.Id == fire.LeftAnimationId);
            leftTask.Layers.Add(new WorkspaceLayer { Name = layerPath, Type = AnimationLayerKind.Additive });
            var layeredTask = new WorkspaceDualAnimation { Name = "layered-fire", LeftAnimationId = fire.LeftAnimationId, RightAnimationId = fire.RightAnimationId, OutputFolder = output };
            var layered = translator.Load<SkeletonAnimation>(engine.Export(document, layeredTask).OutputFile);
            Vector3 At(SkeletonAnimation clip, string name) => clip.Targets.Single(t => t.BoneName == name).SampleTranslation(0);
            Require(Vector3.Distance(At(originalFire, "j_wrist_le"), At(layered, "j_wrist_le")) > 1, "Left layer did not reach the dual result.");
            Require(Vector3.Distance(At(originalFire, "j_wrist_ri"), At(layered, "j_wrist_ri")) < 1e-6, "Left layer contaminated right hand.");
            leftTask.Layers.Clear();
            var originalRight = fire.RightAnimationId; fire.RightAnimationId = "missing";
            MustReject(() => engine.Export(document, fire)); fire.RightAnimationId = originalRight;
            var originalName = fire.Name; var originalFolder = fire.OutputFolder;
            fire.Name = Path.GetFileNameWithoutExtension(leftTask.Name); fire.OutputFolder = Path.GetDirectoryName(leftTask.Name)!;
            MustReject(() => engine.Export(document, fire)); fire.Name = originalName; fire.OutputFolder = originalFolder;
            var rTask = document.Animations.Single(a => a.Id == fire.RightAnimationId);
            rTask.OutputFramerate = 60;
            Require(rTask.OutputFramerate == 30, "Workspace output framerate must stay fixed at 30 FPS.");
            var rightPath = rTask.Name;
            rTask.Name = document.Animations.First(a => a.Name.Contains("_r_idle")).Name;
            MustReject(() => engine.Export(document, fire)); rTask.Name = rightPath;
            document.CastAnimationOnly = true;
            layeredTask.Name = "animation-only-fire";
            var only = CastReader.Load(engine.Export(document, layeredTask).OutputFile).RootNodes.SelectMany(Walk).ToArray();
            Require(!only.OfType<ModelNode>().Any() && only.OfType<AnimationNode>().Count() == 1, "Animation-only output contains model data.");
            VerifyCompanionModel(engine.Export(document, layeredTask));
            layeredTask.Name = "no-model-fire"; layeredTask.ExportWeaponModels = false;
            document.DualAnimations.Add(layeredTask);
            var switchProject = Path.Combine(output, "Model-Switch.aprj");
            store.Save(document, switchProject);
            Require(!store.Load(switchProject).DualAnimations.Last().ExportWeaponModels, "Model switch was not persisted.");
            document.DualAnimations.Remove(layeredTask);
            var disabled = engine.Export(document, layeredTask);
            Require(disabled.ModelFile is null && disabled.OutputFiles.Count == 1 && !File.Exists(Path.Combine(output, "no-model-fire_model.cast")), "Disabled switch wrote a model.");
            layeredTask.ExportWeaponModels = true;
            var preview = engine.Export(document, layeredTask, preview: true);
            Require(preview.ModelFile is null && !File.Exists(Path.Combine(output, "no-model-fire_model.cast")), "Preview wrote a companion model.");
            foreach (var format in new[] { ".smd", ".seanim" })
            {
                document.OutputFormat = format; layeredTask.Name = "model-switch-" + format[1..];
                VerifyCompanionModel(engine.Export(document, layeredTask));
            }
            document.OutputFormat = ".cast";
            var protectedInput = new WorkspaceAnimation { Name = Path.Combine(output, "protected_model.cast") };
            document.Animations.Add(protectedInput); layeredTask.Name = "protected";
            MustReject(() => engine.Export(document, layeredTask)); document.Animations.Remove(protectedInput);
            layeredTask.Name = fire.Name + "_model";
            var destinations = new[] { fire, layeredTask }.SelectMany(t => DualWieldEngine.GetOutputFiles(document, t)).ToArray();
            Require(destinations.Distinct(StringComparer.OrdinalIgnoreCase).Count() < destinations.Length, "Batch collision detection missed a companion/animation conflict.");
            Console.WriteLine("PASS model switch on/off, persistence, animation-only CAST, SMD/SEAnim companions, preview isolation and companion path protection");
            if (args.Length == 3)
            {
                document.CastAnimationOnly = false; document.OutputFormat = ".fbx";
                var fbx = engine.Export(document, fire).OutputFile;
                Require(File.ReadAllBytes(fbx).AsSpan().StartsWith("Kaydara FBX Binary"u8), "FBX output is not a binary FBX file.");
                Console.WriteLine("PASS native dual engine -> DCC -> FBX: " + fbx);
            }
            foreach (var input in inputs) Require(hashes[input] == Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(input))), "Source was modified: " + input);
            Console.WriteLine("PASS source-layer propagation, right-side isolation, references, validation, animation-only, source integrity");
            Console.WriteLine(project);
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static IEnumerable<CastNode> Walk(CastNode node)
    {
        yield return node;
        foreach (var child in node.Children) foreach (var descendant in Walk(child)) yield return descendant;
    }
    private static void VerifyCompanionModel(DualWieldResult result)
    {
        Require(result.ModelFile is not null && File.Exists(result.ModelFile), "Companion model was not exported.");
        var nodes = CastReader.Load(result.ModelFile!).RootNodes.SelectMany(Walk).ToArray();
        Require(!nodes.OfType<AnimationNode>().Any(), "Companion model unexpectedly contains an animation.");
        var model = nodes.OfType<ModelNode>().Single(); var bones = model.Skeleton!.Bones;
        Require(bones.Length == 221 && model.Meshes.Count() == 39 && bones.Count(b => b.ParentIndex < 0) == 1, "Companion lost model parts or skeleton connectivity.");
        var leftMeshes = 0; var rightMeshes = 0;
        foreach (var mesh in model.Meshes)
        {
            var weights = mesh.VertexWeightBoneBuffer switch
            {
                CastArrayProperty<byte> p => p.Values.Select(v => (uint)v).ToArray(),
                CastArrayProperty<ushort> p => p.Values.Select(v => (uint)v).ToArray(),
                CastArrayProperty<uint> p => p.Values.ToArray(),
                _ => throw new InvalidDataException("Missing companion skin weights."),
            };
            Require(weights.All(i => i < bones.Length), "Companion has invalid skin indices.");
            if (weights.Any(i => bones[i].Name.EndsWith("__left"))) leftMeshes++;
            if (weights.Any(i => bones[i].Name.EndsWith("__right"))) rightMeshes++;
        }
        Require(leftMeshes == 18 && rightMeshes == 18, "Companion must retain all 18 meshes for each weapon.");
    }
    internal static void VerifyMountWorlds(WorkspaceDocument document, WorkspaceDualAnimation task, ModelNode output,
        SkeletonAnimation result, Graphics3DTranslatorFactory translator, int frames)
    {
        var hands = CastReader.Load(document.Parts.Single(p => p.Type == ModelPartKind.ViewHands).FilePath)
            .RootNodes.SelectMany(Walk).OfType<ModelNode>().Single().Skeleton!.Bones;
        var weapon = CastReader.Load(document.Parts.Single(p => p.Type == ModelPartKind.Weapon).FilePath)
            .RootNodes.SelectMany(Walk).OfType<ModelNode>().Single().Skeleton!.Bones.Single(b => b.ParentIndex < 0);
        foreach (var (id, side) in new[] { (task.LeftAnimationId, "left"), (task.RightAnimationId, "right") })
        {
            var source = translator.Load<SkeletonAnimation>(document.Animations.Single(a => a.Id == id).Name);
            for (var frame = 0; frame < frames; frame++)
            {
                var original = World(hands, source, task.SourceMount, frame);
                var expected = Matrix4x4.CreateFromQuaternion(weapon.LocalRotation) * Matrix4x4.CreateTranslation(weapon.LocalPosition) * original;
                var actual = World(output.Skeleton!.Bones, result, weapon.Name + "__" + side, frame);
                Require(Vector3.Distance(expected.Translation, actual.Translation) < 0.001f, $"{side} weapon world position changed at frame {frame}.");
                Require(1 - MathF.Abs(Quaternion.Dot(Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(expected)),
                    Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(actual)))) < 1e-5, $"{side} weapon world rotation changed at frame {frame}.");
            }
        }
        static Matrix4x4 World(BoneNode[] bones, SkeletonAnimation clip, string name, int frame)
        {
            var index = Array.FindIndex(bones, b => b.Name == name);
            Require(index >= 0, "Missing world-verification target " + name);
            var matrix = Matrix4x4.Identity;
            for (; index >= 0; index = bones[index].ParentIndex)
            {
                var bone = bones[index]; var target = clip.Targets.SingleOrDefault(t => t.BoneName == bone.Name);
                var p = target?.TranslationFrameCount > 0 ? target.SampleTranslation(frame) : bone.LocalPosition;
                var q = target?.RotationFrameCount > 0 ? target.SampleRotation(frame) : bone.LocalRotation;
                var type = target?.TransformType == TransformType.Parent ? clip.TransformType : target?.TransformType;
                if (target?.TranslationFrameCount > 0 && type is TransformType.Relative or TransformType.Additive)
                    p += bone.LocalPosition;
                if (target?.RotationFrameCount > 0 && type == TransformType.Additive)
                    q = bone.LocalRotation * q;
                matrix *= Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q)) * Matrix4x4.CreateTranslation(p);
            }
            return matrix;
        }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void MustReject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidDataException("Invalid request was accepted.");
    }
}
