// Optional: copy into Assets/Editor. No automatic importer or prefab changes.
using System;
using System.Linq;
using UnityEditor;
using UnityEngine;

public sealed class UnityRavenfieldCameraSetup : EditorWindow
{
    private GameObject weapon;
    private Transform motion;
    private Transform cameraFrame;
    private TextAsset source;

    [MenuItem("Tools/Ravenfield/Bind optional RF camera motion")]
    private static void Open() { GetWindow<UnityRavenfieldCameraSetup>("RF camera binding"); }

    private void OnGUI()
    {
        EditorGUILayout.HelpBox("Optional scene-instance binding. Stop animation preview first. Camera frame must be an unanimated reference aligned with the first-person camera (+Z forward, +Y up). No controller, importer, asset or project settings are changed. Game validation is still required.", MessageType.Info);
        weapon = (GameObject)EditorGUILayout.ObjectField("Weapon scene object", weapon, typeof(GameObject), true);
        motion = (Transform)EditorGUILayout.ObjectField("RF_CameraMotion", motion, typeof(Transform), true);
        cameraFrame = (Transform)EditorGUILayout.ObjectField("Camera reference frame", cameraFrame, typeof(Transform), true);
        source = (TextAsset)EditorGUILayout.ObjectField("RFCameraMotion.txt", source, typeof(TextAsset), false);
        if (GUILayout.Button("Bind on this scene instance (Undo supported)"))
        {
            try { Bind(); }
            catch (Exception e) { Debug.LogError(e.Message); }
        }
    }

    private void Bind()
    {
        if (EditorApplication.isPlaying || AnimationMode.InAnimationMode())
            throw new InvalidOperationException("Stop Play mode and Animation preview before binding.");
        if (!weapon || !motion || !cameraFrame || !source || !weapon.scene.IsValid() || !motion.gameObject.scene.IsValid())
            throw new InvalidOperationException("Assign scene objects and the Ravenscript text asset.");
        if (!weapon.GetComponent<Weapon>() || motion.name != "RF_CameraMotion" || !motion.IsChildOf(weapon.transform))
            throw new InvalidOperationException("Motion must be RF_CameraMotion under the assigned Weapon.");
        if (motion.parent == null || motion.parent.name != "RF_Root")
            throw new InvalidOperationException("Expected RF_CameraMotion directly under RF_Root.");
        var rig = motion.parent.parent;
        if (!rig) throw new InvalidOperationException("RF_Root must have a stable armature parent.");
        if (weapon.GetComponentsInChildren<Lua.ScriptedBehaviour>(true).Any(s => s.behaviour == "RFCameraMotion"))
            throw new InvalidOperationException("This weapon already has RFCameraMotion; remove its binding before rebinding.");
        if (source.name != "RFCameraMotion" || !source.text.Contains("behaviour(\"RFCameraMotion\")"))
            throw new InvalidOperationException("Select the supplied RFCameraMotion.txt asset.");
        // Read the imported model's default transform, NEVER the first animated frame.
        var rest = PrefabUtility.GetCorrespondingObjectFromSource(motion);
        var path = AssetDatabase.GetAssetPath(rest);
        if (!rest || !(AssetImporter.GetAtPath(path) is ModelImporter))
            throw new InvalidOperationException("Motion must retain its imported FBX prefab source so the default rest pose can be read.");
        var restRig = PrefabUtility.GetCorrespondingObjectFromSource(rig);
        if (!restRig || rest.parent == null || rest.parent.parent != restRig)
            throw new InvalidOperationException("Motion and armature must retain matching FBX source hierarchy.");
        for (var t = motion.parent; t != null; t = t.parent)
        {
            var s = t.localScale;
            if (s.x <= 0 || Mathf.Abs(s.x - s.y) > 0.00001f || Mathf.Abs(s.x - s.z) > 0.00001f)
                throw new InvalidOperationException("Mirrored/nonuniform ancestor scale is unsupported. Use positive uniform scale.");
        }
        var group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("Bind optional RF camera motion");
        var host = new GameObject("RF Camera Bridge");
        Undo.RegisterCreatedObjectUndo(host, "Create RF camera bridge");
        host.transform.SetParent(weapon.transform, false);
        var restMarker = new GameObject("Rest (imported default; do not animate)").transform;
        restMarker.SetParent(host.transform, false);
        restMarker.localPosition = restRig.InverseTransformPoint(rest.position);
        restMarker.localRotation = Quaternion.Inverse(restRig.rotation) * rest.rotation;
        var basis = new GameObject("Basis (camera calibration; do not animate)").transform;
        basis.SetParent(host.transform, false);
        basis.localRotation = Quaternion.Inverse(cameraFrame.rotation) * rig.rotation;
        basis.localScale = Vector3.one * rig.lossyScale.x;
        var script = Undo.AddComponent<Lua.ScriptedBehaviour>(host);
        script.source = source;
        script.behaviour = "RFCameraMotion";
        script.targets = new[] {
            new Lua.NamedTarget { name = "weapon", value = weapon },
            new Lua.NamedTarget { name = "motion", value = motion.gameObject },
            new Lua.NamedTarget { name = "rig", value = rig.gameObject },
            new Lua.NamedTarget { name = "rest", value = restMarker.gameObject },
            new Lua.NamedTarget { name = "basis", value = basis.gameObject }
        };
        EditorUtility.SetDirty(script);
        Undo.CollapseUndoOperations(group);
        Selection.activeGameObject = host;
        Debug.Log("Optional RF camera bridge added to scene instance only. Check axes in game; save/apply the prefab yourself if desired.");
    }
}
