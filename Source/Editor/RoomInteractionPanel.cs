using System.Linq;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

// UI discovery follows the current art hierarchy, so no duplicate registry is saved.
internal sealed class RoomInteractionPanel : VisualElement
{
    private readonly VisualElement entries = new VisualElement();
    private RoomInteraction[] displayed = System.Array.Empty<RoomInteraction>();
    private GameObject roomRoot;
    private readonly Button add;

    public RoomInteractionPanel()
    {
        Add(new Label("상호작용 이벤트") { name = "interaction-title" });
        Add(new HelpBox("아트의 RoomInteraction을 자동으로 표시합니다. 실행 시 이벤트에 동작을 연결하세요. 방 차단/해제는 해당 요소의 BlockRoom / UnblockRoom을 사용할 수 있습니다.", HelpBoxMessageType.Info));
        add = new Button(AddToSelection) { text = "선택한 아트에 상호작용 추가", name = "btn-add-interaction" };
        Add(add);
        Add(entries);
    }

    private bool CanAdd(GameObject selected) => roomRoot != null && selected != null &&
        !EditorUtility.IsPersistent(selected) && selected.transform.IsChildOf(roomRoot.transform) &&
        selected.GetComponentInParent<RoomPortalAuthoring>() == null &&
        selected.GetComponent<RoomInteraction>() == null;

    private void AddToSelection()
    {
        var selected = Selection.activeGameObject;
        if (!CanAdd(selected)) return;
        Undo.AddComponent<RoomInteraction>(selected);
        Refresh(roomRoot);
    }

    internal void Refresh(GameObject root)
    {
        roomRoot = root;
        add.SetEnabled(CanAdd(Selection.activeGameObject));
        var current = root != null ? root.GetComponentsInChildren<RoomInteraction>(true)
            : System.Array.Empty<RoomInteraction>();
        if (current.SequenceEqual(displayed) && entries.childCount != 0) return;
        entries.Unbind();
        entries.Clear();
        displayed = current;
        if (current.Length == 0)
            entries.Add(new Label("상호작용 요소 없음 — 필요한 방에만 추가하세요."));
        foreach (var interaction in current)
        {
            var foldout = new Foldout { text = interaction.name, value = true };
            foldout.Add(new Button(() => Selection.activeGameObject = interaction.gameObject) { text = "씬에서 선택" });
            var serialized = new SerializedObject(interaction);
            foldout.Add(new PropertyField(serialized.FindProperty("interactionName"), "표시 이름"));
            foldout.Add(new PropertyField(serialized.FindProperty("once"), "인스턴스당 한 번"));
            foldout.Add(new PropertyField(serialized.FindProperty("onInteracted"), "상호작용 시 실행"));
            foldout.Bind(serialized);
            entries.Add(foldout);
        }
    }
}
