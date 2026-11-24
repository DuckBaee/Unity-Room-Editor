using UnityEngine;

public class BlockInstance : MonoBehaviour
{
    [SerializeField] private BlockData data;
    [SerializeField, HideInInspector] private GameObject visualInstance;
    public RoomRuntime Runtime { get; private set; }

    public void Initialize(RoomRuntime runtime)
    {
        InitializeVisual(runtime.Data, runtime);
    }

    public void Initialize(BlockData blockData)
    {
        InitializeVisual(blockData, null);
    }

    private void InitializeVisual(BlockData blockData, RoomRuntime runtime)
    {
        // Reinitialization replaces only the visual owned by this instance.
        if (visualInstance != null)
        {
            visualInstance.SetActive(false);
            if (Application.isPlaying)
                Destroy(visualInstance);
            else
                DestroyImmediate(visualInstance);
            visualInstance = null;
        }

        Runtime = runtime;
        data = blockData;
        if (data == null || data.visualPrefab == null)
            return;

        visualInstance = Instantiate(data.visualPrefab, transform, false);
        visualInstance.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
        visualInstance.transform.localScale = Vector3.one;
        foreach (var spawn in visualInstance.GetComponentsInChildren<RoomConditionalSpawn>(true))
            spawn.Initialize(this);
    }
    
    private void OnDrawGizmos()
    {
        if (data == null)
            return;

        Matrix4x4 previousMatrix = Gizmos.matrix;
        Color previousColor = Gizmos.color;

        Gizmos.matrix = transform.localToWorldMatrix;
        Gizmos.color = Color.cyan;

        foreach (Vector3Int cell in data.cells)
        {
            Gizmos.DrawWireCube((Vector3)cell + Vector3.one * 0.5f, Vector3.one);
        }

        foreach (PortalData portal in data.portals)
        {
            if (portal == null)
                continue;

            Quaternion rotation = Quaternion.Euler(0f, portal.directionIndex * 45f, 0f);

            Gizmos.matrix = transform.localToWorldMatrix * Matrix4x4.TRS(portal.localPosition, rotation, Vector3.one);

            Gizmos.color = Color.yellow;
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(portal.openingSize.x, portal.openingSize.y, 0f));
            Gizmos.DrawLine(Vector3.zero, Vector3.forward);

            Vector3 size = portal.reservationBounds;

            if (portal.useReservationBounds && size.x > 0f && size.y > 0f && size.z > 0f)
            {
                Gizmos.color = Color.magenta;
                Gizmos.DrawWireCube(new Vector3(0f, 0f, -size.z * 0.5f), size);
            }
        }

        Gizmos.matrix = previousMatrix;
        Gizmos.color = previousColor;
    }
}
