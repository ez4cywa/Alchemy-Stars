// Copy this file into Assets/Editor in the target RFTools project.
// Select the exported FBX, then Assets > Ravenfield > Apply adapter clip ranges.
// Runs only on that explicit menu action; it is not an automatic AssetPostprocessor.
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class UnityRavenfieldClipSetup
{
    [Serializable] private sealed class Report { public string mode; public Clip[] clips; }
    [Serializable] private sealed class Clip
    {
        public string name;
        public string takeName;
        public int firstFrame;
        public int lastFrame;
        public bool loopTime;
    }

    [MenuItem("Assets/Ravenfield/Apply adapter clip ranges", true)]
    private static bool CanApply()
    {
        return AssetImporter.GetAtPath(AssetDatabase.GetAssetPath(Selection.activeObject)) is ModelImporter;
    }

    [MenuItem("Assets/Ravenfield/Apply adapter clip ranges")]
    public static void Apply()
    {
        var assetPath = AssetDatabase.GetAssetPath(Selection.activeObject);
        var importer = AssetImporter.GetAtPath(assetPath) as ModelImporter;
        if (importer == null) throw new InvalidOperationException("Select the RF adapter's exported FBX first.");
        var reportPath = Path.ChangeExtension(Path.GetFullPath(assetPath), ".report.json");
        if (!File.Exists(reportPath)) throw new FileNotFoundException("Keep the adapter report beside the FBX.", reportPath);
        var report = JsonUtility.FromJson<Report>(File.ReadAllText(reportPath));
        if (report == null || report.mode != "animation-library" || report.clips == null || report.clips.Length == 0)
            throw new InvalidDataException("This is not an RF animation-library report.");
        if (report.clips.Any(c => c == null || String.IsNullOrWhiteSpace(c.name) || c.takeName != "Scene"
            || c.firstFrame < 1 || c.lastFrame < c.firstFrame)
            || report.clips.Select(c => c.name).Distinct().Count() != report.clips.Length)
            throw new InvalidDataException("Invalid or duplicate clip ranges.");
        var ordered = report.clips.OrderBy(c => c.firstFrame).ToArray();
        for (var i = 1; i < ordered.Length; i++)
            if (ordered[i].firstFrame <= ordered[i - 1].lastFrame)
                throw new InvalidDataException("Animation clip ranges overlap.");
        if (!EditorUtility.DisplayDialog("Apply RF animation clips",
            "Set " + report.clips.Length + " named Scene ranges on:\n" + assetPath
            + "\n\nExisting clips with matching names retain their other settings. Review animation events and the weapon controller separately.",
            "Apply ranges", "Cancel")) return;
        var existing = importer.clipAnimations;
        importer.clipAnimations = report.clips.Select(c =>
        {
            var match = existing.FirstOrDefault(old => old.name == c.name);
            var clip = match ?? new ModelImporterClipAnimation();
            clip.name = c.name;
            clip.takeName = c.takeName;
            clip.firstFrame = c.firstFrame;
            clip.lastFrame = c.lastFrame;
            if (match == null) clip.loopTime = c.loopTime;
            return clip;
        }).ToArray();
        importer.importAnimation = true;
        importer.SaveAndReimport();
        Debug.Log("Applied " + report.clips.Length + " RF animation ranges to " + assetPath
            + ". Configure loop flags, events and the Ravenfield weapon controller as needed.");
    }
}
