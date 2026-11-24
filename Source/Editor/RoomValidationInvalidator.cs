using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Dependency imports (including nested prefabs and meshes) invalidate the old
// approval. The on-demand gate also covers unsaved in-memory data changes.
[InitializeOnLoad]
internal sealed class RoomValidationInvalidator : AssetPostprocessor, IPreprocessBuildWithReport
{
    private static bool queued;
    private static bool scanning;
    public int callbackOrder => -1000;
    static RoomValidationInvalidator() => Queue();
    private static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        Queue();
    }
    private static void Queue()
    {
        if (queued || scanning) return;
        queued = true;
        EditorApplication.delayCall += Invalidate;
    }
    internal static void Invalidate()
    {
        queued = false;
        if (scanning || EditorApplication.isCompiling) return;
        scanning = true;
        try
        {
            foreach (string guid in AssetDatabase.FindAssets("t:BlockData"))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.StartsWith("Assets/__RoomSave_", System.StringComparison.Ordinal)) continue;
                var data = AssetDatabase.LoadAssetAtPath<BlockData>(path);
                if (data == null || data.spaceCheckState != SpaceCheckState.Valid || string.IsNullOrEmpty(data.spaceCheckFingerprint)) continue;
                if (RoomValidationFingerprint.IsCurrent(data)) continue;
                data.spaceCheckState = SpaceCheckState.Unchecked;
                EditorUtility.SetDirty(data);
                AssetDatabase.SaveAssetIfDirty(data);
            }
        }
        finally { scanning = false; }
    }
    public void OnPreprocessBuild(BuildReport report)
    {
        Invalidate();
    }
}
