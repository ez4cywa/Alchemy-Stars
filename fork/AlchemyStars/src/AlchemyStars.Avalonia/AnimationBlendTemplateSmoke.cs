namespace AlchemyStars.Avalonia;

internal static class AnimationBlendTemplateSmoke
{
    public static void Run(string root)
    {
        var directory = Path.Combine(root, "template-analyzer");
        Directory.CreateDirectory(directory);
        var names = new[]
        {
            "sat_vm_ar_test_idle.cast",
            "sat_vm_ar_test_idle_active.cast",
            "sat_vm_ar_test_jog_loop.cast",
            "sat_vm_ar_test_jog_offset_additive.cast",
            "sat_vm_ar_test_walk_loop.cast",
            "sat_vm_ar_test_sprint_loop.cast",
            "sat_vm_ar_test_sprint_to_walk.cast",
            "sat_vm_ar_test_sprint_to_jog.cast",
            "sat_vm_ar_test_walk_to_sprint.cast",
            "sat_vm_ar_test_walk_to_jog.cast",
            "sat_vm_ar_test_jog_to_sprint.cast",
            "sat_vm_ar_test_jog_to_walk.cast",
            "sat_vm_ar_test_ads_up.cast",
            "sat_vm_ar_test_ads_down.cast",
            "sat_vm_ar_test_walk_offset_additive.cast",
            "sat_vm_ar_test_sprint_offset_additive.cast",
            "sat_vm_ar_test_reload.cast",
            "sat_vm_ar_test_reload_ads.cast",
            "sat_vm_ar_test_unrelated.cast",
        };
        foreach (var name in names) File.WriteAllBytes(Path.Combine(directory, name), []);

        var imported = names.Where(name => name is not "sat_vm_ar_test_jog_offset_additive.cast"
            and not "sat_vm_ar_test_walk_offset_additive.cast"
            and not "sat_vm_ar_test_sprint_offset_additive.cast")
            .Select(name => Path.Combine(directory, name)).ToArray();
        var idle = Path.Combine(directory, "sat_vm_ar_test_idle.cast");
        var active = Path.Combine(directory, "sat_vm_ar_test_idle_active.cast");
        var jog = Path.Combine(directory, "sat_vm_ar_test_jog_loop.cast");
        var jogOffset = Path.Combine(directory, "sat_vm_ar_test_jog_offset_additive.cast");
        var walk = Path.Combine(directory, "sat_vm_ar_test_walk_loop.cast");
        var walkOffset = Path.Combine(directory, "sat_vm_ar_test_walk_offset_additive.cast");
        var sprint = Path.Combine(directory, "sat_vm_ar_test_sprint_loop.cast");
        var sprintOffset = Path.Combine(directory, "sat_vm_ar_test_sprint_offset_additive.cast");
        var adsUp = Path.Combine(directory, "sat_vm_ar_test_ads_up.cast");
        var adsDown = Path.Combine(directory, "sat_vm_ar_test_ads_down.cast");
        var transitions = new[] { "sprint_to_walk", "sprint_to_jog", "walk_to_sprint", "walk_to_jog", "jog_to_sprint", "jog_to_walk" };
        foreach (var source in imported)
        {
            var analysis = AnimationBlendTemplateAnalyzer.Analyze(source, imported);
            var stem = Path.GetFileNameWithoutExtension(source);
            if (stem.Equals("sat_vm_ar_test_idle", StringComparison.OrdinalIgnoreCase))
                Require(analysis.BaseAnimationFile == idle && analysis.LayerFiles.Count == 0, "Idle unexpectedly gained a layer.");
            else if (stem.Equals("sat_vm_ar_test_idle_active", StringComparison.OrdinalIgnoreCase))
                Require(analysis.BaseAnimationFile == idle && analysis.LayerFiles.SequenceEqual([active]), "idle_active did not use idle as its base.");
            else if (stem.Equals("sat_vm_ar_test_jog_loop", StringComparison.OrdinalIgnoreCase))
                Require(analysis.BaseAnimationFile == idle && analysis.LayerFiles.SequenceEqual([jog, jogOffset]), "Jog loop did not add its matching offset layer.");
            else if (stem.Equals("sat_vm_ar_test_walk_loop", StringComparison.OrdinalIgnoreCase))
                Require(analysis.BaseAnimationFile == idle && analysis.LayerFiles.SequenceEqual([walk, walkOffset]), "Walk loop did not add its matching offset layer.");
            else if (stem.Equals("sat_vm_ar_test_sprint_loop", StringComparison.OrdinalIgnoreCase))
                Require(analysis.BaseAnimationFile == idle && analysis.LayerFiles.SequenceEqual([sprint, sprintOffset]), "Sprint loop did not add its matching offset layer.");
            else if (stem.Equals("sat_vm_ar_test_ads_up", StringComparison.OrdinalIgnoreCase))
                Require(analysis.BaseAnimationFile == idle && analysis.LayerFiles.SequenceEqual([adsUp]), "ADS up did not pair with idle.");
            else if (stem.Equals("sat_vm_ar_test_ads_down", StringComparison.OrdinalIgnoreCase))
                Require(analysis.BaseAnimationFile == idle && analysis.LayerFiles.SequenceEqual([adsDown]), "ADS down did not pair with idle.");
            else if (transitions.Any(transition => stem.EndsWith(transition, StringComparison.OrdinalIgnoreCase)))
                Require(analysis.BaseAnimationFile == idle && analysis.LayerFiles.SequenceEqual([source]), $"Transition '{stem}' did not pair with idle.");
            else
                Require(analysis.BaseAnimationFile == source && analysis.LayerFiles.Count == 0,
                    $"Unrelated animation '{stem}' was changed into a blend.");
        }
        var standaloneOffset = AnimationBlendTemplateAnalyzer.Analyze(Path.Combine(directory, "sat_vm_ar_test_jog_offset_additive.cast"), imported);
        Require(standaloneOffset.BaseAnimationFile == Path.Combine(directory, "sat_vm_ar_test_jog_offset_additive.cast") && standaloneOffset.LayerFiles.Count == 0, "Standalone offset was changed into a blend.");

        var genericDirectory = Path.Combine(directory, "generic");
        Directory.CreateDirectory(genericDirectory);
        var genericIdle = Path.Combine(genericDirectory, "idle.cast");
        var genericActive = Path.Combine(genericDirectory, "idle_active.cast");
        File.WriteAllBytes(genericIdle, []);
        File.WriteAllBytes(genericActive, []);

        var prefixedDirectory = Path.Combine(directory, "prefixed");
        Directory.CreateDirectory(prefixedDirectory);
        var prefixedIdle = Path.Combine(prefixedDirectory, "sat_vm_ar_hawk_idle.cast");
        var prefixedActive = Path.Combine(prefixedDirectory, "vm_p01_ar_coslo723_idle_active.cast");
        File.WriteAllBytes(prefixedIdle, []);
        File.WriteAllBytes(prefixedActive, []);
        var genericAnalysis = AnimationBlendTemplateAnalyzer.Analyze(genericActive, [genericActive]);
        Require(genericAnalysis.BaseAnimationFile == genericIdle && genericAnalysis.LayerFiles.SequenceEqual([genericActive]), "Standalone idle_active did not keyword-match idle.");
        var prefixedAnalysis = AnimationBlendTemplateAnalyzer.Analyze(prefixedActive, [prefixedActive]);
        Require(prefixedAnalysis.BaseAnimationFile == prefixedIdle && prefixedAnalysis.LayerFiles.SequenceEqual([prefixedActive]), "Prefixed idle_active did not use the only idle candidate.");
        foreach (var suffix in new[] { "idle_bp_spirit", "idle_active_bp_spirit", "ads_up_additive", "super_sprint_loop", "rise", "idleads" })
            File.WriteAllBytes(Path.Combine(directory, "sat_vm_ar_test_" + suffix + ".cast"), []);
        var variantSource = Path.Combine(directory, "sat_vm_ar_test_idle_active_bp_spirit.cast");
        var variant = AnimationBlendTemplateAnalyzer.Analyze(variantSource);
        Require(variant.BaseAnimationFile.EndsWith("_idle_bp_spirit.cast") && variant.LayerFiles.SequenceEqual([variantSource]), "Blueprint variant did not select its matching idle.");
        foreach (var suffix in new[] { "ads_up_additive", "super_sprint_loop", "rise", "idleads" })
        {
            var path = Path.Combine(directory, "sat_vm_ar_test_" + suffix + ".cast");
            var result = AnimationBlendTemplateAnalyzer.Analyze(path);
            Require(result.BaseAnimationFile == path && result.LayerFiles.Count == 0, "Non-whitelisted animation was blended: " + suffix);
            Require(result.EnableLeftHandIK && result.EnableRightHandIK, "Action word was mistaken for a hand marker.");
        }
        Console.WriteLine("Animation blend templates: whitelist, idle_active base fill, offsets and unrelated-source isolation PASS");
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }
}
