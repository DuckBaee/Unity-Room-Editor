using UnityEngine;

// §6 포털 프리팹 컴포넌트.
// 포털 프리팹에 하나만 부착한다. 연결 규격과 표현용 하위 루트를 선언한다.
// 위치·방향은 방 편집기에서 인스턴스별로 조정한다.
[DisallowMultipleComponent]
public sealed class RoomPortalAuthoring : MonoBehaviour
{
    // 포털 고유 ID — 생성 시 발급. 이동·회전·깊이 변경 시 유지.
    // 복제·신규 배치 시 방 편집기가 새 ID를 발급한다.
    [HideInInspector] public string portalId;

    // Stable design identity, independent of the containing room prefab.
    [HideInInspector] public GameObject designPrefab;

    // 소켓 종류 — PortalData에서 복원. FloorCeiling은 이번 범위에서 미지원.
    [HideInInspector] public PortalSocketType socketType = PortalSocketType.Wall;

    // 연결 규격 — 프리팹에서 선언한 값. 같은 openingSize끼리만 연결된다.
    public Vector2 openingSize = new Vector2(2f, 3f);

    // 추가 예약 깊이 — 각 인스턴스에서 개별 조정. 0 = 기본 깊이만 사용.
    [Min(0f)] public float reservationDepth = 0f;

    // 표현용 하위 루트 — 디자인 모델을 담는 Transform.
    // 맵 조립 시 비활성화하고 연결 결과 표현으로 대체한다 (§10).
    public Transform visualRoot;
}
