using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class RoomZonePanel : VisualElement
{
    private readonly VisualElement _entries = new VisualElement();
    private RoomZone[] _displayed = System.Array.Empty<RoomZone>();
    private GameObject _roomRoot;
    private readonly Button _add;

    public RoomZonePanel()
    {
        Add(new Label("영역 이벤트") { name = "zone-title" });
        Add(new HelpBox("트리거 Collider가 있는 오브젝트에 RoomZone을 추가합니다.\n액터가 영역에 진입·이탈·머무를 때 이벤트가 실행됩니다.\n다중 Collider를 가진 액터도 중복 없이 1회 처리됩니다.", HelpBoxMessageType.Info));
        _add = new Button(AddToSelection) { text = "선택한 아트에 영역 추가", name = "btn-add-zone" };
        Add(_add);
        Add(_entries);
    }

    private bool CanAdd(GameObject selected) =>
        _roomRoot != null && selected != null &&
        !EditorUtility.IsPersistent(selected) &&
        selected.transform.IsChildOf(_roomRoot.transform) &&
        selected.GetComponentInParent<RoomPortalAuthoring>() == null &&
        selected.GetComponent<RoomZone>() == null;

    private void AddToSelection()
    {
        var selected = Selection.activeGameObject;
        if (!CanAdd(selected)) return;
        var collider = selected.GetComponent<Collider>();
        if (collider == null) collider = Undo.AddComponent<BoxCollider>(selected);
        Undo.RecordObject(collider, "영역 트리거 설정");
        collider.isTrigger = true;
        Undo.AddComponent<RoomZone>(selected);
        Refresh(_roomRoot);
    }

    internal void Refresh(GameObject root)
    {
        _roomRoot = root;
        _add.SetEnabled(CanAdd(Selection.activeGameObject));
        var current = root != null
            ? root.GetComponentsInChildren<RoomZone>(true)
            : System.Array.Empty<RoomZone>();
        if (current.SequenceEqual(_displayed) && _entries.childCount != 0) return;
        _entries.Unbind();
        _entries.Clear();
        _displayed = current;
        if (current.Length == 0)
        {
            _entries.Add(new Label("영역 요소 없음 — 트리거 Collider 오브젝트에 추가하세요."));
            return;
        }
        foreach (var zone in current)
        {
            var foldout = new Foldout { text = zone.name, value = true };
            foldout.Add(new Button(() => Selection.activeGameObject = zone.gameObject) { text = "씬에서 선택" });
            var so = new SerializedObject(zone);
            foldout.Add(new PropertyField(so.FindProperty("filterByTag"),     "태그 필터 사용"));
            foldout.Add(new PropertyField(so.FindProperty("targetTag"),       "대상 태그"));
            foldout.Add(new PropertyField(so.FindProperty("filterByLayer"),   "레이어 필터 사용"));
            foldout.Add(new PropertyField(so.FindProperty("targetLayerMask"), "대상 레이어"));
            foldout.Add(new PropertyField(so.FindProperty("stayInterval"),    "머무름 간격 (초)"));
            foldout.Add(new PropertyField(so.FindProperty("onEntered"),       "진입 시 실행"));
            foldout.Add(new PropertyField(so.FindProperty("onExited"),        "이탈 시 실행"));
            foldout.Add(new PropertyField(so.FindProperty("onStayed"),        "머무름 시 실행"));
            foldout.Bind(so);
            _entries.Add(foldout);
        }
    }
}
