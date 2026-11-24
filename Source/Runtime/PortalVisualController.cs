using UnityEngine;

// §단계 5 §10: 포털 연결 쌍의 시각 표현 컨트롤러.
// PortalConnection.Changed 이벤트에 반응하여 색상과 차단 Collider를 갱신한다.
//
// 연결됨·통행 가능 → 초록  |  방 잠김 → 주황  |  미연결 → 빨강·차단
[DisallowMultipleComponent]
public sealed class PortalVisualController : MonoBehaviour
{
    // 비활성 상태(미연결·잠김)에서 통행을 차단하는 Collider
    public Collider blockingCollider;

    // 색상을 적용할 Renderer 목록 (비어 있으면 자식에서 자동 수집)
    public Renderer[] colorRenderers;

    private PortalConnection _connection;
    private static readonly int ColorPropId     = Shader.PropertyToID("_Color");
    private static readonly int BaseColorPropId = Shader.PropertyToID("_BaseColor");

    private static readonly Color ColPassable    = Color.green;
    private static readonly Color ColBlocked     = new Color(1f, 0.5f, 0f);
    private static readonly Color ColDisconnected = Color.red;

    // 연결이 없는 경우 (미연결 포털)에 초기화
    public void InitializeUnconnected()
    {
        ApplyColor(ColDisconnected);
        SetBlocker(true);
    }

    // 연결된 포털 쌍에 초기화
    public void Initialize(PortalConnection conn)
    {
        _connection = conn;
        conn.Changed += OnChanged;
        OnChanged();
    }

    private void Awake()
    {
        // colorRenderers가 비어 있으면 자식 Renderer를 자동 수집
        if (colorRenderers == null || colorRenderers.Length == 0)
            colorRenderers = GetComponentsInChildren<Renderer>(true);
    }

    private void OnDestroy()
    {
        if (_connection != null)
            _connection.Changed -= OnChanged;
    }

    private void OnChanged()
    {
        if (_connection == null) return;
        PortalState state = _connection.State;

        Color col = state switch
        {
            PortalState.Passable     => ColPassable,
            PortalState.Blocked      => ColBlocked,
            PortalState.Disconnected => ColDisconnected,
            _                        => Color.white,
        };

        ApplyColor(col);
        SetBlocker(state != PortalState.Passable);
    }

    private void ApplyColor(Color col)
    {
        if (colorRenderers == null) return;
        foreach (var r in colorRenderers)
        {
            if (r == null) continue;
            var mpb = new MaterialPropertyBlock();
            r.GetPropertyBlock(mpb);
            mpb.SetColor(ColorPropId, col);
            mpb.SetColor(BaseColorPropId, col);  // URP/Lit 셰이더 대응
            r.SetPropertyBlock(mpb);
        }
    }

    private void SetBlocker(bool active)
    {
        if (blockingCollider != null)
            blockingCollider.enabled = active;
    }
}
