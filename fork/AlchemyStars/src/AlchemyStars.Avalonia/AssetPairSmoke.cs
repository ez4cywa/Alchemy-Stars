using System.Security.Cryptography;
using Cast.NET;
using Cast.NET.Nodes;
using RedFox.Graphics3D;
using RedFox.Graphics3D.Skeletal;
using RedFox.Graphics3D.Translation;

namespace AlchemyStars.Avalonia;

/// <summary>
/// End-to-end compatibility check for one arms + weapon pair on both the
/// animation-blend pipeline (the Animations page) and the attached dual-wield
/// pipeline (the Dual page, one hands model plus one weapon model duplicated to
/// both mounts). Real production assets are large, so the paths are supplied on
/// the command line instead of being committed as fixtures.
/// </summary>
internal static class AssetPairSmoke
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length != 4)
                throw new ArgumentException("--asset-pair-smoke <hands.cast> <weapon.cast> <animation.cast> <output folder>");
            var hands = Path.GetFullPath(args[0]);
            var weapon = Path.GetFullPath(args[1]);
            var animation = Path.GetFullPath(args[2]);
            var output = Path.GetFullPath(args[3]);
            foreach (var input in new[] { hands, weapon, animation })
                if (!File.Exists(input)) throw new FileNotFoundException("Input asset is missing", input);
            Directory.CreateDirectory(output);
            foreach (var input in new[] { hands, weapon, animation })
                if (output.Equals(input, StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException("Output folder must not be an input asset.");

            var hashes = new[] { hands, weapon, animation }.ToDictionary(path => path, path => SHA256.HashData(File.ReadAllBytes(path)));
            var translator = new Graphics3DTranslatorFactory().WithDefaultTranslators();
            var sourceClip = translator.Load<SkeletonAnimation>(animation);
            // The RedFox CAST translator does not populate Framerate; read the
            // same CAST metadata used by the production resampler.
            sourceClip.Framerate = CastReader.Load(animation).RootNodes.SelectMany(Walk).OfType<AnimationNode>().Single().Framerate;
            var sourceFrames = Math.Max(1, (int)MathF.Ceiling(sourceClip.GetAnimationFrameCount()));
            Require(float.IsFinite(sourceClip.Framerate) && sourceClip.Framerate > 0, "Source frame rate must be positive.");
            // Both default pipelines resample to 30 FPS. Compare the resulting
            // duration/frame grid, not the source frame count (e.g. 61 @ 60 -> 31 @ 30).
            var expectedFrames = Math.Max(1, (int)MathF.Ceiling((sourceFrames - 1)
                * WorkspacePaths.StandardAnimationFramerate / sourceClip.Framerate) + 1);

            var handsModel = Model(hands);
            var weaponModel = Model(weapon);
            var handsVertices = VertexCount(handsModel);
            var weaponVertices = VertexCount(weaponModel);
            var handsBones = handsModel.Skeleton!.Bones.Length;
            var weaponBones = weaponModel.Skeleton!.Bones.Length;
            var weaponRoot = weaponModel.Skeleton.Bones.Single(bone => bone.ParentIndex < 0).Name;
            Console.WriteLine($"Hands: {handsVertices} vertices, {handsBones} bones ({Path.GetFileName(hands)})");
            Console.WriteLine($"Weapon: {weaponVertices} vertices, {weaponBones} bones, root '{weaponRoot}' ({Path.GetFileName(weapon)})");
            Console.WriteLine($"Animation: {sourceFrames} frames @ {sourceClip.Framerate} FPS -> {expectedFrames} @ 30 FPS ({Path.GetFileName(animation)})");

            var handsClassification = ModelPartClassifier.Classify(hands);
            var weaponClassification = ModelPartClassifier.Classify(weapon);
            Require(handsClassification.Kind == ModelPartKind.ViewHands,
                $"the hands asset must classify as ViewHands, saw {handsClassification.Kind}.");
            Require(weaponClassification.Kind == ModelPartKind.Weapon,
                $"the weapon asset must classify as Weapon, saw {weaponClassification.Kind}.");
            Console.WriteLine($"Classification: hands={handsClassification.Kind} ({handsClassification.Confidence:P0}), "
                + $"weapon={weaponClassification.Kind} ({weaponClassification.Confidence:P0}), mount '{weaponClassification.RecommendedParentBone}'");

            var store = new WorkspaceProjectStore();
            var mount = string.IsNullOrWhiteSpace(weaponClassification.RecommendedParentBone) ? "tag_weapon" : weaponClassification.RecommendedParentBone;
            var blendFrames = VerifyBlendPipeline(store, hands, weapon, animation, output, mount,
                handsClassification.Kind, weaponClassification.Kind,
                handsVertices + weaponVertices, handsBones + weaponBones, expectedFrames);
            var dualFrames = VerifyAttachedDualPipeline(hands, weapon, animation, output, mount,
                handsClassification.Kind, weaponClassification.Kind,
                handsVertices + 2 * weaponVertices, expectedFrames);

            Require(dualFrames == blendFrames,
                $"both pipelines must bake the same clip length, blend {blendFrames} vs dual {dualFrames}.");

            foreach (var (path, hash) in hashes)
                Require(hash.SequenceEqual(SHA256.HashData(File.ReadAllBytes(path))), $"Source asset was modified: {path}");

            Console.WriteLine("Asset pair: blend export, attached dual export, geometry/rig conservation, mount resolution and source integrity PASS");
            return 0;
        }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            return 1;
        }
    }

    /// <summary>Merges the pair the way the Animations page does and checks the baked scene.</summary>
    private static int VerifyBlendPipeline(WorkspaceProjectStore store, string hands, string weapon, string animation,
        string output, string mount, ModelPartKind handsKind, ModelPartKind weaponKind,
        int expectedVertices, int expectedBones, int expectedFrames)
    {
        var document = new WorkspaceDocument { OutputFormat = ".cast" };
        document.Parts.Add(new WorkspacePart { FilePath = hands, Type = handsKind });
        document.Parts.Add(new WorkspacePart { FilePath = weapon, Type = weaponKind, ParentBoneTag = mount });
        document.Animations.Add(new WorkspaceAnimation
        {
            Name = animation,
            OutputName = "asset_pair_blend",
            OutputFolder = output,
            EnableLeftHandIK = false,
            EnableRightHandIK = false,
        });

        var result = new AnimationExportEngine().Export(store.CreateExportRequest(document));
        Require(result.OutputFiles.Count == 1, $"the blend export must produce one file, saw {result.OutputFiles.Count}.");
        var path = Path.GetFullPath(result.OutputFiles[0]);
        Require(File.Exists(path) && new FileInfo(path).Length > 0, "the blend export is missing or empty.");

        var merged = Model(path);
        var vertices = VertexCount(merged);
        var bones = merged.Skeleton!.Bones.Length;
        Require(vertices == expectedVertices, $"the blend merge must keep every vertex, expected {expectedVertices}, saw {vertices}.");
        Require(bones == expectedBones, $"the blend merge must keep every bone, expected {expectedBones}, saw {bones}.");
        var names = merged.Skeleton.Bones.Select(bone => bone.Name).ToArray();
        Require(names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length,
            "the blend merge produced duplicate bone names.");
        Require(merged.Skeleton.Bones.Count(bone => bone.ParentIndex < 0) == 1, "the blend merge produced a disconnected rig.");

        var preview = CastPreviewScene.Load(path);
        var frames = preview.FrameCount;
        Require(frames == expectedFrames && preview.Framerate == WorkspacePaths.StandardAnimationFramerate,
            $"the blend bake changed duration, expected {expectedFrames} @ 30 FPS, output {frames} @ {preview.Framerate}.");
        Console.WriteLine($"Blend: {vertices} vertices, {bones} bones, {frames} frames -> {Path.GetFileName(path)}");
        VerifyWeaponMount(merged, "blend", mount);
        return frames;
    }

    /// <summary>Duplicates the weapon onto both mounts the way the Dual page does.</summary>
    private static int VerifyAttachedDualPipeline(string hands, string weapon, string animation,
        string output, string mount, ModelPartKind handsKind, ModelPartKind weaponKind,
        int expectedVertices, int expectedFrames)
    {
        var document = new WorkspaceDocument { OutputFormat = ".cast" };
        document.Parts.Add(new WorkspacePart { FilePath = hands, Type = handsKind });
        document.Parts.Add(new WorkspacePart { FilePath = weapon, Type = weaponKind, ParentBoneTag = mount });
        var left = new WorkspaceAnimation { Name = animation, EnableLeftHandIK = false, EnableRightHandIK = false };
        var right = new WorkspaceAnimation { Name = animation, EnableLeftHandIK = false, EnableRightHandIK = false };
        document.Animations.Add(left);
        document.Animations.Add(right);
        var task = new WorkspaceDualAnimation
        {
            Name = "asset_pair_dual",
            Mode = DualModelMode.Attached,
            LeftAnimationId = left.Id,
            RightAnimationId = right.Id,
            OutputFolder = output,
            SourceMount = mount,
            ExportWeaponModels = true,
        };
        document.DualAnimations.Add(task);

        var result = new DualWieldEngine().Export(document, task);
        Require(File.Exists(result.OutputFile) && new FileInfo(result.OutputFile).Length > 0, "the dual export is missing or empty.");
        Require(result.ModelFile is not null && File.Exists(result.ModelFile), "the dual companion model is missing.");

        // The animated deliverable is animation-only; the duplicated geometry and rig
        // live in the companion bound model, which is where conservation must hold.
        var merged = Model(result.ModelFile!);
        var vertices = VertexCount(merged);
        var names = merged.Skeleton!.Bones.Select(bone => bone.Name).ToArray();
        // Unresolved extractor hashes and bones that only exist in the full player rig
        // can never map onto a viewmodel skeleton. Anything that DOES exist in the
        // merged rig but stayed unmapped is a real defect.
        var mapable = result.UnmappedTargets
            .Where(target => names.Contains(target, StringComparer.OrdinalIgnoreCase)).ToArray();
        Require(mapable.Length == 0,
            $"the dual bake left targets unmapped that exist in the merged rig: {string.Join(", ", mapable)}.");
        if (result.UnmappedTargets.Count > 0)
            Console.WriteLine($"  unmapped source targets (absent from the viewmodel rig): {string.Join(", ", result.UnmappedTargets)}");
        Require(vertices == expectedVertices, $"the dual merge must keep one hands and two weapon copies, expected {expectedVertices}, saw {vertices}.");
        Require(names.Distinct(StringComparer.OrdinalIgnoreCase).Count() == names.Length, "the dual merge produced duplicate bone names.");
        Require(merged.Skeleton.Bones.Count(bone => bone.ParentIndex < 0) == 1, "the dual merge produced a disconnected rig.");
        var outputRate = CastReader.Load(result.OutputFile).RootNodes.SelectMany(Walk).OfType<AnimationNode>().Single().Framerate;
        Require(result.FrameCount == expectedFrames && outputRate == WorkspacePaths.StandardAnimationFramerate,
            $"the dual bake changed duration, expected {expectedFrames} @ 30 FPS, output {result.FrameCount}.");
        Require(!CastReader.Load(result.ModelFile!).RootNodes.SelectMany(Walk).OfType<AnimationNode>().Any(),
            "the dual companion model must not contain animation.");
        Require(!CastReader.Load(result.OutputFile).RootNodes.SelectMany(Walk).OfType<ModelNode>().Any(),
            "the dual animation deliverable must not embed the model.");
        Console.WriteLine($"Dual: {vertices} vertices, {names.Length} bones, {result.FrameCount} frames -> "
            + $"{Path.GetFileName(result.OutputFile)} + {Path.GetFileName(result.ModelFile!)}");
        // Attached mode keeps the source mount for the original clip and mounts the two
        // duplicated copies on the dedicated left/right mount bones.
        VerifyWeaponMount(merged, "dual", task.LeftMount, task.RightMount);

        // The weapon is duplicated, so both hands must carry a distinct weapon copy.
        var leftSuffix = names.Count(name => name.EndsWith("__left", StringComparison.OrdinalIgnoreCase));
        var rightSuffix = names.Count(name => name.EndsWith("__right", StringComparison.OrdinalIgnoreCase));
        Require(leftSuffix > 0 && leftSuffix == rightSuffix,
            $"both hands must carry the same weapon branch, saw {leftSuffix} left and {rightSuffix} right bones.");
        return result.FrameCount;
    }

    /// <summary>Every weapon copy must end up parented to a mount bone, not orphaned or renamed away.</summary>
    private static void VerifyWeaponMount(ModelNode merged, string stage, params string[] mounts)
    {
        foreach (var mount in mounts)
        {
            var mountIndex = Array.FindIndex(merged.Skeleton!.Bones, bone => bone.Name.Equals(mount, StringComparison.OrdinalIgnoreCase));
            Require(mountIndex >= 0, $"the {stage} merge lost the mount bone '{mount}'.");
            var children = merged.Skeleton.Bones.Where(bone => bone.ParentIndex == mountIndex).Select(bone => bone.Name).ToArray();
            Require(children.Length > 0, $"the {stage} merge left the mount bone '{mount}' with no weapon attached.");
            Console.WriteLine($"  mount {mount} -> {string.Join(", ", children)}");
        }
    }

    private static ModelNode Model(string path) =>
        CastReader.Load(path).RootNodes.SelectMany(Walk).OfType<ModelNode>().Single();

    private static int VertexCount(ModelNode model) => model.Meshes.Sum(mesh => mesh.VertexPositionBuffer.ValueCount);

    private static IEnumerable<CastNode> Walk(CastNode node)
    {
        yield return node;
        foreach (var child in node.Children)
            foreach (var descendant in Walk(child)) yield return descendant;
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidDataException(message);
    }
}
