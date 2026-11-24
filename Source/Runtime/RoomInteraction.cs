using UnityEngine;
using UnityEngine.Events;

// Stored with the room art; execution state belongs to each spawned instance.
[DisallowMultipleComponent]
public sealed class RoomInteraction : MonoBehaviour
{
    [SerializeField] private string interactionName = "상호작용";
    [SerializeField] private bool once;
    [SerializeField] private UnityEvent onInteracted = new UnityEvent();

    private bool consumed;
    private bool invoking;
    public string InteractionName => interactionName;
    public bool IsConsumed => consumed;
    public UnityEvent OnInteracted => onInteracted;
    public RoomRuntime Room => GetComponentInParent<BlockInstance>()?.Runtime;
    public bool CanInteract => Application.isPlaying && isActiveAndEnabled &&
        !invoking && (!once || !consumed) && Room != null && Room.Level.IsActive;

    // A game's input/raycast system calls this; no player controller dependency.
    public void Interact() => TryInteract();

    public bool TryInteract()
    {
        if (!CanInteract) return false;
        invoking = true;
        consumed = true;
        try { onInteracted.Invoke(); }
        finally { invoking = false; }
        return true;
    }

    public void BlockRoom() => SetRoomBlocked(true);
    public void UnblockRoom() => SetRoomBlocked(false);

    private void SetRoomBlocked(bool blocked)
    {
        if (!Application.isPlaying || !isActiveAndEnabled) return;
        var room = Room;
        if (room != null && room.Level.IsActive) room.SetBlocked(blocked);
    }
}
