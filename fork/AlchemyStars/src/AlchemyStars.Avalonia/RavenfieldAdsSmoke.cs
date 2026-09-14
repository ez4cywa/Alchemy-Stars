using System.Numerics;
using Alchemist.UI;
using RedFox.Graphics3D.Skeletal;
using RedFox.Graphics3D.Translation;

namespace AlchemyStars.Avalonia;

internal static class RavenfieldAdsSmoke
{
    internal static int Run(string[] args)
    {
        try
        {
            if (args.Length != 3) throw new ArgumentException("--rf-ads-smoke <project.aprj> <ads.cast> <output-directory>");
            var store = new WorkspaceProjectStore();
            var document = store.Load(args[0]);
            var request = store.CreateExportRequest(document);
            var output = Path.GetFullPath(args[2]); Directory.CreateDirectory(output);
            var referenceIndex = document.Animations.ToList().FindIndex(a => a.Id == document.Ravenfield.ReferenceAnimationId);
            if (referenceIndex < 0) throw new InvalidDataException("Select the RF reference animation in the fixture.");
            var (axisRequest, _) = RavenfieldAdaptationEngine.PrepareModelAxes(request, document.Ravenfield.UntaggedModelUpAxis, output);
            var plan = SkeletonMergePlan.Build(axisRequest.Parts.Select(AnimationExportEngine.ToCompatibilityPart), request.Options.MatchOldCallOfDuty, outputAxis: "z");
            var job = new AnimationExportJob(Path.GetFullPath(args[1]), "ADS_processed", output, EnableLeftHandIk: false, EnableRightHandIk: false);
            var prepared = RavenfieldAdsPreparation.Prepare(axisRequest, job, referenceIndex, document.Ravenfield.IdleFrame, plan, output);
            Require(prepared.SourceFile != job.SourceFile && prepared.Layers?.Single().FilePath == job.SourceFile, "ADS reference was not inserted.");
            var explicitJob = job with { Layers = [new AnimationLayerSpec(job.SourceFile)] };
            Require(ReferenceEquals(explicitJob, RavenfieldAdsPreparation.Prepare(axisRequest, explicitJob, referenceIndex, 0, plan, output)), "Explicit layers changed.");
            var ordinary = request.Animations[referenceIndex];
            Require(ReferenceEquals(ordinary, RavenfieldAdsPreparation.Prepare(axisRequest, ordinary, referenceIndex, 0, plan, output)), "Ordinary animation changed.");
            MustReject(() => RavenfieldAdsPreparation.Prepare(axisRequest with { Animations = [job] }, job, 0, 0, plan, output));
            MustReject(() => RavenfieldAdsPreparation.Prepare(axisRequest, job, referenceIndex, int.MaxValue, plan, output));
            var paths = new AnimationExportEngine().Export(axisRequest with { Animations = [prepared], Options = request.Options with {
                Format = ExportFormat.Cast, OutputPrefix = "", OutputSuffix = "", OutputUpAxis = "z", CastAnimationOnly = false, BakeRelevantBonesOnly = false } }).OutputFiles;
            var factory = new Graphics3DTranslatorFactory().WithDefaultTranslators();
            var result = factory.Load<SkeletonAnimation>(paths.Single());
            var pose = factory.Load<SkeletonAnimation>(prepared.SourceFile);
            Require(result.Targets.Count == plan.Skeleton.Bones.Count, "ADS lost full hand/weapon tracks.");
            Require(result.GetAnimationFrameCount() == 11, "Hawk ADS duration changed.");
            var ads = result.Targets.Single(t => t.BoneName == "tag_ads");
            Require(Vector3.Distance(ads.SampleTranslation(0), ads.SampleTranslation(10)) > 0.1f, "ADS movement disappeared.");
            foreach (var name in new[] { "j_wrist_le", "j_wrist_ri", "tag_weapon" })
            {
                Require(Vector3.Distance(result.Targets.Single(t => t.BoneName == name).SampleTranslation(0), pose.Targets.Single(t => t.BoneName == name).SampleTranslation(0)) < 0.0001f, "Reference pose changed: " + name);
                Require(Math.Abs(Quaternion.Dot(result.Targets.Single(t => t.BoneName == name).SampleRotation(0), pose.Targets.Single(t => t.BoneName == name).SampleRotation(0))) > 0.9999f, "Reference rotation changed: " + name);
            }
            Console.WriteLine($"PASS ADS: {result.Targets.Count} full tracks; 11 frames; selected reference pose retained; explicit layers and ordinary clips unchanged. {paths.Single()}");
            return 0;
        }
        catch (Exception e) { Console.Error.WriteLine(e); return 1; }
    }
    private static void Require(bool condition, string message) { if (!condition) throw new InvalidDataException(message); }
    private static void MustReject(Action action)
    {
        try { action(); } catch (InvalidDataException) { return; }
        throw new InvalidDataException("Invalid ADS reference was accepted.");
    }
}
