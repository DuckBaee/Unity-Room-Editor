using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

internal sealed class RoomConditionalSpawnPanel : VisualElement
{
    private readonly VisualElement _entries = new VisualElement();
    private RoomConditionalSpawn[] _displayed = System.Array.Empty<RoomConditionalSpawn>();
    private GameObject _roomRoot;
    private readonly Button _add;
    private readonly HelpBox _eventRefWarning;

    public RoomConditionalSpawnPanel()
    {
        Add(new Label("조건부 생성") { name = "spawn-title" });
        Add(new HelpBox("방 초기화 시 한 번 추첨합니다. 같은 생성 시드·방 위치·데이터·요소 순서에서 재현됩니다.\n비활성 요소도 초기화하며 편집 모드의 원본은 바꾸지 않습니다.", HelpBoxMessageType.Info));
        _eventRefWarning = new HelpBox("", HelpBoxMessageType.Warning);
        _eventRefWarning.style.display = DisplayStyle.None;
        Add(_eventRefWarning);
        _add = new Button(AddToSelection) { text = "선택한 아트에 조건부 생성 추가", name = "btn-add-spawn" };
        Add(_add);
        Add(_entries);
    }

    private bool CanAdd(GameObject selected) =>
        _roomRoot != null && selected != null &&
        !EditorUtility.IsPersistent(selected) &&
        selected.transform.IsChildOf(_roomRoot.transform) &&
        selected.GetComponentInParent<RoomPortalAuthoring>() == null &&
        selected.GetComponent<RoomConditionalSpawn>() == null;

    private void AddToSelection()
    {
        var selected = Selection.activeGameObject;
        if (!CanAdd(selected)) return;
        Undo.AddComponent<RoomConditionalSpawn>(selected);
        Refresh(_roomRoot);
    }

    internal void Refresh(GameObject root)
    {
        _roomRoot = root;
        _add.SetEnabled(CanAdd(Selection.activeGameObject));
        var current = root != null
            ? root.GetComponentsInChildren<RoomConditionalSpawn>(true)
            : System.Array.Empty<RoomConditionalSpawn>();

        RefreshEventRefWarning(root, current);

        if (current.SequenceEqual(_displayed) && _entries.childCount != 0) return;
        _entries.Unbind();
        _entries.Clear();
        _displayed = current;
        if (current.Length == 0)
        {
            _entries.Add(new Label("조건부 생성 없음 — 선택 오브젝트에 추가하세요."));
            return;
        }
        foreach (var spawn in current)
        {
            var foldout = new Foldout { text = spawn.name, value = true };
            foldout.Add(new Button(() => Selection.activeGameObject = spawn.gameObject) { text = "씬에서 선택" });
            var so = new SerializedObject(spawn);
            foldout.Add(new PropertyField(so.FindProperty("entries"), "생성 목록 (대상·확률)"));
            foldout.Bind(so);
            _entries.Add(foldout);
        }
    }

    private void RefreshEventRefWarning(GameObject root, RoomConditionalSpawn[] spawns)
    {
        if (root == null || spawns.Length == 0)
        {
            _eventRefWarning.style.display = DisplayStyle.None;
            return;
        }

        var conditionalTargets = spawns
            .SelectMany(s => s.Entries)
            .Where(e => e != null && e.target != null)
            .Select(e => e.target)
            .ToHashSet();

        if (conditionalTargets.Count == 0) { _eventRefWarning.style.display = DisplayStyle.None; return; }

        var conflicts = new System.Collections.Generic.List<string>();
        foreach (var zone in root.GetComponentsInChildren<RoomZone>(true))
            foreach (var evt in new UnityEngine.Events.UnityEventBase[] { zone.OnEntered, zone.OnExited, zone.OnStayed })
                for (int i = 0; i < evt.GetPersistentEventCount(); i++)
                {
                    var target = evt.GetPersistentTarget(i);
                    var go = target as GameObject ?? (target as Component)?.gameObject;
                    if (go != null && conditionalTargets.Any(ct => go == ct || go.transform.IsChildOf(ct.transform)))
                        conflicts.Add($"{zone.name} → {go.name}");
                }
        foreach (var interaction in root.GetComponentsInChildren<RoomInteraction>(true))
        {
            int count = interaction.OnInteracted.GetPersistentEventCount();
            for (int i = 0; i < count; i++)
            {
                var t  = interaction.OnInteracted.GetPersistentTarget(i);
                var go = t as GameObject ?? (t as Component)?.gameObject;
                if (go != null && conditionalTargets.Any(ct =>
                        go == ct || go.transform.IsChildOf(ct.transform)))
                    conflicts.Add($"{interaction.name} → {go.name}");
            }
        }

        if (conflicts.Count > 0)
        {
            _eventRefWarning.text = "조건부로 비활성화될 수 있는 오브젝트가 상호작용 이벤트에 참조되어 있습니다:\n"
                                  + string.Join("\n", conflicts);
            _eventRefWarning.style.display = DisplayStyle.Flex;
        }
        else
        {
            _eventRefWarning.style.display = DisplayStyle.None;
        }
    }
}
