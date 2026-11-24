using System;
using System.Collections.Generic;
using UnityEngine;

// Runtime state belongs to one finalized layout, never to the shared BlockData asset.
public sealed class LevelRuntime : IDisposable
{
    private readonly Dictionary<PlacedBlock, RoomRuntime> rooms = new();
    public Guid Id { get; } = Guid.NewGuid();
    public bool IsActive { get; private set; } = true;
    public int GenerationSeed { get; }

    public LevelRuntime(IReadOnlyList<PlacedBlock> blocks, int generationSeed = 0)
    {
        GenerationSeed = generationSeed;
        foreach (PlacedBlock block in blocks)
            rooms.Add(block, new RoomRuntime(this, block.data));

        foreach (PlacedBlock block in blocks)
        {
            if (block.parent == null) continue;
            RoomRuntime parent = rooms[block.parent];
            RoomRuntime child = rooms[block];
            int a = block.parentPortalIndex;
            int b = block.entryPortalIndex;
            if (a < 0 || a >= parent.PortalCount || b < 0 || b >= child.PortalCount ||
                parent.GetConnection(a) != null || child.GetConnection(b) != null ||
                block.parent.sealedPortalIndices.Contains(a) || block.sealedPortalIndices.Contains(b))
                throw new InvalidOperationException("Final layout contains an invalid portal connection.");

            var connection = new PortalConnection(this);
            connection.SetRooms(parent, child);

            // §단계 5: 포털 세계 좌표와 디자인 프리팹 저장 — SpawnFinalLayout에서 포털 표현 배치에 사용
            PortalData ppd = block.parent.data.portals[a];
            Pose ppose = block.parent.pose;
            Vector3 pWorldPos = ppose.position + ppose.rotation * ppd.localPosition;
            Quaternion pWorldRot = ppose.rotation * Quaternion.Euler(0f, ppd.directionIndex * 45f, 0f);
            connection.SetPortalInfo(pWorldPos, pWorldRot, ppd.visualPrefab);

            parent.Connect(a, connection);
            child.Connect(b, connection);
        }
    }

    public RoomRuntime GetRoom(PlacedBlock block) => rooms[block];

    // §단계 5: 레이아웃의 모든 연결(중복 없이)을 열거한다.
    public IEnumerable<PortalConnection> GetAllConnections()
    {
        var seen = new HashSet<PortalConnection>();
        foreach (RoomRuntime room in rooms.Values)
            for (int i = 0; i < room.PortalCount; i++)
            {
                PortalConnection c = room.GetConnection(i);
                if (c != null && seen.Add(c)) yield return c;
            }
    }

    public void Dispose()
    {
        if (!IsActive) return;
        IsActive = false;
        foreach (RoomRuntime room in rooms.Values)
            for (int i = 0; i < room.PortalCount; i++)
                room.GetConnection(i)?.Invalidate();
    }
}

public sealed class RoomRuntime
{
    private readonly PortalConnection[] connections;
    public Guid Id { get; } = Guid.NewGuid();
    public LevelRuntime Level { get; }
    public BlockData Data { get; }
    public int PortalCount => connections.Length;

    internal RoomRuntime(LevelRuntime level, BlockData data)
    {
        Level = level;
        Data = data;
        connections = new PortalConnection[data.portals.Count];
    }

    // Indices refer to this generation's data. Persistent authoring IDs are a later step.
    public PortalConnection GetConnection(int index) =>
        index >= 0 && index < connections.Length ? connections[index] : null;

    internal void Connect(int index, PortalConnection connection) => connections[index] = connection;

    // §3.3 방 단위 통행 차단 상태.
    // true이면 이 방에 연결된 모든 포털이 Blocked로 계산된다.
    public bool IsBlocked { get; private set; }

    public void SetBlocked(bool blocked)
    {
        if (IsBlocked == blocked) return;
        IsBlocked = blocked;
        for (int i = 0; i < connections.Length; i++)
            connections[i]?.NotifyRoomStateChanged();
    }
}

public sealed class PortalConnection
{
    private readonly LevelRuntime level;
    private RoomRuntime _roomA, _roomB;

    public bool IsActive => level.IsActive;
    public bool IsOpen { get; private set; }
    public event Action Changed;

    internal PortalConnection(LevelRuntime level) => this.level = level;

    // §3.3 포털 상태 계산.
    // 1. 비활성 → Disconnected
    // 2. 연결된 방 중 하나라도 IsBlocked → Blocked
    // 3. 나머지 → Passable
    public PortalState State
    {
        get
        {
            if (!IsActive) return PortalState.Disconnected;
            if ((_roomA != null && _roomA.IsBlocked) || (_roomB != null && _roomB.IsBlocked))
                return PortalState.Blocked;
            return PortalState.Passable;
        }
    }

    internal void NotifyRoomStateChanged() => Changed?.Invoke();

    internal void SetRooms(RoomRuntime a, RoomRuntime b) { _roomA = a; _roomB = b; }

    // §단계 5: 포털 세계 좌표와 디자인 프리팹 — SpawnFinalLayout이 포털 표현을 배치할 때 사용한다.
    public Vector3 WorldPosition { get; private set; }
    public Quaternion WorldRotation { get; private set; }
    public GameObject DesignPrefab { get; private set; }

    internal void SetPortalInfo(Vector3 pos, Quaternion rot, GameObject design)
    {
        WorldPosition = pos;
        WorldRotation = rot;
        DesignPrefab = design;
    }

    public bool TryOpen()
    {
        if (!IsActive) return false;
        if (!IsOpen) { IsOpen = true; Changed?.Invoke(); }
        return true;
    }

    public bool TryClose()
    {
        if (!IsActive) return false;
        if (IsOpen) { IsOpen = false; Changed?.Invoke(); }
        return true;
    }

    internal void Invalidate()
    {
        Changed = null;
        _roomA = null; _roomB = null;
        IsOpen = false;
    }
}
