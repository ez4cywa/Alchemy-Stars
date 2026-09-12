using Cast.NET;
using Cast.NET.Nodes;
using System.Numerics;

namespace AlchemyStars.Avalonia;

internal static class CastAxisSmoke
{
    internal static void Run(string directory, bool includeFbx = false)
    {
        var folder = Path.Combine(directory, "axis");
        Directory.CreateDirectory(folder);
        foreach (var axis in new string?[] { "y", "z", "x", null, "unknown" })
        {
            var source = Path.Combine(folder, (axis ?? "missing") + ".cast");
            Write(source, axis);
            AddGeometry(source);
            var original = File.ReadAllBytes(source);
            var zFrames = new List<CastPreviewFrame>();
            foreach (var outputAxis in new[] { "z", "y" })
            {
                var request = Request(source, folder, "out-" + (axis ?? "missing") + outputAxis);
                request = request with { Options = request.Options with { OutputUpAxis = outputAxis } };
                var output = new AnimationExportEngine().Export(request).OutputFiles.Single();
                var outputCast = CastReader.Load(output);
                var metadata = outputCast.RootNodes.SelectMany(root => root.Children).OfType<MetadataNode>().SingleOrDefault();
                Require(metadata?.UpAxis == outputAxis, "Output CAST axis does not match the selection: " + axis);
                var expected = axis switch { "z" => new Vector3(1, 2, 3), "x" => new Vector3(-3, 2, 1), _ => new Vector3(1, -3, 2) };
                if (outputAxis == "y") expected = new(expected.X, expected.Z, -expected.Y);
                var preview = CastPreviewScene.Load(output);
                Require(Vector3.Distance(preview.Skeletons.Single().Bones[0].BaseLocalTranslation, expected) < 1e-5f,
                    "Bind position was not converted: " + axis);
                preview.Sample(0);
                Require(Vector3.Distance(preview.Skeletons.Single().Bones[0].LocalTranslation, expected) < 1e-5f,
                    "Animation position was not converted: " + axis);
                var model = outputCast.RootNodes.SelectMany(root => root.Children).OfType<ModelNode>().Single();
                Require(Vector3.Distance(model.Meshes.Single().VertexPositionBuffer.Values[0], expected) < 1e-5f,
                    "Mesh positions were not converted: " + axis);
                var expectedNormal = Transform(Vector3.UnitY, axis, outputAxis);
                Require(Vector3.Distance(model.Meshes.Single().VertexNormalBuffer!.Values[0], expectedNormal) < 1e-5f,
                    "Mesh normals were not converted: " + axis);
                Require(Vector3.Distance(model.Meshes.Single().VertexTangentBuffer!.Values[0], Transform(Vector3.UnitX, axis, outputAxis)) < 1e-5f,
                    "Mesh tangents were not converted: " + axis);
                var q = Rotation;
                var vector = Transform(new(q.X, q.Y, q.Z), axis, outputAxis);
                var expectedRotation = new Quaternion(vector, q.W);
                Require(1 - MathF.Abs(Quaternion.Dot(model.Skeleton!.Bones[0].LocalRotation, expectedRotation)) < 1e-5f,
                    "Bind quaternion was not basis-converted: " + axis);
                Require(1 - MathF.Abs(Quaternion.Dot(preview.Skeletons.Single().Bones[0].LocalRotation, expectedRotation)) < 1e-5f,
                    "Animation quaternion was not basis-converted: " + axis);
                var scale = Vector3.Abs(Transform(new(2, 3, 4), axis, outputAxis));
                Require(Vector3.Distance(model.Skeleton.Bones[0].Scale, scale) < 1e-5f, "Bind scale axes were not permuted.");
                foreach (var camera in new[] { PreviewCamera.Default, PreviewCamera.FirstPerson })
                {
                    var frame = CastPreviewRenderer.Prepare(preview, 0, 640, 400, camera, true);
                    if (outputAxis == "z") zFrames.Add(frame);
                    else
                    {
                        var reference = zFrames[camera.Mode == PreviewCameraMode.Orbit ? 0 : 1];
                        Require(frame.TrianglePoints.Length == reference.TrianglePoints.Length && frame.TrianglePoints.Length > 0,
                            $"Axis-aware preview lost geometry: {axis}/{camera.Mode} {frame.TrianglePoints.Length}/{reference.TrianglePoints.Length}.");
                        for (var i = 0; i < frame.TrianglePoints.Length; i++)
                            Require(Vector2.Distance(frame.TrianglePoints[i], reference.TrianglePoints[i]) < 0.01f,
                                "Changing output axis changes the preview orientation.");
                    }
                }
                foreach (var format in new[] { ExportFormat.Cast, ExportFormat.Smd, ExportFormat.Seanim })
                {
                    var other = request with { Options = request.Options with { Format = format, CastAnimationOnly = true } };
                    var file = new AnimationExportEngine().Export(other).OutputFiles.Single();
                    if (format == ExportFormat.Cast)
                    {
                        Require(CastReader.Load(file).RootNodes.SelectMany(root => root.Children).OfType<MetadataNode>().Single().UpAxis == outputAxis,
                            "Animation-only CAST lost the output axis.");
                        var scene = CastPreviewScene.Load(file, request.Parts);
                        scene.Sample(0);
                        Require(Vector3.Distance(scene.Skeletons.Single().Bones[0].LocalTranslation, expected) < 1e-5f,
                            "Animation-only preview uses a different basis from its skeleton.");
                    }
                    else if (format == ExportFormat.Smd)
                    {
                        var pose = File.ReadLines(file).SkipWhile(line => line != "time 0").Skip(1).First().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                        var position = new Vector3(float.Parse(pose[1], System.Globalization.CultureInfo.InvariantCulture),
                            float.Parse(pose[2], System.Globalization.CultureInfo.InvariantCulture), float.Parse(pose[3], System.Globalization.CultureInfo.InvariantCulture));
                        Require(Vector3.Distance(position, expected) < 1e-5f, "SMD position ignores output axis.");
                    }
                    else
                    {
                        var scene = new RedFox.Graphics3D.Graphics3DScene();
                        using var stream = File.OpenRead(file);
                        new RedFox.Graphics3D.SEAnim.SEAnimTranslator().Read(stream, file, scene);
                        var clip = scene.EnumerateObjectsOfType<RedFox.Graphics3D.Skeletal.SkeletonAnimation>().Single();
                        Require(Vector3.Distance(clip.Targets[0].SampleTranslation(0), expected) < 1e-5f, "SEAnim ignores output axis.");
                    }
                }
                Require(original.SequenceEqual(File.ReadAllBytes(source)), "Axis export changed the input.");
            }
        }
        var y = Path.Combine(folder, "y.cast");
        var z = Path.Combine(folder, "z.cast");
        var weapon = CastReader.Load(z);
        weapon.RootNodes.SelectMany(root => root.Children).OfType<ModelNode>().Single().Skeleton!.Bones[0].AddString("n", "weapon_root");
        var weaponPath = Path.Combine(folder, "weapon.cast");
        CastWriter.Save(weaponPath, weapon);
        var mixed = new AnimationExportEngine().Export(Request(y, folder, "mixed-output") with
        {
            Parts = [new(y, ModelPartKind.ViewHands), new(weaponPath, ModelPartKind.Weapon, "tag_origin")]
        });
        Require(File.Exists(mixed.OutputFiles.Single()), "Mixed model axes must be accepted.");
        var store = new WorkspaceProjectStore();
        var project = WorkspaceDocument.Create();
        Require(project.OutputUpAxis == "source", "Unspecified projects must keep the original scene axis.");
        project.OutputUpAxis = "y";
        var projectPath = Path.Combine(folder, "axis.aprj");
        store.Save(project, projectPath);
        Require(store.Load(projectPath).OutputUpAxis == "y" && WorkspaceProjectStore.Snapshot(project).OutputUpAxis == "y"
            && store.CreateExportRequest(project).Options.OutputUpAxis == "y", "Project axis was not persisted/snapshotted.");
        var preferencesPath = Path.Combine(folder, "preferences.json");
        new ApplicationPreferencesStore(preferencesPath).SaveDefaults("system", project);
        Require(new ApplicationPreferencesStore(preferencesPath).CreateWorkspace().OutputUpAxis == "y", "Default axis was not persisted.");
        project.OutputUpAxis = "";
        store.Save(project, projectPath);
        new ApplicationPreferencesStore(preferencesPath).SaveDefaults("system", project);
        Require(store.Load(projectPath).OutputUpAxis == "source" && new ApplicationPreferencesStore(preferencesPath).CreateWorkspace().OutputUpAxis == "source",
            "Keep-scene selection was not persisted.");
        var oldProject = Path.Combine(folder, "old-axis.aprj");
        File.WriteAllText(oldProject, "{}");
        Require(store.Load(oldProject).OutputUpAxis == "source", "A project without an axis must not force conversion.");
        VerifySourceAxis(folder);
        VerifyMismatchedSourceMetadata(folder);
        VerifyDual(folder);
        if (includeFbx)
        {
            foreach (var outputAxis in new[] { "z", "y" })
            {
                var request = Request(y, folder, "fbx-" + outputAxis);
                request = request with { Options = request.Options with { Format = ExportFormat.Fbx, OutputUpAxis = outputAxis } };
                var file = new AnimationExportEngine().Export(request).OutputFiles.Single();
                Require(new FileInfo(file).Length > 100, "FBX was not generated.");
                Console.WriteLine("FBX axis output: " + file);
            }
            foreach (var sourceAxis in Environment.GetEnvironmentVariable("ALCHEMY_STARS_FBX_BACKEND") == "maya" ? new[] { "y", "z" } : new[] { "x", "y", "z" })
            {
                var request = Request(Path.Combine(folder, sourceAxis + ".cast"), folder, "fbx-keep-" + sourceAxis);
                var file = new AnimationExportEngine().Export(request with { Options = request.Options with { Format = ExportFormat.Fbx } }).OutputFiles.Single();
                Require(new FileInfo(file).Length > 100, "Keep-scene FBX was not generated.");
                Console.WriteLine("Keep-scene FBX: " + file);
            }
        }
        Console.WriteLine("CAST axis: selectable Y/Z, X/Y/Z/missing/unknown inputs, bind/animation/mesh/normal/tangent conversion, mixed inputs, CAST/SMD/SEAnim and project/default persistence PASS");
    }

