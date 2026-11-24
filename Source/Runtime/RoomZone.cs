using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public sealed class RoomZone : MonoBehaviour
{
    private readonly Dictionary<Collider, GameObject> tracked = new Dictionary<Collider, GameObject>();
    private readonly Dictionary<GameObject, float> lastStay = new Dictionary<GameObject, float>();
    private void Reset() { var col = GetComponent<Collider>(); if (col != null) col.isTrigger = true; }
    [SerializeField] private bool filterByTag;
    [SerializeField] private string targetTag = "Player";
    [SerializeField] private bool filterByLayer;
    [SerializeField] private LayerMask targetLayerMask = ~0;
    [SerializeField] private float stayInterval = 1f;
    [SerializeField] private UnityEvent<GameObject> onEntered = new UnityEvent<GameObject>();
    [SerializeField] private UnityEvent<GameObject> onExited  = new UnityEvent<GameObject>();
    [SerializeField] private UnityEvent<GameObject> onStayed  = new UnityEvent<GameObject>();

    // actor → number of overlapping colliders (multi-collider deduplication)
    private readonly Dictionary<GameObject, int> _overlap  = new Dictionary<GameObject, int>();
    // actor → next allowed stay-event time
    private readonly Dictionary<GameObject, float> _nextStay = new Dictionary<GameObject, float>();

    public RoomRuntime Room => GetComponentInParent<BlockInstance>()?.Runtime;
    private bool CanFireEvent => Application.isPlaying && isActiveAndEnabled && Room != null && Room.Level.IsActive;

    public UnityEvent<GameObject> OnEntered => onEntered;
    public UnityEvent<GameObject> OnExited  => onExited;
    public UnityEvent<GameObject> OnStayed  => onStayed;

    private static GameObject ActorOf(Collider col) =>
        col.attachedRigidbody != null ? col.attachedRigidbody.gameObject : col.gameObject;

    private bool PassesFilter(GameObject actor)
    {
        if (filterByTag  && actor.tag != targetTag) return false;
        if (filterByLayer && (targetLayerMask.value & (1 << actor.layer)) == 0) return false;
        return true;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!CanFireEvent || other == null || tracked.ContainsKey(other)) return;
        var actor = ActorOf(other);
        if (!PassesFilter(actor)) return;
        tracked.Add(other, actor);
        _overlap.TryGetValue(actor, out int n);
        _overlap[actor] = n + 1;
        if (n == 0)
        {
            _nextStay[actor] = Time.time + (float.IsNaN(stayInterval) ? 1f : Mathf.Max(0f, stayInterval));
            if (CanFireEvent) onEntered.Invoke(actor);
        }
    }

    private void OnTriggerStay(Collider other)
    {
        if (!CanFireEvent || other == null) return;
        if (!tracked.ContainsKey(other)) OnTriggerEnter(other);
        var actor = ActorOf(other);
        if (!PassesFilter(actor) || !_overlap.ContainsKey(actor)) return;
        if (_nextStay.TryGetValue(actor, out float t) && Time.time >= t &&
            (!lastStay.TryGetValue(actor, out float last) || last != Time.fixedTime))
        {
            _nextStay[actor] = Time.time + (float.IsNaN(stayInterval) ? 1f : Mathf.Max(0f, stayInterval));
            lastStay[actor] = Time.fixedTime;
            onStayed.Invoke(actor);
        }
    }

    private void OnTriggerExit(Collider other)
    {
        if (!tracked.TryGetValue(other, out var actor)) return;
        tracked.Remove(other);
        if (!_overlap.TryGetValue(actor, out int n)) return;
        if (n <= 1)
        {
            _overlap.Remove(actor);
            _nextStay.Remove(actor);
            lastStay.Remove(actor);
            if (CanFireEvent && actor != null) onExited.Invoke(actor);
        }
        else
        {
            _overlap[actor] = n - 1;
        }
    }

    private void FixedUpdate()
    {
        if (!CanFireEvent) { ClearTracking(); return; }
        var stale = new List<Collider>();
        foreach (var pair in tracked)
            if (pair.Key == null || !pair.Key.enabled || !pair.Key.gameObject.activeInHierarchy ||
                pair.Value == null || !PassesFilter(pair.Value)) stale.Add(pair.Key);
        foreach (var col in stale) OnTriggerExit(col);
    }

    private void OnDisable() => ClearTracking();
    private void OnDestroy() => ClearTracking();

    private void ClearTracking()
    {
        tracked.Clear();
        lastStay.Clear();
        _overlap.Clear();
        _nextStay.Clear();
    }
}
