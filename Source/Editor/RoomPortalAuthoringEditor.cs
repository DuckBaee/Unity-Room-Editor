using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

[CustomEditor(typeof(RoomPortalAuthoring)), CanEditMultipleObjects]
public sealed class RoomPortalAuthoringEditor : Editor
{
    private System.Action _refreshPermissions;

    private void OnEnable()
    {
        EditorApplication.hierarchyChanged += RefreshPermissions;
        EditorApplication.update += RefreshPermissions;
        Undo.undoRedoPerformed += RefreshPermissions;
    }

    private void OnDisable()
    {
        EditorApplication.hierarchyChanged -= RefreshPermissions;
        EditorApplication.update -= RefreshPermissions;
        Undo.undoRedoPerformed -= RefreshPermissions;
        _refreshPermissions = null;
    }

    private void RefreshPermissions() => _refreshPermissions?.Invoke();

    internal static bool CanEditDesign(RoomPortalAuthoring author)
    {
        if (author == null) return false;
        var stage = PrefabStageUtility.GetPrefabStage(author.gameObject);
        if (stage != null) return stage.prefabContentsRoot == author.gameObject;
        if (EditorUtility.IsPersistent(author))
            return AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GetAssetPath(author)) == author.gameObject;
        // New, unparented design authoring is allowed; room instances are not.
        return author.transform.parent == null && !PrefabUtility.IsPartOfPrefabInstance(author);
    }

    public override VisualElement CreateInspectorGUI()
    {
        var root = new VisualElement();
        bool design = targets.Cast<RoomPortalAuthoring>().All(CanEditDesign);
        var script = new PropertyField(serializedObject.FindProperty("m_Script"));
        script.SetEnabled(false);
        root.Add(script);
        var help = new HelpBox(design
            ? "포털 디자인 규격입니다. 이 디자인을 사용하는 방은 변경 후 재검증해야 합니다."
            : "방 포털의 폭·높이와 모델 루트는 디자인에서 결정합니다. 위치·45도 방향은 Transform, 예약 깊이는 아래에서 수정하세요.", HelpBoxMessageType.Info);
        root.Add(help);
        var size = new PropertyField(serializedObject.FindProperty("openingSize"), "개구부 크기 (W×H)") { name = "openingSize" };
        size.SetEnabled(design);
        root.Add(size);
        root.Add(new PropertyField(serializedObject.FindProperty("reservationDepth"), "예약 깊이") { name = "reservationDepth" });
        var visual = new PropertyField(serializedObject.FindProperty("visualRoot"), "모델 루트") { name = "visualRoot" };
        visual.SetEnabled(design);
        root.Add(visual);
        var id = new PropertyField(serializedObject.FindProperty("portalId"), "포털 ID");
        id.SetEnabled(false);
        root.Add(id);
        _refreshPermissions = () =>
        {
            bool editable = targets.Length > 0 && targets.Cast<RoomPortalAuthoring>().All(CanEditDesign);
            if (editable == design) return;
            design = editable;
            size.SetEnabled(editable);
            visual.SetEnabled(editable);
            help.text = editable
                ? "포털 디자인 규격입니다. 이 디자인을 사용하는 방은 변경 후 재검증해야 합니다."
                : "방 포털의 폭·높이와 모델 루트는 디자인에서 결정합니다. 위치·45도 방향은 Transform, 예약 깊이는 아래에서 수정하세요.";
        };
        return root;
    }
}
