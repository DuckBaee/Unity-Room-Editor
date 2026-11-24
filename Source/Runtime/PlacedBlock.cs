using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

public class PlacedBlock
{
    public BlockData data;
    public PlacedBlock parent;
    public Pose pose;
    public List<ReservationBox> boxes;
    public int depth;
    public bool isTerminal;
    public int parentPortalIndex = -1;
    public int entryPortalIndex = -1;
    public Bounds worldBounds;
    
    public HashSet<int> usedPortalIndices = new HashSet<int>();
    public HashSet<int> sealedPortalIndices = new HashSet<int>();

    public PlacedBlock(BlockData data, Pose pose)
    {
        this.data = data;
        this.pose = pose;
        boxes = BlockReservationBuilder.Build(data, pose);
        worldBounds = BlockReservationBuilder.CalculateAABB(boxes);
    }
    
    public PlacedBlock(BlockData data, Pose pose, List<ReservationBox> boxes, Bounds worldBounds)
    {
        this.data = data;
        this.pose = pose;
        this.boxes = boxes;
        this.worldBounds = worldBounds;
    }
    public int GetAvailablePortalIndex()
    {
        for (int i = 0; i < data.portals.Count; i++)
        {
            if (!usedPortalIndices.Contains(i) && !sealedPortalIndices.Contains(i))
            {
                return i;
            }
        }
        return -1;
    }
}