    private static void VerifySourceAxis(string folder)
    {
        foreach (var axis in new[] { "y", "z", "x", "missing", "unknown" })
        {
            var source = Path.Combine(folder, axis + ".cast");
            var request = Request(source, folder, "keep-" + axis);
            Require(request.Options.OutputUpAxis == "source", "Engine default must keep the scene axis.");
            var result = new AnimationExportEngine().Export(request);
            var nodes = CastReader.Load(result.OutputFiles.Single()).RootNodes.Single().Children;
            var expectedAxis = axis is "x" or "z" ? axis : "y";
            Require(nodes.OfType<MetadataNode>().Single().UpAxis == expectedAxis, "Original scene axis was not preserved: " + axis);
            var model = nodes.OfType<ModelNode>().Single();
            Require(Vector3.Distance(model.Skeleton!.Bones[0].LocalPosition, new(1, 2, 3)) < 1e-5f
                && Vector3.Distance(model.Meshes.Single().VertexPositionBuffer.Values[0], new(1, 2, 3)) < 1e-5f,
                "Keep-scene mode rotated original model coordinates: " + axis);
            var scene = CastPreviewScene.Load(result.OutputFiles.Single());
            scene.Sample(1);
            Require(Vector3.Distance(scene.Skeletons.Single().Bones[0].LocalTranslation, new(2, 2, 3)) < 1e-5f,
                "Keep-scene mode rotated original animation coordinates: " + axis);
            var animationOnly = new AnimationExportEngine().Export(request with { Options = request.Options with { CastAnimationOnly = true } });
            Require(CastReader.Load(animationOnly.OutputFiles.Single()).RootNodes.Single().Children.OfType<MetadataNode>().Single().UpAxis == expectedAxis,
                "Animation-only CAST did not preserve the scene axis.");
        }
        var hands = Path.Combine(folder, "y.cast"); var weapon = Path.Combine(folder, "weapon.cast");
        var mixed = Request(hands, folder, "keep-mixed") with
        {
            Parts = [new(weapon, ModelPartKind.Weapon, "tag_origin"), new(hands, ModelPartKind.ViewHands)],
        };
        var output = new AnimationExportEngine().Export(mixed).OutputFiles.Single();
        Require(CastReader.Load(output).RootNodes.Single().Children.OfType<MetadataNode>().Single().UpAxis == "y",
            "Keep-scene mode must use the primary arms model, not the UI insertion order.");
        Console.WriteLine("Keep scene axis: X/Y/Z/missing, unchanged coordinates, animation-only metadata, primary-model priority PASS");
    }

