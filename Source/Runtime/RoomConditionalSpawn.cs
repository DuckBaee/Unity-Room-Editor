using System;
using System.Collections.Generic;
using UnityEngine;

// BlockInstance가 외형 생성 후 비활성 하위 요소까지 한 번 초기화한다.
// 생성 시드, 방 위치, 데이터와 컴포넌트 순서를 사용하며 편집 모드 원본은 변경하지 않는다.
public sealed class RoomConditionalSpawn : MonoBehaviour
{
    [Serializable]
    public sealed class Entry
    {
        public GameObject target;
        [Range(0f, 1f)]
        public float probability = 1f;
    }

    [SerializeField] private List<Entry> entries = new List<Entry>();
    public IReadOnlyList<Entry> Entries => entries;

    private bool initialized;

    public void Initialize(BlockInstance bi)
    {
        if (!Application.isPlaying || initialized || bi?.Runtime == null || !bi.Runtime.Level.IsActive) return;
        if (GetComponentInParent<BlockInstance>() != bi) return;
        initialized = true;
        var pos = bi.transform.position;
        int seed = bi.Runtime.Level.GenerationSeed;
        unchecked
        {
            foreach (char c in bi.Runtime.Data.blockId ?? "") seed = seed * 31 + c;
            seed = seed * 31 + Mathf.RoundToInt(pos.x * 100);
            seed = seed * 31 + Mathf.RoundToInt(pos.y * 100);
            seed = seed * 31 + Mathf.RoundToInt(pos.z * 100);
            for (var t = transform; t != bi.transform; t = t.parent)
                seed = seed * 31 + t.GetSiblingIndex();
            var peers = GetComponents<RoomConditionalSpawn>();
            seed = seed * 31 + Array.IndexOf(peers, this);
        }
        var rng = new System.Random(seed);
        var seen = new HashSet<GameObject>();
        foreach (var entry in entries)
        {
            if (entry == null || entry.target == null || !seen.Add(entry.target)) continue;
            if (entry.target == bi.gameObject || !entry.target.transform.IsChildOf(bi.transform)) continue;
            float chance = float.IsNaN(entry.probability) ? 0f : Mathf.Clamp01(entry.probability);
            entry.target.SetActive(rng.NextDouble() < chance);
        }
    }
}
