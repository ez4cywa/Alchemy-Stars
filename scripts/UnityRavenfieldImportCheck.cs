// Run in an isolated Unity project after copying this file into Assets/Editor.
// Unity.exe -batchmode -nographics -quit -projectPath <project>
//   -executeMethod UnityRavenfieldImportCheck.Run -rfReport <absolute-report.json>
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class UnityRavenfieldImportCheck
{
    [Serializable] public sealed class CheckReport
    {
        public bool passed;
        public string unityVersion;
        public string assetPath;
        public float globalScale, fileScale;
        public bool useFileScale;
        public string animationType;
        public int transforms, skinnedMeshes, meshes, vertices, rfRootCount;
        public float handDistanceMeters, largestBoundsDimensionMeters;
        public string[] boneNames, errors;
        public ClipReport[] clips;
    }
    [Serializable] public sealed class ClipReport
    {
        public string name;
        public float length, frameRate, maxWorldVertexDeltaMeters;
        public int sampledVertices;
    }

    public static void Run()
    {
        var report = new CheckReport { unityVersion = Application.unityVersion,
            assetPath = "Assets/hawk_idle_rf_idle.fbx" };
        var failures = new List<string>();
        var args = Environment.GetCommandLineArgs();
        var outputArg = Array.IndexOf(args, "-rfReport");
        var output = outputArg >= 0 && outputArg + 1 < args.Length
            ? args[outputArg + 1] : Path.GetFullPath("../unity-rf-import-report.json");
        GameObject instance = null;
        try
        {
            AssetDatabase.ImportAsset(report.assetPath, ImportAssetOptions.ForceSynchronousImport);
            var importer = AssetImporter.GetAtPath(report.assetPath) as ModelImporter;
            Require(importer != null, "ModelImporter missing");
            report.globalScale = importer.globalScale;
            report.fileScale = importer.fileScale;
            report.useFileScale = importer.useFileScale;
            report.animationType = importer.animationType.ToString();
            Require(Mathf.Abs(importer.globalScale - 1f) < 0.00001f,
                "Default importer global scale is not 1");
            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(report.assetPath);
            Require(asset != null, "Imported model missing");
            instance = UnityEngine.Object.Instantiate(asset);
            var transforms = instance.GetComponentsInChildren<Transform>(true);
            report.transforms = transforms.Length;
            report.rfRootCount = transforms.Count(t => t.name == "RF_Root");
            Require(report.rfRootCount == 1, "Expected exactly one RF_Root");
            var left = transforms.Single(t => t.name == "Hand.L");
            var right = transforms.Single(t => t.name == "Hand.R");
            report.handDistanceMeters = Vector3.Distance(left.position, right.position);
            Require(report.handDistanceMeters > 0.02f && report.handDistanceMeters < 2f,
                "Hand distance outside plausible meter range");
            var skins = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
            report.skinnedMeshes = skins.Length;
            Require(skins.Length > 0, "No skinned mesh renderers");
            var meshes = AssetDatabase.LoadAllAssetsAtPath(report.assetPath).OfType<Mesh>().ToArray();
            report.meshes = meshes.Length;
            report.vertices = meshes.Sum(m => m.vertexCount);
            report.boneNames = skins.SelectMany(s => s.bones).Where(b => b != null)
                .Select(b => b.name).Distinct().OrderBy(n => n).ToArray();
            Require(report.boneNames.Contains("Hand.L") && report.boneNames.Contains("Hand.R"),
                "RF hands are not included in skin bone bindings");
            foreach (var skin in skins)
            {
                Require(skin.sharedMesh != null, "Skinned mesh is null");
                Require(skin.bones.Length > 0 && skin.bones.All(b => b != null),
                    "Missing bone bindings: " + skin.name);
                Require(skin.sharedMesh.bindposes.Length == skin.bones.Length,
                    "Bindpose/bone count mismatch: " + skin.name);
            }
            var initial = Bake(skins);
            var bounds = new Bounds(initial[0], Vector3.zero);
            foreach (var vertex in initial) bounds.Encapsulate(vertex);
            report.largestBoundsDimensionMeters = Mathf.Max(bounds.size.x, bounds.size.y, bounds.size.z);
            Require(report.largestBoundsDimensionMeters > 0.1f && report.largestBoundsDimensionMeters < 5f,
                "Model bounds outside plausible meter range");
            var clips = AssetDatabase.LoadAllAssetsAtPath(report.assetPath).OfType<AnimationClip>()
                .Where(c => !c.name.StartsWith("__preview__", StringComparison.Ordinal)).ToArray();
            Require(clips.Length > 0, "No imported animation clip");
            var clipReports = new List<ClipReport>();
            foreach (var clip in clips)
            {
                Require(clip.length > 0 && clip.frameRate > 0, "Empty clip: " + clip.name);
                Require(Mathf.Abs(clip.length * clip.frameRate - 1f) < 0.05f,
                    "Expected a two-frame static clip: " + clip.name);
                clip.SampleAnimation(instance, 0);
                var first = Bake(skins);
                clip.SampleAnimation(instance, clip.length);
                var last = Bake(skins);
                Require(first.Length == last.Length, "Sampled vertex count changed");
                var delta = first.Zip(last, (a, b) => Vector3.Distance(a, b)).Max();
                clipReports.Add(new ClipReport { name = clip.name, length = clip.length,
                    frameRate = clip.frameRate, maxWorldVertexDeltaMeters = delta,
                    sampledVertices = first.Length });
                Require(delta < 0.0001f, "Static idle changed between first and last frames: " + delta);
            }
            report.clips = clipReports.ToArray();
        }
        catch (Exception error) { failures.Add(error.ToString()); }
        finally
        {
            if (instance != null) UnityEngine.Object.DestroyImmediate(instance);
            report.errors = failures.ToArray();
            report.passed = failures.Count == 0;
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
            File.WriteAllText(output, JsonUtility.ToJson(report, true));
            Debug.Log("RF_IMPORT_CHECK " + JsonUtility.ToJson(report));
        }
        if (!report.passed) EditorApplication.Exit(1);
    }

    private static Vector3[] Bake(SkinnedMeshRenderer[] skins)
    {
        var vertices = new List<Vector3>();
        foreach (var skin in skins)
        {
            var baked = new Mesh();
            try
            {
                skin.BakeMesh(baked);
                Require(baked.vertexCount == skin.sharedMesh.vertexCount && baked.vertexCount > 0,
                    "BakeMesh returned incorrect vertex count: " + skin.name);
                foreach (var vertex in baked.vertices)
                {
                    var world = skin.transform.TransformPoint(vertex);
                    Require(Finite(world.x) && Finite(world.y) && Finite(world.z),
                        "Non-finite deformed vertex: " + skin.name);
                    vertices.Add(world);
                }
            }
            finally { UnityEngine.Object.DestroyImmediate(baked); }
        }
        return vertices.ToArray();
    }
    private static bool Finite(float value) { return !float.IsNaN(value) && !float.IsInfinity(value); }
    private static void Require(bool condition, string message)
    { if (!condition) throw new InvalidOperationException(message); }
}