    private static void VerifyMismatchedSourceMetadata(string folder)
    {
        var hands = Path.Combine(folder, "y.cast");
        var animation = Path.Combine(folder, "z.cast");
        var request = Request(hands, folder, "keep-mismatched-metadata") with
        {
            Animations = [new(animation, "keep-mismatched-metadata", folder, EnableLeftHandIk: false, EnableRightHandIk: false)],
        };
        var output = new AnimationExportEngine().Export(request).OutputFiles.Single();
        var scene = CastPreviewScene.Load(output);
        scene.Sample(1);
        Require(Vector3.Distance(scene.Skeletons.Single().Bones[0].LocalTranslation, new(2, 2, 3)) < 1e-5f,
            "Keep-scene mode trusted mismatched metadata and rotated raw animation coordinates.");
        Require(CastReader.Load(output).RootNodes.Single().Children.OfType<MetadataNode>().Single().UpAxis == "y",
            "Keep-scene mode did not retain the primary model metadata marker.");
        Console.WriteLine("Keep scene axis: mismatched model/animation metadata preserves raw coordinates PASS");
    }

    private static void VerifyDual(string folder)
    {
        var handsPath = Path.Combine(folder, "dual-hands.cast");
        var weaponPath = Path.Combine(folder, "dual-weapon.cast");
        Write(handsPath, "y"); Write(weaponPath, "z");
        var hands = CastReader.Load(handsPath);
        var skeleton = hands.RootNodes.Single().Children.OfType<ModelNode>().Single().Skeleton!;
        ulong hash = 200;
        foreach (var (name, position) in new[] { ("tag_weapon", new Vector3(0, 2, 1)), ("tag_left", new Vector3(1, 0, 2)), ("tag_right", new Vector3(-1, 0, 2)) })
        {
            var bone = new BoneNode { Hash = hash++, Parent = skeleton };
            bone.AddString("n", name); bone.AddValue("p", (uint)0);
            bone.AddValue("lp", position); bone.AddValue("wp", position + new Vector3(1, 2, 3));
            bone.AddValue("lr", new Vector4(0, 0, 0, 1)); bone.AddValue("wr", new Vector4(0, 0, 0, 1));
        }
        CastWriter.Save(handsPath, hands);
        var weapon = CastReader.Load(weaponPath);
        weapon.RootNodes.Single().Children.OfType<ModelNode>().Single().Skeleton!.Bones[0].AddString("n", "weapon_root");
        CastWriter.Save(weaponPath, weapon);
        var beforeHands = File.ReadAllBytes(handsPath); var beforeWeapon = File.ReadAllBytes(weaponPath);
        var project = WorkspaceDocument.Create();
        project.Parts.Add(new() { FilePath = handsPath, Type = ModelPartKind.ViewHands });
        project.Parts.Add(new() { FilePath = weaponPath, Type = ModelPartKind.Weapon });
        foreach (var side in new[] { "left", "right" })
            project.Animations.Add(new() { Name = handsPath, OutputName = side, EnableLeftHandIK = false, EnableRightHandIK = false });
        var task = new WorkspaceDualAnimation
        {
            Name = "dual-z",
            LeftAnimationId = project.Animations[0].Id,
            RightAnimationId = project.Animations[1].Id,
            SourceMount = "tag_weapon",
            LeftMount = "tag_left",
            RightMount = "tag_right",
            OutputFolder = folder,
            ExportWeaponModels = true
        };
        var engine = new DualWieldEngine();
        project.OutputUpAxis = "z";
        var z = engine.Export(project, task);
        project.OutputUpAxis = "y"; task.Name = "dual-y";
        var y = engine.Export(project, task);
        project.OutputUpAxis = "source"; task.Name = "dual-keep";
        var kept = engine.Export(project, task);
        foreach (var file in kept.OutputFiles)
            Require(CastReader.Load(file).RootNodes.Single().Children.OfType<MetadataNode>().Single().UpAxis == "y",
                "Dual keep-scene output did not follow the primary hands model.");
        foreach (var (result, axis) in new[] { (z, "z"), (y, "y") })
            foreach (var file in result.OutputFiles)
                Require(CastReader.Load(file).RootNodes.Single().Children.OfType<MetadataNode>().Single().UpAxis == axis,
                    "Dual animation/companion model axis differs from selection.");
        var zScene = CastPreviewScene.Load(z.OutputFile); var yScene = CastPreviewScene.Load(y.OutputFile);
        for (var frame = 0; frame < zScene.FrameCount; frame++)
        {
            zScene.Sample(frame); yScene.Sample(frame);
            var zBones = zScene.Skeletons.Single().Bones; var yBones = yScene.Skeletons.Single().Bones;
            Require(zBones.Count == yBones.Count, "Dual axis conversion changed topology.");
            for (var i = 0; i < zBones.Count; i++)
                Require(Vector3.Distance(Transform(zBones[i].WorldTranslation, "z", "y"), yBones[i].WorldTranslation) < 1e-4f,
                    "Dual world motion is not invariant under axis selection: " + zBones[i].Name);
        }
        Require(beforeHands.SequenceEqual(File.ReadAllBytes(handsPath)) && beforeWeapon.SequenceEqual(File.ReadAllBytes(weaponPath)),
            "Dual axis conversion modified an input.");
        Console.WriteLine("Dual axes: mixed Y/Z input, selected Y/Z output, companion model and world motion equivalence PASS");
    }

