using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public static class BlockReservationBuilder
{
    public static List<ReservationBox> Build(BlockData data, Pose pose)
    {
        
        if (data == null || data.cells == null || data.portals == null)
        {
            throw new ArgumentException("유효하지 않은 블록 데이터");
        }

        var boxes = new List<ReservationBox>();

        foreach (var cell in data.cells)
        {
            Vector3 localCenter = cell + Vector3.one * 0.5f;
            Vector3 worldCenter = pose.position + pose.rotation * localCenter;

            boxes.Add(new ReservationBox(worldCenter, Vector3.one, pose.rotation));
        }

        foreach (PortalData portal in data.portals)
        {
            if (portal == null)
            {
                throw new ArgumentException("포탈 데이터가 비어 있습니다.");
            }
            
            if(!portal.useReservationBounds)
                continue;

            Vector3 size = portal.reservationBounds;

            if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
            {
                throw new ArgumentException("예약박스 크기는 항상 0보다 큰 양수여야 합니다.");
            }
            
            Quaternion portalRotation = Quaternion.Euler(0f, portal.directionIndex * 45f, 0f);

            Vector3 inwardOffset = new Vector3(0f, 0f, -size.z * 0.5f);
            
            Vector3 localCenter = portal.localPosition + portalRotation * inwardOffset;
            
            Vector3 worldCenter = pose.position + pose.rotation * localCenter;
            
            Quaternion worldRotation = pose.rotation * portalRotation;
            
            boxes.Add(new ReservationBox(worldCenter, size, worldRotation));
        }
        
        return boxes;
    }
    
    public static Bounds CalculateAABB(IReadOnlyList<ReservationBox> boxes)
    {
        if (boxes == null || boxes.Count == 0)
            throw new ArgumentException("예약 박스가 없습니다.");

        Bounds bounds = ToAABB(boxes[0]);

        for (int i = 1; i < boxes.Count; i++)
        {
            bounds.Encapsulate(ToAABB(boxes[i]));
        }

        return bounds;
    }

    private static Bounds ToAABB(ReservationBox box)
    {
        // 회전된 박스의 월드 방향 벡터
        Vector3 right = box.rotation * Vector3.right;
        Vector3 up = box.rotation * Vector3.up;
        Vector3 forward = box.rotation * Vector3.forward;

        Vector3 half = box.size * 0.5f;

        // 월드 축 기준 AABB 반크기
        Vector3 extent = new Vector3(
            Mathf.Abs(right.x) * half.x +
            Mathf.Abs(up.x) * half.y +
            Mathf.Abs(forward.x) * half.z,

            Mathf.Abs(right.y) * half.x +
            Mathf.Abs(up.y) * half.y +
            Mathf.Abs(forward.y) * half.z,

            Mathf.Abs(right.z) * half.x +
            Mathf.Abs(up.z) * half.y +
            Mathf.Abs(forward.z) * half.z
        );

        return new Bounds(box.center, extent * 2f);
    }
}
