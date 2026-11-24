using UnityEngine;

public static class BlockPlacementCalculator
{
    public static bool TryCalculate(BlockData fixedBlock, Pose fixedPose, int fixedPortalIndex, BlockData candidateBlock, int candidatePortalIndex, out Pose candidatePose)
    {
        candidatePose = new Pose(Vector3.zero, Quaternion.identity);

        if (fixedBlock == null || candidateBlock == null || fixedBlock.portals == null || candidateBlock.portals == null)
            return false;

        // §7 V03: 공간 검사 위반이 있는 블록은 일반 생성에 사용 불가
        if (!candidateBlock.IsSpaceValidated || !fixedBlock.IsSpaceValidated)
            return false;

        if (fixedPortalIndex < 0 || fixedPortalIndex >= fixedBlock.portals.Count || candidatePortalIndex < 0 || candidatePortalIndex >= candidateBlock.portals.Count)
            return false;

        PortalData fixedPortal = fixedBlock.portals[fixedPortalIndex];
        PortalData candidatePortal = candidateBlock.portals[candidatePortalIndex];

        if (fixedPortal == null || candidatePortal == null)
            return false;

        // 이번 단계에서는 벽면 포털만 지원
        if (fixedPortal.socketType != PortalSocketType.Wall || candidatePortal.socketType != PortalSocketType.Wall)
            return false;

        if (fixedPortal.directionIndex < 0 || fixedPortal.directionIndex > 7 || candidatePortal.directionIndex < 0 || candidatePortal.directionIndex > 7)
            return false;

        if (fixedPortal.openingSize.x <= 0f || fixedPortal.openingSize.y <= 0f || candidatePortal.openingSize.x <= 0f || candidatePortal.openingSize.y <= 0f || !fixedPortal.openingSize.Equals(candidatePortal.openingSize))
            return false;

        // §2 예외 2: 디자인 동일성 — 같은 원본 포털 프리팹 참조여야 연결 가능.
        // null이거나 다른 프리팹이면 연결 불가.
        if (fixedPortal.visualPrefab == null || fixedPortal.visualPrefab != candidatePortal.visualPrefab)
            return false;

        Quaternion fixedLocalRotation = Quaternion.Euler(0f, fixedPortal.directionIndex * 45f, 0f);

        Quaternion candidateLocalRotation = Quaternion.Euler(0f, candidatePortal.directionIndex * 45f, 0f);

        Quaternion fixedWorldRotation = fixedPose.rotation * fixedLocalRotation;

        Vector3 fixedWorldPosition = fixedPose.position + fixedPose.rotation * fixedPortal.localPosition;

        Quaternion candidateRotation = fixedWorldRotation * Quaternion.Euler(0f, 180f, 0f) * Quaternion.Inverse(candidateLocalRotation);

        Vector3 candidatePosition = fixedWorldPosition - candidateRotation * candidatePortal.localPosition;

        candidatePose = new Pose(candidatePosition, candidateRotation);
        return true;
    }
}