    private static Quaternion Rotation => Quaternion.CreateFromYawPitchRoll(0.2f, 0.4f, -0.3f);

    private static Vector3 Transform(Vector3 value, string? source, string destination)
    {
        // Independent quaternion oracle for the production component permutation.
        var toZ = source switch
        {
            "z" => Quaternion.Identity,
            "x" => Quaternion.CreateFromAxisAngle(Vector3.UnitY, -MathF.PI / 2),
            _ => Quaternion.CreateFromAxisAngle(Vector3.UnitX, MathF.PI / 2),
        };
        var result = Vector3.Transform(value, toZ);
        return destination == "y" ? Vector3.Transform(result, Quaternion.CreateFromAxisAngle(Vector3.UnitX, -MathF.PI / 2)) : result;
    }

    private static void AddGeometry(string path)
    {
        var cast = CastReader.Load(path);
        var root = cast.RootNodes.Single();
        var model = root.Children.OfType<ModelNode>().Single();
        var q = Rotation;
        var rotation = new Vector4(q.X, q.Y, q.Z, q.W);
        var bone = model.Skeleton!.Bones[0];
        bone.AddValue("lr", rotation); bone.AddValue("wr", rotation); bone.AddValue("s", new Vector3(2, 3, 4));
        var curve = root.Children.OfType<AnimationNode>().Single().Children.OfType<CurveNode>().Single(c => c.KeyPropertyName == "rq");
        curve.KeyValueBuffer = new CastArrayProperty<Vector4>([rotation, rotation]);
        var mesh = new MeshNode { Hash = 100, Parent = model };
        var material = new MaterialNode { Hash = 101, Parent = model };
        material.AddString("n", "axis_material");
        material.AddString("t", "pbr");
        mesh.AddValue("m", (ulong)101);
        mesh.AddString("n", "axis_triangle");
        mesh.Properties["vp"] = new CastArrayProperty<Vector3>([new(1, 2, 3), new(1.25f, 2, 3), new(1, 2, 3.25f)]);
        mesh.Properties["vn"] = new CastArrayProperty<Vector3>([Vector3.UnitY, Vector3.UnitY, Vector3.UnitY]);
        mesh.Properties["vt"] = new CastArrayProperty<Vector3>([Vector3.UnitX, Vector3.UnitX, Vector3.UnitX]);
        mesh.Properties["f"] = new CastArrayProperty<byte>([0, 1, 2]);
        mesh.Properties["wb"] = new CastArrayProperty<byte>([0, 0, 0]);
        mesh.Properties["wv"] = new CastArrayProperty<float>([1, 1, 1]);
        mesh.AddValue("mi", (byte)1);
        CastWriter.Save(path, cast);
    }

