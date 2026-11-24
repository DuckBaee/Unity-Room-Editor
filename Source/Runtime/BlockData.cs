using System.Collections.Generic;
using UnityEngine;

public class BlockData : ScriptableObject
{
    public string blockId;
    public List<Vector3Int> cells = new List<Vector3Int>();
    public List<PortalData> portals = new List<PortalData>();
    public Bounds localBounds;
    public GameObject visualPrefab;

    // §7 V03: 공간 검사 결과. 편집기에서 저장 시 갱신. Unchecked = 미검사 또는 무효화됨.
    public SpaceCheckState spaceCheckState = SpaceCheckState.Unchecked;
    public const int CurrentSpaceCheckVersion = 2;
    public int spaceCheckVersion;
    [HideInInspector] public string spaceCheckFingerprint;
    public bool IsSpaceValidated
    {
        get
        {
            if (spaceCheckState != SpaceCheckState.Valid || spaceCheckVersion != CurrentSpaceCheckVersion) return false;
#if UNITY_EDITOR
            return RoomValidationFingerprint.IsCurrent(this);
#else
            return true;
#endif
        }
    }
}

public enum SpaceCheckState
{
    Unchecked,     // 아직 검사하지 않았거나 변경으로 무효화됨
    Valid,         // 모든 Collider가 예약 공간 안에 있음
    Inconclusive = 3, // 지원 제한/연산 한도: 일반 생성 사용 불가
    HasViolations = 2, // 예약 공간 밖에 Collider가 있음 — 일반 생성 사용 불가
}
