// §3.3 포털 상태 열거형
// 상태 계산: 미연결 → Disconnected, 한쪽이라도 차단 → Blocked, 나머지 → Passable
public enum PortalState
{
    Passable,     // 초록 — 통행 가능
    Blocked,      // 주황 — 방 진행으로 잠김
    Disconnected, // 빨강 — 미연결
}
