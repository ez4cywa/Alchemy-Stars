using Alchemist.InverseKinematics;
using RedFox.Graphics3D;
using RedFox.Graphics3D.Skeletal;
using RedFox.Graphics3D.Translation;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Alchemist.UI
{
#if ALCHEMYSTARS_ENGINE
    internal class AnimationConverter
#else
    public class AnimationConverter
#endif
    {
        public static Graphics3DTranslatorFactory TranslatorFactory { get; set; } = new Graphics3DTranslatorFactory().WithDefaultTranslators();

        /// <summary>Loads an animation and resamples its keyframe timeline to the requested output rate.</summary>
        internal static SkeletonAnimation LoadAtStandardFramerate(string path, string outputAxis = "z", bool normalizeCoordinates = true) =>
            LoadAtFramerate(path, 30f, outputAxis, normalizeCoordinates);

        internal static SkeletonAnimation LoadAtFramerate(string path, float targetFramerate, string outputAxis = "z", bool normalizeCoordinates = true)
        {
            if (!float.IsFinite(targetFramerate) || targetFramerate <= 0)
                throw new ArgumentOutOfRangeException(nameof(targetFramerate));
            var source = TranslatorFactory.Load<SkeletonAnimation>(path);
            if (Path.GetExtension(path).Equals(".cast", StringComparison.OrdinalIgnoreCase))
            {
                var cast = Cast.NET.CastReader.Load(path);
                if (normalizeCoordinates)
                    CastCoordinateSystem.NormalizeAnimation(source, CastCoordinateSystem.ReadAxis(cast), outputAxis);
                var nodes = cast.RootNodes
                    .SelectMany(root => root.EnumerateChildrenOfType<Cast.NET.Nodes.AnimationNode>()).ToArray();
                if (nodes.Length != 1) throw new InvalidDataException("Expected one CAST animation: " + path);
                source.Framerate = nodes[0].Framerate;
            }
            if (!float.IsFinite(source.Framerate) || source.Framerate <= 0)
                throw new InvalidDataException("Invalid source animation framerate: " + path);
            var sourceRate = float.IsFinite(source.Framerate) && source.Framerate > 0
                ? source.Framerate : 30f;
            if (MathF.Abs(sourceRate - targetFramerate) < 0.0001f)
            {
                source.Framerate = targetFramerate;
                return source;
            }

            var sourceFrameCount = source.GetAnimationFrameCount();
            if (!float.IsFinite(sourceFrameCount) || sourceFrameCount <= 0)
                sourceFrameCount = 1;
            var outputFrameCount = Math.Max(1, (int)MathF.Ceiling(MathF.Max(0, sourceFrameCount - 1) * targetFramerate / sourceRate) + 1);
            var result = new SkeletonAnimation(source.Name ?? string.Empty)
            {
                Framerate = targetFramerate,
                TransformType = source.TransformType,
                TransformSpace = source.TransformSpace,
            };

            foreach (var sourceTarget in source.Targets)
            {
                var target = new SkeletonAnimationTarget(sourceTarget.BoneName)
                {
                    TransformType = sourceTarget.TransformType,
                    ChildTransformType = sourceTarget.ChildTransformType,
                };
                if (sourceTarget.TranslationFrameCount > 0)
                    for (var frame = 0; frame < outputFrameCount; frame++)
                        target.AddTranslationFrame(frame, sourceTarget.SampleTranslation(frame * sourceRate / targetFramerate));
                if (sourceTarget.RotationFrameCount > 0)
                    for (var frame = 0; frame < outputFrameCount; frame++)
                        target.AddRotationFrame(frame, Quaternion.Normalize(sourceTarget.SampleRotation(frame * sourceRate / targetFramerate)));
                if (sourceTarget.ScaleFrameCount > 0)
                    for (var frame = 0; frame < outputFrameCount; frame++)
                        target.AddScaleFrame(frame, SampleVector(sourceTarget.ScaleFrames!, frame * sourceRate / targetFramerate, Vector3.One));
                result.Targets.Add(target);
            }

            if (source.Actions is not null)
                foreach (var action in source.Actions)
                {
                    var scaled = result.CreateAction(action.Name);
                    scaled.Type = action.Type;
                    scaled.KeyFrames = action.KeyFrames.Select(frame => new AnimationKeyFrame<float, Action<Graphics3DScene>?>(
                        frame.Frame * targetFramerate / sourceRate, frame.Value)).ToList();
                }
            return result;

            static Vector3 SampleVector(List<AnimationKeyFrame<float, Vector3>> frames, float time, Vector3 fallback)
            {
                var (first, second) = AnimationKeyFrameHelper.GetFramePairIndex(frames, time, 0f);
                if (first < 0) return fallback;
                if (first == second) return frames[first].Value;
                return Vector3.Lerp(frames[first].Value, frames[second].Value,
                    (time - frames[first].Frame) / (frames[second].Frame - frames[first].Frame));
            }
        }

        internal static IKTwoBoneSolver? CreateIKSolver(string name, IKSettings settings, Skeleton skeleton, AnimationPlayer player)
        {
            Logging.Logger.Info($"Attempting to create IK solver: {name}");

            if (!skeleton.TryGetBone(settings.StartBoneName, out var startBone))
            {
                Logging.Logger.Warn($"Failed to locate IK start bone: {settings.StartBoneName} in the provided skeleton");
                return null;
            }
            if (!skeleton.TryGetBone(settings.MiddleBoneName, out var middleBone))
            {
                Logging.Logger.Warn($"Failed to locate IK middle bone: {settings.MiddleBoneName} in the provided skeleton");
                return null;
            }
            if (!skeleton.TryGetBone(settings.EndBoneName, out var endBone))
            {
                Logging.Logger.Warn($"Failed to locate IK end bone: {settings.EndBoneName} in the provided skeleton");
                return null;
            }
            if (!skeleton.TryGetBone(settings.TargetBoneName, out var targetBone))
            {
                Logging.Logger.Warn($"Failed to locate IK target bone: {settings.TargetBoneName} in the provided skeleton");
                return null;
            }

            if (!middleBone.IsDescendantOf(startBone) || !endBone.IsDescendantOf(middleBone))
            {
                Logging.Logger.Warn($"Rejected IK solver {name}: start/middle/end bones do not form a valid hierarchy.");
                return null;
            }

            if (targetBone == startBone || targetBone.IsDescendantOf(startBone))
            {
                Logging.Logger.Warn($"Rejected IK solver {name}: target {targetBone.Name} is inside the solved chain and would create a dependency cycle.");
                return null;
            }

            var solver = new IKTwoBoneSolver(name, startBone, middleBone, endBone, targetBone);
            player.Solvers.Add(solver);

            Logging.Logger.Info($"Created IK solver: {name}");

            return solver;
        }

        internal static string Convert(
            SkeletonMergePlan mergePlan,
            Animation animation,
            IKSettings lSettings,
            IKSettings rSettings,
            string prefix,
            string suffix,
            string format,
            bool castAnimationOnly,
            bool bakeRelevantBonesOnly,
            bool matchOldCallOfDuty, int weaponFollowMode = 0, string followMount = "tag_weapon")
        {
            if (string.IsNullOrEmpty(animation.OutputFolder))
                throw new ArgumentException("No output folder was provided.");
            var baked = Bake(mergePlan, animation, lSettings, rSettings, bakeRelevantBonesOnly, matchOldCallOfDuty, weaponFollowMode, followMount);
            return SaveBaked(mergePlan, baked,
                Path.Combine(animation.OutputFolder, prefix + animation.OutputName + suffix + format), format, castAnimationOnly);
        }

        internal static SkeletonAnimation Bake(
            SkeletonMergePlan mergePlan, Animation animation, IKSettings lSettings, IKSettings rSettings,
            bool bakeRelevantBonesOnly, bool matchOldCallOfDuty, int weaponFollowMode = 0, string followMount = "tag_weapon")
        {
            var skeleton = mergePlan.Skeleton;
            Logging.Logger.Info($"Attempting to convert: {animation.Name}");

            if (string.IsNullOrEmpty(animation.Name))
                throw new ArgumentException("No input file was provided to alchemist for main animation.");

            var player = new AnimationPlayer("Player");

            Logging.Logger.Info($"Attempting to load left and right pose");

            SkeletonAnimationSampler? plSampler = null;
            SkeletonAnimationSampler? prSampler = null;

            AnimationSamplerSolver? lSolver = null;
            AnimationSamplerSolver? rSolver = null;

            var targetFramerate = animation.OutputFramerate;
            var mainAnimation = LoadAtFramerate(animation.Name, targetFramerate, mergePlan.UpAxis, mergePlan.NormalizeInputAxes);
            mergePlan.BindAnimation(mainAnimation);
            var contributingAnimations = new List<SkeletonAnimation> { mainAnimation };

            if (matchOldCallOfDuty)
            {
                mainAnimation.TransformType = TransformType.Relative;
                mainAnimation.Targets.ForEach(x => x.TransformType = TransformType.Relative);
            }

            var mainSampler = new SkeletonAnimationSampler("Main", mainAnimation, skeleton, player);

            // Load optional animations
            if (!string.IsNullOrWhiteSpace(animation.LeftHandPoseFile))
            {
                Logging.Logger.Info($"Loading left hand pose: {animation.LeftHandPoseFile}");
                var leftHandPose = LoadAtFramerate(animation.LeftHandPoseFile, targetFramerate, mergePlan.UpAxis, mergePlan.NormalizeInputAxes);
                mergePlan.BindAnimation(leftHandPose);
                contributingAnimations.Add(leftHandPose);
                plSampler = new SkeletonAnimationSampler("PLLayer", leftHandPose, skeleton, player);
                Logging.Logger.Info($"Loaded left hand pose: {animation.LeftHandPoseFile}");
            }
            else
            {
                Logging.Logger.Info($"No left hand pose provided, skipping");
            }

            if (!string.IsNullOrWhiteSpace(animation.RightHandPoseFile))
            {
                Logging.Logger.Info($"Loading right hand pose: {animation.RightHandPoseFile}");
                var rightHandPose = LoadAtFramerate(animation.RightHandPoseFile, targetFramerate, mergePlan.UpAxis, mergePlan.NormalizeInputAxes);
                mergePlan.BindAnimation(rightHandPose);
                contributingAnimations.Add(rightHandPose);
                prSampler = new SkeletonAnimationSampler("PRLayer", rightHandPose, skeleton, player);
                Logging.Logger.Info($"Loaded right hand pose: {animation.RightHandPoseFile}");
            }
            else
            {
                Logging.Logger.Info($"No right hand pose provided, skipping");
            }

            // Force additive
            plSampler?.SetTransformType(TransformType.Additive);
            prSampler?.SetTransformType(TransformType.Additive);

            // CAST rotations may be slightly non-unit after quantization. Build rigid
            // transforms from normalized local rotations, as DCC importers do.
            (Vector3 Position, Quaternion Rotation) FollowWorld(SkeletonBone bone)
            {
                var rotation = Quaternion.Normalize(bone.LocalRotation);
                if (bone.Parent is null) return (bone.LocalTranslation, rotation);
                var parent = FollowWorld(bone.Parent);
                return (parent.Position + Vector3.Transform(bone.LocalTranslation, parent.Rotation),
                    Quaternion.Normalize(parent.Rotation * rotation));
            }
            if (weaponFollowMode is < 0 or > 2) throw new InvalidDataException("未知武器跟随选项 / Unknown weapon follow mode.");
            SkeletonBone? followHand = null, followAnchor = null;
            var gripPosition = Vector3.Zero;
            var gripRotation = Quaternion.Identity;
            if (weaponFollowMode != 0)
            {
                if (matchOldCallOfDuty) throw new InvalidDataException("武器跟随请关闭旧版 COD 兼容 / Disable legacy COD transforms for weapon follow.");
                var handName = weaponFollowMode == 1 ? lSettings.EndBoneName : rSettings.EndBoneName;
                followHand = skeleton.Bones.SingleOrDefault(b => b.Name == handName)
                    ?? throw new InvalidDataException("武器跟随手腕不存在 / Follow wrist is missing: " + handName);
                followAnchor = skeleton.Bones.SingleOrDefault(b => b.Name == followMount)
                    ?? throw new InvalidDataException("武器跟随挂点不存在 / Follow mount is missing: " + followMount);
                for (var ancestor = followHand; ancestor is not null; ancestor = ancestor.Parent)
                    if (ReferenceEquals(ancestor, followAnchor)) throw new InvalidDataException("武器挂点不能是跟随手腕或其祖先 / Mount cannot be the followed wrist or its ancestor.");
                bool AttachedToAnchor(SkeletonBone bone)
                {
                    for (var parent = bone.Parent; parent is not null; parent = parent.Parent)
                        if (ReferenceEquals(parent, followAnchor)) return true;
                    return false;
                }
                if (!mergePlan.Sources.Where(source => source.Type == PartType.Weapon).Any(source => source.BoneMap.Any(index => AttachedToAnchor(skeleton.Bones[index]))))
                    throw new InvalidDataException("所选武器不在跟随挂点下 / No weapon is attached below the follow mount: " + followMount);
                skeleton.InitializeAnimationTransforms();
                mainSampler.Update(0, AnimationSampleType.AbsoluteFrameTime);
                plSampler?.Update(0, AnimationSampleType.AbsoluteFrameTime);
                prSampler?.Update(0, AnimationSampleType.AbsoluteFrameTime);
                skeleton.Update();
                if (skeleton.Bones.Any(b => Vector3.DistanceSquared(b.BaseScale, Vector3.One) > 1e-8f))
                    throw new InvalidDataException("武器跟随需要单位骨骼缩放 / Weapon follow requires unit bone scale.");
                // Capture the original held pose, including its configured IK. The
                // playback solver is skipped on the following side, not this reference.
                var referencePlayer = new AnimationPlayer("GripReference");
                if (animation.EnableLeftHandIK) CreateIKSolver("GripLeft", lSettings, skeleton, referencePlayer);
                if (animation.EnableRightHandIK) CreateIKSolver("GripRight", rSettings, skeleton, referencePlayer);
                foreach (var solver in referencePlayer.Solvers) solver.Update(0);
                skeleton.Update();
                var referenceHand = FollowWorld(followHand);
                var referenceAnchor = FollowWorld(followAnchor);
                var inverse = Quaternion.Inverse(referenceHand.Rotation);
                gripPosition = Vector3.Transform(referenceAnchor.Position - referenceHand.Position, inverse);
                gripRotation = Quaternion.Normalize(inverse * referenceAnchor.Rotation);
            }

            if (animation.EnableLeftHandIK && weaponFollowMode != 1)
                lSolver = CreateIKSolver("LSolver", lSettings, skeleton, player);
            if (animation.EnableRightHandIK && weaponFollowMode != 2)
                rSolver = CreateIKSolver("RSolver", rSettings, skeleton, player);

            // Add layers
            foreach (var layer in animation.Layers)
            {
                Logging.Logger.Info($"Loading layer: {layer.Name} of type: {layer.Type}");
                var anim = LoadAtFramerate(layer.Name, targetFramerate, mergePlan.UpAxis, mergePlan.NormalizeInputAxes);
                mergePlan.BindAnimation(anim);
                contributingAnimations.Add(anim);

                switch(layer.Type)
                {
                    case AnimationLayerType.Additive:
                    case AnimationLayerType.GesturePose:
                        anim.TransformType = TransformType.Additive;
                        anim.Targets.ForEach(x => x.TransformType = TransformType.Additive);
                        break;
                }

                var startFrame = layer.Offset ?? 0;
                var layerSampler = new SkeletonAnimationSampler(layer.Type.ToString(), anim, skeleton)
                {
                    StartFrame = startFrame,
                };
                layerSampler.FrameCount = Math.Max(0, layerSampler.FrameCount + startFrame);
                if (startFrame > 0)
                {
                    layerSampler.Weights.Add(new(0, 0));
                    layerSampler.Weights.Add(new(startFrame - 1, 0));
                    layerSampler.Weights.Add(new(startFrame, 1));
                }
                player.AddLayer(layerSampler);
                Logging.Logger.Info($"Loaded layer: {layer.Name}");
            }

            // Pre-cache gesture layers
            var gestureLayers = player.Layers.FindAll(x =>
                string.Equals(x.Name, "gesture", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(x.Name, "gesturepose", StringComparison.OrdinalIgnoreCase));

            Logging.Logger.Info($"Building weights from notetracks");

            foreach (var sampler in player.Layers)
            {
                if (sampler.Animation.Actions is null)
                    continue;

                // First let's see if we need to add gesture blend in helper weight to start off at 0
                // As unlike fingers, gestures don't have a blend start notetrack
                foreach (var note in sampler.Animation.Actions)
                {
                    if (note.Name == "gesture_blend_in")
                    {
                        gestureLayers.ForEach(x => x.Weights.Add(new(0, 0)));
                    }
                }

                foreach (var note in sampler.Animation.Actions)
                {
                    switch (note.Name)
                    {
                        case "ik_out_start_left_hand":
                        case "ik_in_end_left_hand":
                            Logging.Logger.Info($"Adding Left Hand IK 1.0 from: {note.Name} ({note.KeyFrames.Count} frames)");
                            AddShiftedWeights(lSolver?.Weights, note.KeyFrames, sampler.StartFrame, 1);
                            break;
                        case "ik_in_start_left_hand":
                        case "ik_out_end_left_hand":
                            Logging.Logger.Info($"Adding Left Hand IK 0.0 weights from: {note.Name} ({note.KeyFrames.Count} frames)");
                            AddShiftedWeights(lSolver?.Weights, note.KeyFrames, sampler.StartFrame, 0);
                            break;
                        case "ik_out_start_right_hand":
                        case "ik_in_end_right_hand":
                            Logging.Logger.Info($"Adding Right Hand IK 1.0 weights from: {note.Name} ({note.KeyFrames.Count} frames)");
                            AddShiftedWeights(rSolver?.Weights, note.KeyFrames, sampler.StartFrame, 1);
                            break;
                        case "ik_in_start_right_hand":
                        case "ik_out_end_right_hand":
                            Logging.Logger.Info($"Adding Right Hand IK 0.0 weights from: {note.Name} ({note.KeyFrames.Count} frames)");
                            AddShiftedWeights(rSolver?.Weights, note.KeyFrames, sampler.StartFrame, 0);
                            break;
                        case "fingers_out_start_left_hand":
                        case "fingers_in_end_left_hand":
                            Logging.Logger.Info($"Adding Left Hand Finger 1.0 weights from: {note.Name} ({note.KeyFrames.Count} frames)");
                            AddShiftedWeights(plSampler?.Weights, note.KeyFrames, sampler.StartFrame, 1);
                            break;
                        case "fingers_in_start_left_hand":
                        case "fingers_out_end_left_hand":
                            Logging.Logger.Info($"Adding Left Hand Finger 0.0 weights from: {note.Name} ({note.KeyFrames.Count} frames)");
                            AddShiftedWeights(plSampler?.Weights, note.KeyFrames, sampler.StartFrame, 0);
                            break;
                        case "fingers_out_start_right_hand":
                        case "fingers_in_end_right_hand":
                            Logging.Logger.Info($"Adding Right Hand Finger 1.0 weights from: {note.Name} ({note.KeyFrames.Count} frames)");
                            AddShiftedWeights(prSampler?.Weights, note.KeyFrames, sampler.StartFrame, 1);
                            break;
                        case "fingers_in_start_right_hand":
                        case "fingers_out_end_right_hand":
                            Logging.Logger.Info($"Adding Right Hand Finger 0.0 weights from: {note.Name} ({note.KeyFrames.Count} frames)");
                            AddShiftedWeights(prSampler?.Weights, note.KeyFrames, sampler.StartFrame, 0);
                            break;
                        case "gesture_blend_in":
                        case "gesture_blend_out_start":
                            Logging.Logger.Info($"Adding Gesture 1.0 weights from: {note.Name} ({note.KeyFrames.Count} frames)");
                            gestureLayers.ForEach(layer => AddShiftedWeights(layer.Weights, note.KeyFrames, sampler.StartFrame, 1));
                            break;
                        case "gesture_blend_out_end":
                            Logging.Logger.Info($"Adding Gesture 0.0 weights from: {note.Name} ({note.KeyFrames.Count} frames)");
                            gestureLayers.ForEach(layer => AddShiftedWeights(layer.Weights, note.KeyFrames, sampler.StartFrame, 0));
                            break;
                    }
                }
            }

            Logging.Logger.Info($"Built weights from notetracks");

            // Ensure the weights we just built are in-order
            player.Layers.ForEach(x => x.Weights.Sort((x, y) => x.Frame.CompareTo(y.Frame)));
            player.Solvers.ForEach(x => x.Weights.Sort((x, y) => x.Frame.CompareTo(y.Frame)));

            //var v = new (Vector3, Quaternion)[skeleton.Bones.Count];

            //skeleton.InitializeAnimationTransforms();
            //player.Update(player.FrameCount, AnimationSampleType.AbsoluteFrameTime);

            //for (int k = 0; k < skeleton.Bones.Count; k++)
            //{
            //    v[k] = (skeleton!.Bones[k].LocalTranslation, skeleton!.Bones[k].LocalRotation);
            //}

            //var finalFrame = player.FrameCount;

            //for (int i = 0; i < player.FrameCount; i++)
            //{
            //    skeleton.InitializeAnimationTransforms();
            //    player.Update(i, AnimationSampleType.AbsoluteFrameTime);

            //    bool minChanges = true;

            //    for (int k = 0; k < skeleton.Bones.Count; k++)
            //    {
            //        var a = skeleton!.Bones[k].LocalTranslation;
            //        var b = skeleton!.Bones[k].LocalRotation;

            //        var a0 = v[k].Item1;
            //        var b0 = v[k].Item2;
            //        var d0 = Vector3.DistanceSquared(a0, a);
            //        var d1 = Quaternion.Dot(b0, b);

            //        if (Quaternion.Dot(b0, b) < 0.9)
            //            minChanges = false;
            //        if (Vector3.DistanceSquared(a0, a) > (1 * 1))
            //            minChanges = false;
            //    }

            //    if (minChanges)
            //    {
            //        finalFrame = i;
            //        break;
            //    }
            //}

            var bakeBoneIndices = bakeRelevantBonesOnly
                ? FindRelevantBoneIndices(skeleton, contributingAnimations, player.Solvers).ToHashSet()
                : Enumerable.Range(0, skeleton.Bones.Count).ToHashSet();
            Logging.Logger.Info($"Generating output animation with {player.FrameCount} frames and {bakeBoneIndices.Count} initially selected of {skeleton.Bones.Count} bones");

            var newAnim = new SkeletonAnimation(animation.OutputName, skeleton)
            {
                TransformType = TransformType.Absolute,
                Framerate = targetFramerate,
            };

            Logging.Logger.Info($"Generating frames");

            for (int i = 0; i < player.FrameCount; i++)
            {
                skeleton.InitializeAnimationTransforms();
                if (followHand is null || followAnchor is null) player.Update(i, AnimationSampleType.AbsoluteFrameTime);
                else
                {
                    foreach (var layer in player.Layers) layer.Update(i, AnimationSampleType.AbsoluteFrameTime);
                    skeleton.Update();
                    var handWorld = FollowWorld(followHand);
                    var position = handWorld.Position + Vector3.Transform(gripPosition, handWorld.Rotation);
                    var rotation = Quaternion.Normalize(handWorld.Rotation * gripRotation);
                    var parentWorld = followAnchor.Parent is null ? (Position: Vector3.Zero, Rotation: Quaternion.Identity) : FollowWorld(followAnchor.Parent);
                    var inverseParent = Quaternion.Inverse(parentWorld.Rotation);
                    followAnchor.LocalTranslation = Vector3.Transform(position - parentWorld.Position, inverseParent);
                    followAnchor.LocalRotation = Quaternion.Normalize(inverseParent * rotation);
                    skeleton.Update();
                    // The opposite hand can still solve against the now-moving weapon target.
                    foreach (var solver in player.Solvers) solver.Update(i);
                }

                if (bakeRelevantBonesOnly)
                {
                    for (var k = 0; k < skeleton.Bones.Count; k++)
                    {
                        if (bakeBoneIndices.Contains(k) || !TransformDiffersFromBindPose(skeleton.Bones[k]))
                            continue;

                        bakeBoneIndices.Add(k);
                        for (var earlierFrame = 0; earlierFrame < i; earlierFrame++)
                        {
                            newAnim.Targets[k].AddTranslationFrame(earlierFrame, skeleton.Bones[k].BaseLocalTranslation);
                            newAnim.Targets[k].AddRotationFrame(earlierFrame, skeleton.Bones[k].BaseLocalRotation);
                        }
                    }
                }

                for (var k = 0; k < skeleton.Bones.Count; k++)
                {
                    if (!bakeBoneIndices.Contains(k))
                        continue;
                    newAnim.Targets[k].AddTranslationFrame(i, skeleton!.Bones[k].LocalTranslation);
                    newAnim.Targets[k].AddRotationFrame(i, skeleton!.Bones[k].LocalRotation);
                }
            }

            if (bakeRelevantBonesOnly)
            {
                newAnim.Targets.RemoveAll(target =>
                    target.TranslationFrameCount == 0
                    && target.RotationFrameCount == 0
                    && target.ScaleFrameCount == 0);
                Logging.Logger.Info($"Relevant-bone bake retained {newAnim.Targets.Count} of {skeleton.Bones.Count} bones");
            }

            Logging.Logger.Info($"Generated frames");

            //// Ensure we add the last frame
            //for (int i = 0; i < skeleton.Bones.Count; i++)
            //{
            //    newAnim.Targets[i].AddTranslationFrame(finalFrame + 2, v[i].Item1);
            //    newAnim.Targets[i].AddRotationFrame(finalFrame + 2, v[i].Item2);
            //}

            Logging.Logger.Info($"Copying notetracks");

            // Copy notetracks
            foreach (var layer in player.Layers)
            {
                if (layer.Animation.Actions is null)
                    continue;

                foreach (var note in layer.Animation.Actions)
                {
                    switch (note.Name)
                    {
                        case "ik_out_start_left_hand":
                        case "ik_in_end_left_hand":
                        case "ik_in_start_left_hand":
                        case "ik_out_end_left_hand":
                        case "ik_out_start_right_hand":
                        case "ik_in_end_right_hand":
                        case "ik_in_start_right_hand":
                        case "ik_out_end_right_hand":
                        case "fingers_out_start_left_hand":
                        case "fingers_in_end_left_hand":
                        case "fingers_in_start_left_hand":
                        case "fingers_out_end_left_hand":
                        case "gesture_blend_in":
                        case "fingers_out_start_right_hand":
                        case "fingers_in_end_right_hand":
                        case "fingers_in_start_right_hand":
                        case "fingers_out_end_right_hand":
                        case "gesture_blend_out_start":
                        case "gesture_blend_out_end":
                            Logging.Logger.Info($"Skipped notetrack: {note.Name}");
                            break;
                        default:
                            Logging.Logger.Info($"Created notetrack: {note.Name}");
                            newAnim.CreateAction(
                                note.Name,
                                note.KeyFrames.Select(x => new AnimationKeyFrame<float, Action<Graphics3DScene>?>(
                                    x.Frame + layer.StartFrame,
                                    x.Value)));
                            break;
                    }
                }
            }

            return newAnim;
        }

        internal static string SaveBaked(SkeletonMergePlan mergePlan, SkeletonAnimation newAnim,
            string outputFull, string format, bool castAnimationOnly)
        {
            format = OutputFormatCatalog.Normalize(format);

            Logging.Logger.Info($"Copied notetracks");
            Logging.Logger.Info($"Saving animation to: {outputFull}");

            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputFull))!);

            if (string.Equals(format, ".cast", StringComparison.OrdinalIgnoreCase)
                && !castAnimationOnly)
            {
                MayaCastPackage.Save(
                    outputFull,
                    mergePlan,
                    newAnim,
                    TranslatorFactory);
            }
            else if (string.Equals(format, ".cast", StringComparison.OrdinalIgnoreCase))
            {
                SaveAtomically(outputFull, temporaryPath =>
                {
                    TranslatorFactory.Save(temporaryPath, newAnim);
                    var cast = Cast.NET.CastReader.Load(temporaryPath);
                    var root = cast.RootNodes.First();
                    var hashes = cast.RootNodes.SelectMany(CastNodeTraversal.DescendantsAndSelf).Select(node => node.Hash).ToHashSet();
                    ulong hash = 1;
                    while (hashes.Contains(hash)) hash++;
                    var metadata = new Cast.NET.CastNode(Cast.NET.CastNodeIdentifier.Metadata) { Hash = hash, Parent = root };
                    metadata.AddString("up", mergePlan.UpAxis);
                    Cast.NET.CastWriter.Save(temporaryPath, cast);
                });
            }
            else if (string.Equals(format, ".smd", StringComparison.OrdinalIgnoreCase))
            {
                SaveAtomically(outputFull, temporaryPath => SmdAnimationExporter.Save(temporaryPath, newAnim));
            }
            else if (string.Equals(format, ".fbx", StringComparison.OrdinalIgnoreCase))
            {
                SaveAtomically(outputFull, temporaryPath =>
                {
                    var stagingDirectory = Path.Combine(
                        Path.GetTempPath(),
                        "AlchemyStarsFbx",
                        Guid.NewGuid().ToString("N"));
                    var stagedFbx = Path.Combine(stagingDirectory, "output.fbx");
                    var stagedCast = Path.Combine(stagingDirectory, "input.cast");
                    try
                    {
                        Directory.CreateDirectory(stagingDirectory);
                        MayaCastPackage.Save(stagedCast, mergePlan, newAnim, TranslatorFactory);
                        DesktopFbxExporter.Export(stagedCast, stagedFbx, newAnim.Framerate);
                        File.Move(stagedFbx, temporaryPath, overwrite: true);
                    }
                    finally
                    {
                        if (Directory.Exists(stagingDirectory))
                        {
                            try
                            {
                                Directory.Delete(stagingDirectory, recursive: true);
                            }
                            catch (Exception ex)
                            {
                                Logging.Logger.Warn($"Failed to clean FBX staging directory: {stagingDirectory}", ex);
                            }
                        }
                    }
                });
            }
            else
            {
                SaveAtomically(outputFull, temporaryPath => TranslatorFactory.Save(temporaryPath, newAnim));
            }

            Logging.Logger.Info($"Saved animation to: {outputFull}");
            return outputFull;
        }

        internal static IReadOnlyList<int> FindRelevantBoneIndices(
            Skeleton skeleton,
            IEnumerable<SkeletonAnimation> animations,
            IEnumerable<AnimationSamplerSolver> solvers)
        {
            var relevantBoneNames = animations
                .SelectMany(animation => animation.Targets)
                .Where(target =>
                    target.TranslationFrameCount > 0
                    || target.RotationFrameCount > 0
                    || target.ScaleFrameCount > 0)
                .Select(target => target.BoneName)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            foreach (var solver in solvers)
            {
                if (solver is not IKTwoBoneSolver twoBoneSolver)
                {
                    Logging.Logger.Warn($"Selective baking does not recognize solver '{solver.GetType().Name}'; baking every bone for safety.");
                    return Enumerable.Range(0, skeleton.Bones.Count).ToArray();
                }

                AddBoneName(twoBoneSolver.StartBone.Name);
                AddBoneName(twoBoneSolver.MiddleBone.Name);
                AddBoneName(twoBoneSolver.EndBone.Name);
            }

            return skeleton.Bones
                .Select((bone, index) => (bone, index))
                .Where(item => item.bone.Name is not null && relevantBoneNames.Contains(item.bone.Name))
                .Select(item => item.index)
                .ToArray();

            void AddBoneName(string? name)
            {
                if (!string.IsNullOrWhiteSpace(name))
                    relevantBoneNames.Add(name);
            }
        }

        private static bool TransformDiffersFromBindPose(SkeletonBone bone)
        {
            if (Vector3.DistanceSquared(bone.LocalTranslation, bone.BaseLocalTranslation) > 0.000001f)
                return true;

            var rotationDot = MathF.Abs(Quaternion.Dot(bone.LocalRotation, bone.BaseLocalRotation));
            return !float.IsFinite(rotationDot) || 1f - MathF.Min(rotationDot, 1f) > 0.0001f;
        }

        /// <summary>
        /// Loads a combined skeleton from the provided parts.
        /// </summary>
        /// <param name="parts">The parts to load skeletons from.</param>
        /// <returns>A single combined skeleton.</returns>
