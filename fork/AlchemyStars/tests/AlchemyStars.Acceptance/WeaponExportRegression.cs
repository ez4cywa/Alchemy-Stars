using Alchemist.UI;
using Cast.NET;
using Cast.NET.Nodes;
using RedFox.Graphics3D.Skeletal;
using System.IO;
using System.Numerics;
using System.Text.Json;

internal static class WeaponExportRegression
{
    public static void TestStructure(string directory)
    {
        Directory.CreateDirectory(directory);
        var hands = Path.Combine(directory, "hands.cast");
        var weapon = Path.Combine(directory, "weapon.cast");
        var delta = Path.Combine(directory, "delta.cast");
        var animationPath = Path.Combine(directory, "idle.cast");
        WriteModel(hands, [("tag_origin", -1, new(4, 6, 0), Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2)),
            ("tag_weapon", 0, Vector3.Zero, Quaternion.Identity), ("j_wrist_ri", 0, Vector3.Zero, Quaternion.Identity),
            ("j_gun", 2, Vector3.One, Quaternion.Identity)], null);
        WriteModel(weapon, [("j_gun", -1, new(10, 0, 0), Quaternion.Identity),
            ("slide", 0, new(1, 0, 0), Quaternion.Identity)], new(11, 0, 0));
        WriteModel(delta, [("j_gun", -1, new(10, 0, 0), Quaternion.Identity),
            ("slide", 0, new(2, 0, 0), Quaternion.Identity)], null);
        var root = new CastNode(CastNodeIdentifier.Root);
        new AnimationNode { Parent = root }.AddValue("fr", 30f);
        CastWriter.Save(animationPath, new Cast.NET.Cast([root]));
        var vm = new MainViewModel(_ => { }, string.Empty) { OutputFormat = ".cast", CastAnimationOnly = false };
        vm.Parts.Add(new Part(vm, hands) { Type = PartType.ViewHands });
        vm.Parts.Add(new Part(vm, weapon) { Type = PartType.Weapon });
        vm.Animations.Add(new Animation(animationPath) { OutputFolder = directory, OutputName = "merged", EnableLeftHandIK = false, EnableRightHandIK = false });
        var output = vm.ExportAnimations().Single();
        var model = CastReader.Load(output).RootNodes.Single().Children.OfType<ModelNode>().Single();
        Check(Vector3.Distance(model.Meshes.Single().VertexPositionBuffer.Values[0], new(4, 17, 0)) < 0.00001f,
            "Rotated nonzero source origin corrupted mesh alignment.");
        vm.Parts.Add(new Part(vm, weapon) { Type = PartType.Attachment });
        var reused = AnimationConverter.LoadSkeletonFromParts(vm.Parts, false);
        Check(reused.Bones.Count == 6, "Equivalent attachment bones should share their matching bind hierarchy.");
        vm.Parts.Add(new Part(vm, delta) { Type = PartType.Attachment });
        var separated = AnimationConverter.LoadSkeletonFromParts(vm.Parts, false);
        Check(separated.Bones.Count == 7 && separated.FindBone("slide__attachment") is not null,
            "Different bind transforms must not silently reuse the same bone.");
        var plan = SkeletonMergePlan.Build(vm.Parts, false);
        var ambiguous = new SkeletonAnimation("ambiguous");
        ambiguous.Targets.Add(new SkeletonAnimationTarget("slide"));
        try { plan.BindAnimation(ambiguous); throw new Exception("Ambiguous slide animation was accepted."); }
        catch (InvalidDataException) { }
        var handTrack = new SkeletonAnimation("hand");
        handTrack.Targets.Add(new SkeletonAnimationTarget("j_gun"));
        plan.BindAnimation(handTrack);
        Check(handTrack.Targets.Single().BoneName == "j_gun", "Unqualified hand animation was sent to the weapon root.");
        vm.Parts[1].ParentBoneTag = "missing";
        try { AnimationConverter.LoadSkeletonFromParts(vm.Parts, false); throw new Exception("Missing parent was accepted."); }
        catch (InvalidDataException) { }
        vm.Parts[1].ParentBoneTag = "j_wrist_ri";
        var explicitParent = AnimationConverter.LoadSkeletonFromParts(vm.Parts.Take(2), false);
        Check(explicitParent.FindBone("j_gun__weapon")?.Parent?.Name == "j_wrist_ri", "Explicit parent was overridden.");
        TestAttachmentFrames(Path.Combine(directory, "attachment-frames"));
    }

    private static void TestAttachmentFrames(string directory)
    {
        Directory.CreateDirectory(directory);
        var weapon = Path.Combine(directory, "weapon.cast");
        var attachment = Path.Combine(directory, "attachment.cast");
        var rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI / 2);
        WriteModel(weapon, [("gun", -1, new(4, 6, 0), Quaternion.Identity),
            ("tag_scope", 0, new(10, 0, 0), rotation)], null);
        var vm = new MainViewModel(_ => { }, string.Empty);
        vm.Parts.Add(new Part(vm, weapon) { Type = PartType.Weapon });
        vm.Parts.Add(new Part(vm, attachment) { Type = PartType.Attachment });

        // A root authored in the mount's parent frame must keep the old reuse path.
        WriteModel(attachment, [("tag_scope", -1, new(10, 0, 0), Quaternion.Negate(rotation))], null);
        var reused = AnimationConverter.LoadSkeletonFromParts(vm.Parts, false);
        Check(reused.Bones.Count == 2, "Equivalent attachment root (including q/-q) was not reused.");

        WriteModel(attachment, [("tag_scope", -1, new(10, 0, 0), Quaternion.Identity)], null);
        var rotated = AnimationConverter.LoadSkeletonFromParts(vm.Parts, false);
        Check(rotated.FindBone("tag_scope__attachment")?.Parent?.Name == "tag_scope",
            "A rotation-only bind mismatch must not reuse the mount's parent frame.");
        WriteModel(attachment, [("tag_scope", -1, new(10, 0, 0), rotation)], null);
        var scaledFile = CastReader.Load(attachment);
        scaledFile.RootNodes.Single().Children.OfType<ModelNode>().Single().Skeleton!.Bones[0]
            .AddValue("s", new Vector3(2));
        CastWriter.Save(attachment, scaledFile);
        var scaled = AnimationConverter.LoadSkeletonFromParts(vm.Parts, false);
        Check(scaled.FindBone("tag_scope__attachment")?.Parent?.Name == "tag_scope",
            "A scale-only bind mismatch must not reuse the mount's parent frame.");

        // An origin-authored root instead inherits the complete mount transform.
        WriteModel(attachment, [("tag_scope", -1, Vector3.Zero, Quaternion.Identity)], Vector3.UnitX);
        var attached = AnimationConverter.LoadSkeletonFromParts(vm.Parts, false);
        Check(attached.FindBone("tag_scope__attachment")?.Parent?.Name == "tag_scope",
            "Origin-authored attachment must parent to its matching mount, not the mount's parent.");
        var animationPath = Path.Combine(directory, "idle.cast");
        var root = new CastNode(CastNodeIdentifier.Root);
        new AnimationNode { Parent = root }.AddValue("fr", 30f);
        CastWriter.Save(animationPath, new Cast.NET.Cast([root]));
        var engine = new AlchemyStars.Engine.AnimationExportEngine();
        var request = new AlchemyStars.Engine.AnimationExportRequest(
            [new(weapon, AlchemyStars.Engine.ModelPartKind.Weapon),
             new(attachment, AlchemyStars.Engine.ModelPartKind.Attachment)],
            [new(animationPath, "mounted", directory, EnableLeftHandIk: false, EnableRightHandIk: false)],
            new(new("", "", "", ""), new("", "", "", "")));
        var output = engine.Export(request).OutputFiles.Single();
        var model = CastReader.Load(output).RootNodes.Single().Children.OfType<ModelNode>().Single();
        Check(Vector3.Distance(model.Meshes.Single().VertexPositionBuffer.Values[0], new(14, 7, 0)) < 1e-5f,
            "Engine export did not move/rotate attachment geometry into the mount frame.");

        // Even a non-origin mismatch must retain its local offset under the mount.
        WriteModel(attachment, [("tag_scope", -1, new(2, 0, 0), Quaternion.Identity)], null);
        var offset = AnimationConverter.LoadSkeletonFromParts(vm.Parts, false);
        Check(offset.FindBone("tag_scope__attachment")?.Parent?.Name == "tag_scope",
            "Nonmatching attachment frame was attached to the wrong parent.");
        vm.Parts[1].ParentBoneTag = "gun";
        var explicitParent = AnimationConverter.LoadSkeletonFromParts(vm.Parts, false);
        Check(explicitParent.FindBone("tag_scope__attachment")?.Parent?.Name == "gun",
            "Attachment auto-parenting overrode an explicit parent.");
        vm.Parts[1].ParentBoneTag = "";
        WriteModel(attachment, [("missing_mount", -1, Vector3.Zero, Quaternion.Identity)], null);
        try { AnimationConverter.LoadSkeletonFromParts(vm.Parts, false); throw new Exception("Missing attachment mount was accepted."); }
        catch (InvalidDataException) { }
        WriteModel(weapon, [("gun", -1, Vector3.Zero, Quaternion.Identity),
            ("tag_scope", 0, new(1, 0, 0), Quaternion.Identity),
            ("tag_scope", 0, new(2, 0, 0), Quaternion.Identity)], null);
        WriteModel(attachment, [("tag_scope", -1, Vector3.Zero, Quaternion.Identity)], null);
        try { AnimationConverter.LoadSkeletonFromParts(vm.Parts, false); throw new Exception("Ambiguous attachment mount was accepted."); }
        catch (InvalidDataException) { }
    }

    private static void WriteModel(string path, (string Name, int Parent, Vector3 Position, Quaternion Rotation)[] definitions, Vector3? vertex)
    {
        ulong hash = 1;
        var root = new CastNode(CastNodeIdentifier.Root) { Hash = hash++ };
        var model = new ModelNode { Parent = root, Hash = hash++ };
        var skeleton = new SkeletonNode { Parent = model, Hash = hash++ };
        var world = new List<(Vector3 Position, Quaternion Rotation)>();
        foreach (var d in definitions)
        {
            var position = d.Parent < 0 ? d.Position : Vector3.Transform(d.Position, world[d.Parent].Rotation) + world[d.Parent].Position;
            var rotation = d.Parent < 0 ? d.Rotation : world[d.Parent].Rotation * d.Rotation;
            var bone = new BoneNode { Parent = skeleton, Hash = hash++ };
            bone.AddString("n", d.Name); bone.AddValue("p", d.Parent < 0 ? uint.MaxValue : (uint)d.Parent);
            bone.AddValue("lp", d.Position); bone.AddValue("wp", position);
            bone.AddValue("lr", new Vector4(d.Rotation.X, d.Rotation.Y, d.Rotation.Z, d.Rotation.W));
            bone.AddValue("wr", new Vector4(rotation.X, rotation.Y, rotation.Z, rotation.W));
            world.Add((position, rotation));
        }
        if (vertex is { } v)
        {
            var mesh = new MeshNode { Parent = model, Hash = hash++ };
            mesh.Properties["vp"] = new CastArrayProperty<Vector3>([v, v + Vector3.UnitX, v + Vector3.UnitY]);
            mesh.Properties["f"] = new CastArrayProperty<byte>([0, 1, 2]);
            mesh.Properties["wb"] = new CastArrayProperty<byte>([0, 0, 0]);
            mesh.Properties["wv"] = new CastArrayProperty<float>([1, 1, 1]);
            mesh.AddValue("mi", (byte)1);
        }
        CastWriter.Save(path, new Cast.NET.Cast([root]));
    }

    public static void Run(string outputDirectory)
    {
        Directory.CreateDirectory(outputDirectory);
        var reports = new List<object>();
        foreach (var (id, weapon, prefix, motion) in new[]
        {
            ("1911", @"D:\_tiqu\mergedmodels\sat_vm_pi_alcor_rec_LOD0.cast", "sat_vm_pi_alcor", "ads_down"),
            ("P27", @"D:\_tiqu\mergedmodels\att_vm_p27_pi_papa220_rec_v0_LOD0.cast", "vm_p27_pi_papa220", "ads_up"),
        })
        {
            var vm = new MainViewModel(_ => { }, string.Empty);
            var hands = @"D:\_tiqu\Files\viewhands_mp_base_iw8_LOD0.cast";
            var animations = @"D:\_tiqu\Saluki\exported_files\bo7\animations";
            vm.Parts.Add(new Part(vm, weapon) { Type = PartType.Weapon }); // Reproduce old empty-parent / weapon-first project.
            vm.Parts.Add(new Part(vm, hands) { Type = PartType.ViewHands });
            var animation = new Animation(Path.Combine(animations, prefix + "_idle.cast"))
            {
                OutputFolder = Path.Combine(outputDirectory, id), OutputName = id + "_" + motion,
                EnableLeftHandIK = true, EnableRightHandIK = true,
            };
            animation.Layers.Add(new AnimationLayer(Path.Combine(animations, prefix + "_" + motion + ".cast"), animation)
            { Type = AnimationLayerType.Additive });
            vm.Animations.Add(animation);
            if (id == "1911")
            {
                vm.LoadProjectFile(@"D:\_tiqu\Files\1911.aprj");
                animation = vm.Animations.Single();
                animation.OutputFolder = Path.Combine(outputDirectory, id);
                animation.OutputName = id + "_" + motion;
            }
            var full = vm.ExportAnimations().Single();
            Validate(full);
            vm.BakeRelevantBonesOnly = true;
            animation.OutputName += "_selective";
            var selective = vm.ExportAnimations().Single();
            Validate(selective);
            vm.CastAnimationOnly = true;
            animation.OutputName = id + "_animation";
            var animationOnly = vm.ExportAnimations().Single();
            vm.CastAnimationOnly = false;
            vm.OutputFormat = ".smd";
            var smd = vm.ExportAnimations().Single();
            vm.OutputFormat = ".fbx";
            var fbx = vm.ExportAnimations().Single();
            reports.Add(new { id, hands, weapon, main = animation.Name,
                layers = animation.Layers.Select(l => l.Name).ToArray(),
                layerTypes = animation.Layers.Select(l => (int)l.Type).ToArray(), full, selective, animationOnly, smd, fbx });
            Console.WriteLine($"PASS {id}: distinct weapon root, full/selective CAST, animation CAST, SMD, FBX exported.");
        }
        File.WriteAllText(Path.Combine(outputDirectory, "weapon-regression.json"), JsonSerializer.Serialize(reports, new JsonSerializerOptions { WriteIndented = true }));
    }

    private static void Validate(string path)
    {
        var root = CastReader.Load(path).RootNodes.Single();
        var model = root.Children.OfType<ModelNode>().Single();
        var bones = model.Skeleton!.Bones;
        var gun = Array.FindIndex(bones, b => b.Name == "j_gun__weapon");
        var helper = Array.FindIndex(bones, b => b.Name == "j_gun");
        Check(gun >= 0 && helper >= 0 && gun != helper, "Missing distinct weapon/helper bones.");
        Check(bones[bones[gun].ParentIndex].Name == "tag_weapon", "Weapon is attached to wrist instead of tag_weapon.");
        Check(bones[gun].LocalPosition.Length() < 1e-6f, "Weapon root bind position must be zero.");
        Check(bones[bones[helper].ParentIndex].Name == "j_wrist_ri", "Wrist helper parent changed.");
        foreach (var mesh in model.Meshes.Skip(3))
            Check(!mesh.EnumerateBoneWeights().Any(w => w.Item1 == helper && w.Item2 > 0), "Weapon skin points to wrist helper.");
        var animation = root.Children.OfType<AnimationNode>().Single();
        foreach (var curve in animation.Curves.Where(c => c.NodeName == "j_gun__weapon"))
        {
            if (curve.KeyPropertyName is "tx" or "ty" or "tz")
                Check(((CastArrayProperty<float>)curve.KeyValueBuffer!).Values.All(v => Math.Abs(v) < 1e-6f), "Wrist translation leaked into weapon root.");
            else if (curve.KeyPropertyName == "rq")
                Check(((CastArrayProperty<Vector4>)curve.KeyValueBuffer!).Values.All(v => Vector4.Distance(v, Vector4.UnitW) < 1e-6f), "Wrist rotation leaked into weapon root.");
        }
    }

    private static void Check(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
