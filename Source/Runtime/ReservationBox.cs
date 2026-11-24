using UnityEngine;

public struct ReservationBox
{
    public Vector3 center;
    public Vector3 size;
    public Quaternion rotation;

    public ReservationBox(Vector3 center, Vector3 size, Quaternion rotation)
    {
        this.center = center;
        this.size = size;
        this.rotation = rotation;
    }
}