#if !ALCHEMYSTARS_ENGINE
        public static Skeleton LoadSkeletonFromParts(IEnumerable<Part> parts, bool matchOldCallOfDuty) =>
            SkeletonMergePlan.Build(parts, matchOldCallOfDuty).Skeleton;
#endif

        private static void SaveAtomically(string outputPath, Action<string> write)
        {
            var fullOutputPath = Path.GetFullPath(outputPath);
            var directory = Path.GetDirectoryName(fullOutputPath)
                ?? throw new InvalidOperationException("Output path has no directory.");
            Directory.CreateDirectory(directory);
            var temporaryPath = Path.Combine(
                directory,
                $".{Path.GetFileName(fullOutputPath)}.{Guid.NewGuid():N}.tmp{Path.GetExtension(fullOutputPath)}");
            try
            {
                write(temporaryPath);
                File.Move(temporaryPath, fullOutputPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static void AddShiftedWeights(
            List<AnimationKeyFrame<float, float>>? destination,
            IEnumerable<AnimationKeyFrame<float, Action<Graphics3DScene>?>> source,
            float frameOffset,
            float value)
        {
            if (destination is null)
                return;

            destination.AddRange(source.Select(frame =>
                new AnimationKeyFrame<float, float>(frame.Frame + frameOffset, value)));
        }
    }
}