    private static AnimationExportRequest Request(string source, string folder, string name) => new(
        [new(source, ModelPartKind.ViewHands)],
        [new(source, name, folder, EnableLeftHandIk: false, EnableRightHandIk: false)],
        new(new("", "", "", ""), new("", "", "", "")));

    internal static void Write(string path, string? axis)
    {
        var root = new CastNode(CastNodeIdentifier.Root) { Hash = 1 };
        if (axis is not null)
        {
            var metadata = new CastNode(CastNodeIdentifier.Metadata) { Hash = 2, Parent = root };
            metadata.AddString("up", axis);
        }
        var model = new ModelNode { Hash = 3, Parent = root };
        var skeleton = new SkeletonNode { Hash = 4, Parent = model };
        var bone = new BoneNode { Hash = 5, Parent = skeleton };
        bone.AddString("n", "tag_origin");
        bone.AddValue("p", uint.MaxValue);
        bone.AddValue("lp", new Vector3(1, 2, 3));
        bone.AddValue("wp", new Vector3(1, 2, 3));
        bone.AddValue("lr", new Vector4(0, 0, 0, 1));
        bone.AddValue("wr", new Vector4(0, 0, 0, 1));
        var animation = new AnimationNode { Hash = 6, Parent = root };
        animation.AddValue("fr", 30f);
        ulong hash = 7;
        foreach (var (channel, values) in new[] { ("tx", new float[] { 1, 2 }), ("ty", new float[] { 2, 2 }), ("tz", new float[] { 3, 3 }) })
        {
            _ = new CurveNode
            {
                Hash = hash++,
                Parent = animation,
                NodeName = "tag_origin",
                KeyPropertyName = channel,
                Mode = "absolute",
                KeyFrameBuffer = new CastArrayProperty<byte>([0, 1]),
                KeyValueBuffer = new CastArrayProperty<float>(values),
            };
        }
        _ = new CurveNode
        {
            Hash = hash,
            Parent = animation,
            NodeName = "tag_origin",
            KeyPropertyName = "rq",
            Mode = "absolute",
            KeyFrameBuffer = new CastArrayProperty<byte>([0, 1]),
            KeyValueBuffer = new CastArrayProperty<Vector4>([new(0, 0, 0, 1), new(0, 0, 0, 1)]),
        };
        CastWriter.Save(path, new Cast.NET.Cast([root]));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
