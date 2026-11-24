using System;
using UnityEngine;

public enum PortalSocketType
{
    Wall,
    FloorCeiling,
}

[Serializable]
public class PortalData
{
    // Stable identity — preserved across position/direction/size edits.
    // Assigned once on creation; never changed by copy or load operations.
    public string portalId;

    public PortalSocketType socketType = PortalSocketType.Wall;
    public Vector3 localPosition;
    public bool useReservationBounds;
    public Vector3 reservationBounds = new Vector3(3f, 4f, 2f);

    [Range(0, 7)]
    public int directionIndex;
    public Vector2 openingSize = new Vector2(2f, 3f);
    // Optional complete model, placed at the portal pose without cell scaling.
    public GameObject visualPrefab;
}
