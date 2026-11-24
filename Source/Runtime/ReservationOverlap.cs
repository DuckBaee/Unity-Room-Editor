using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public static class ReservationOverlap
{
    public static bool Overlaps(ReservationBox a, ReservationBox b, ref int timer, float tolerance = 0.0001f)
    {
        timer++;
        // 전제: 양수 크기, Y축 회전만 사용
        tolerance = Mathf.Max(0f, tolerance);

        Vector3 delta = b.center - a.center;

        float heightRadius = (a.size.y + b.size.y) * 0.5f;

        // 높이가 떨어져 있거나, 맞닿기만 하면 겹치지 않음
        if (Mathf.Abs(delta.y) >= heightRadius - tolerance)
        {
            return false;
        }

        Vector3 aRight = a.rotation * Vector3.right;
        Vector3 aForward = a.rotation * Vector3.forward;

        Vector3 bRight = b.rotation * Vector3.right;
        Vector3 bForward = b.rotation * Vector3.forward;

        if (IsSeparated(a, b, delta, aRight))
        {
            return false;
        }

        if (IsSeparated(a, b, delta, aForward))
        {
            return false;
        }

        if (IsSeparated(a, b, delta, bRight))
        {
            return false;
        }

        if (IsSeparated(a, b, delta, bForward))
        {
            return false;
        } 
        
        return true;

        bool IsSeparated(ReservationBox boxA, ReservationBox boxB, Vector3 centerDelta, Vector3 axis)
        {
            float distance = Mathf.Abs(Vector3.Dot(centerDelta, axis));

            float radiusA = ProjectedRadius(boxA, axis);
            float radiusB = ProjectedRadius(boxB, axis);

            return distance >= radiusA + radiusB - tolerance;
        }
    }

    private static float ProjectedRadius(ReservationBox box, Vector3 axis)
    {
        Vector3 right = box.rotation * Vector3.right;
        Vector3 forward = box.rotation * Vector3.forward;

        return Mathf.Abs(Vector3.Dot(right, axis)) * box.size.x * 0.5f + Mathf.Abs(Vector3.Dot(forward, axis)) * box.size.z * 0.5f;
    }
    
    public static bool AnyOverlap(IReadOnlyList<ReservationBox> boxesA, IReadOnlyList<ReservationBox> boxesB, ref int timer)
    {
        foreach (ReservationBox a in boxesA)
        {
            foreach (ReservationBox b in boxesB)
            {
                if (Overlaps(a, b, ref timer))
                    return true;
            }
        }

        return false;
    }
}