using Cast.NET;
using Cast.NET.Nodes;
using RedFox.Graphics3D.Skeletal;
using RedFox.Graphics3D.Translation;
using System.Security.Cryptography;
using System.Numerics;
using RedFox.Graphics3D;

namespace AlchemyStars.Avalonia;

internal static class CombinedDualSmoke
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length != 2) throw new ArgumentException("--combined-dual-smoke <quantao> <output>");
            var source = Path.GetFullPath(args[0]); var output = Path.GetFullPath(args[1]);
            if (output.Equals(source, StringComparison.OrdinalIgnoreCase) || output.StartsWith(source + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException("Output must be outside fixtures.");
            Directory.CreateDirectory(output);
            var hashes = Directory.GetFiles(source, "*.cast", SearchOption.AllDirectories).ToDictionary(p => p, p => SHA256.HashData(File.ReadAllBytes(p)));
            var doc = new WorkspaceDocument();
            foreach (var path in Directory.GetFiles(source, "*.cast"))
            {
                var classified = ModelPartClassifier.Classify(path);
                doc.Parts.Add(new WorkspacePart { FilePath = path, Type = classified.Kind });
            }
            foreach (var path in Directory.GetFiles(Path.Combine(source, "anims"), "*_l_*.cast").Order())
            {
                var l = new WorkspaceAnimation { Name = path, EnableLeftHandIK = false, EnableRightHandIK = false };
                var r = new WorkspaceAnimation { Name = path.Replace("_l_", "_r_"), EnableLeftHandIK = false, EnableRightHandIK = false };
                doc.Animations.Add(l); doc.Animations.Add(r);
                doc.DualAnimations.Add(new WorkspaceDualAnimation { Mode = DualModelMode.CombinedWeapons, Name = Path.GetFileNameWithoutExtension(path) + "_dual", LeftAnimationId = l.Id, RightAnimationId = r.Id, OutputFolder = output });
            }
            var store = new WorkspaceProjectStore(); var project = Path.Combine(output, "Quantao-Dual.aprj");
            store.Save(doc, project); doc = store.Load(project);
            Check(doc.SchemaVersion == 3 && doc.DualAnimations.All(t => t.Mode == DualModelMode.CombinedWeapons), "Mode persistence failed");
            var sourceModels = doc.Parts.Select(p => Model(p.FilePath)).ToArray();
            var vertexCount = sourceModels.Sum(m => m.Meshes.Sum(mesh => mesh.VertexPositionBuffer.ValueCount));
            var faceCount = sourceModels.Sum(m => m.Meshes.Sum(mesh => mesh.FaceBuffer.ValueCount));
            var translator = new Graphics3DTranslatorFactory().WithDefaultTranslators();
            var engine = new DualWieldEngine();
            foreach (var task in doc.DualAnimations)
            {
                var result = engine.Export(doc, task); var model = Model(result.OutputFile);
                Check(model.Meshes.Sum(m => m.VertexPositionBuffer.ValueCount) == vertexCount, "Vertices lost or duplicated");
                Check(model.Meshes.Sum(m => m.FaceBuffer.ValueCount) == faceCount, "Faces lost or duplicated");
                Check(model.Skeleton!.Bones.Count(b => b.ParentIndex < 0) == 1, "Disconnected rig");
                Check(model.Skeleton.Bones.Select(b => b.Name).Distinct().Count() == model.Skeleton.Bones.Length, "Duplicate bone names");
                var companion = Model(result.ModelFile!);
                Check(companion.Meshes.Sum(m => m.VertexPositionBuffer.ValueCount) == vertexCount, "Companion model differs");
                Check(!CastReader.Load(result.ModelFile!).RootNodes.SelectMany(Walk).OfType<AnimationNode>().Any(), "Companion includes animation");
                DualWieldSmoke.VerifyMountWorlds(doc, task, model, translator.Load<SkeletonAnimation>(result.OutputFile), translator, result.FrameCount);
                VerifyWeightedBones(doc, task, model, translator.Load<SkeletonAnimation>(result.OutputFile), translator, result.FrameCount);
                Console.WriteLine($"PASS {task.Name}: {result.FrameCount} frames, {model.Skeleton.Bones.Length} bones, {vertexCount} vertices, {faceCount / 3} faces");
            }
            var first = doc.DualAnimations[0];
            first.LeftWeaponBranch = "bone_485fca2f995d0083"; first.RightWeaponBranch = "bone_3f6fb22ccb69d785";
            first.Name = "explicit-branches"; engine.Export(doc, first);
            first.RightWeaponBranch = first.LeftWeaponBranch;
            Reject(() => engine.Export(doc, first));
            first.RightWeaponBranch = "missing"; Reject(() => engine.Export(doc, first));
            first.RightWeaponBranch = "j_gun"; Reject(() => engine.Export(doc, first));
            first.LeftWeaponBranch = first.RightWeaponBranch = "";
            first.Name = "animation-only"; doc.CastAnimationOnly = true;
            var only = engine.Export(doc, first);
            Check(!CastReader.Load(only.OutputFile).RootNodes.SelectMany(Walk).OfType<ModelNode>().Any(), "Animation-only contains mesh");
            first.Name = "no-companion"; first.ExportWeaponModels = false;
            Check(engine.Export(doc, first).ModelFile is null, "Companion switch ignored");
            foreach (var (path, hash) in hashes) Check(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), "Source modified");
            Console.WriteLine("PASS combined geometry conservation, all source mount frames, mode round-trip, invalid branches, animation-only, companion switch, source hashes");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
    }
    private static ModelNode Model(string path) => CastReader.Load(path).RootNodes.SelectMany(Walk).OfType<ModelNode>().Single();
    private static void VerifyWeightedBones(WorkspaceDocument doc, WorkspaceDualAnimation task, ModelNode output,
        SkeletonAnimation baked, Graphics3DTranslatorFactory translator, int frames)
    {
        var weapon = Model(doc.Parts.Single(p => p.Type == ModelPartKind.Weapon).FilePath).Skeleton!.Bones;
        var hands = Model(doc.Parts.Single(p => p.Type == ModelPartKind.ViewHands).FilePath).Skeleton!.Bones;
        var used = new HashSet<int>();
        foreach (var mesh in output.Meshes)
        {
            var indices = mesh.VertexWeightBoneBuffer switch
            {
                CastArrayProperty<byte> a => a.Values.Select(v => (int)v).ToArray(),
                CastArrayProperty<ushort> a => a.Values.Select(v => (int)v).ToArray(),
                CastArrayProperty<uint> a => a.Values.Select(v => (int)v).ToArray(),
                _ => throw new InvalidDataException("Invalid weights")
            };
            for (var i = 0; i < indices.Length; i++) if (mesh.VertexWeightValueBuffer!.Values[i] > 0) used.Add(indices[i]);
        }
        foreach (var (id, suffix) in new[] { (task.LeftAnimationId, "__left"), (task.RightAnimationId, "__right") })
        {
            var raw = translator.Load<SkeletonAnimation>(doc.Animations.Single(a => a.Id == id).Name);
            foreach (var bone in used.Select(i => output.Skeleton!.Bones[i]).Where(b => b.Name.EndsWith(suffix)))
            for (var frame = 0; frame < frames; frame++)
            {
                var expected = World(weapon, raw, bone.Name[..^suffix.Length], frame, true) * World(hands, raw, task.SourceMount, frame, false);
                var actual = World(output.Skeleton!.Bones, baked, bone.Name, frame, false);
                Check(Vector3.Distance(expected.Translation, actual.Translation) < 0.002f, $"Weighted bone position differs: {bone.Name}, frame {frame}");
                Check(1 - MathF.Abs(Quaternion.Dot(Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(expected)), Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(actual)))) < 1e-5f,
                    $"Weighted bone rotation differs: {bone.Name}, frame {frame}");
            }
        }
        static Matrix4x4 World(BoneNode[] bones, SkeletonAnimation clip, string name, int frame, bool isolateRoot)
        {
            var index = Array.FindIndex(bones, b => b.Name == name); Check(index >= 0, "Missing bone " + name);
            var result = Matrix4x4.Identity;
            for (; index >= 0; index = bones[index].ParentIndex)
            {
                var bone = bones[index]; var target = isolateRoot && bone.ParentIndex < 0 ? null : clip.Targets.SingleOrDefault(t => t.BoneName == bone.Name);
                var p = target?.TranslationFrameCount > 0 ? target.SampleTranslation(frame) : bone.LocalPosition;
                var q = target?.RotationFrameCount > 0 ? target.SampleRotation(frame) : bone.LocalRotation;
                var type = target?.TransformType == TransformType.Parent ? clip.TransformType : target?.TransformType;
                if (target?.TranslationFrameCount > 0 && type is TransformType.Relative or TransformType.Additive) p += bone.LocalPosition;
                if (target?.RotationFrameCount > 0 && type == TransformType.Additive) q = bone.LocalRotation * q;
                result *= Matrix4x4.CreateFromQuaternion(Quaternion.Normalize(q)) * Matrix4x4.CreateTranslation(p);
            }
            return result;
        }
    }
    private static IEnumerable<CastNode> Walk(CastNode n) { yield return n; foreach (var c in n.Children) foreach (var d in Walk(c)) yield return d; }
    private static void Check(bool ok, string message) { if (!ok) throw new InvalidDataException(message); }
    private static void Reject(Action action) { try { action(); } catch (InvalidDataException) { return; } throw new Exception("Invalid input accepted"); }
}
