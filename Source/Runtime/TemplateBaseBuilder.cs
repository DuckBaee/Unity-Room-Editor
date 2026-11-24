using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using UnityEngine;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;

public class TemplateBaseBuilder : MonoBehaviour
{
    [SerializeField, Tooltip("지정하면 아래 기존 목록 대신 이 라이브러리의 후보만 사용합니다.")]
    private BlockLibrarySO blockLibrary;
    
    [SerializeField] private List<BlockData> startBlockData = new List<BlockData>();
    private List<int> _startBlockShuffleIndex;
    [SerializeField] private List<BlockData> connectorBlockData = new List<BlockData>();
    private List<int> _connectorBlockShuffleIndex;
    [SerializeField] private List<BlockData> objectiveBlockData = new List<BlockData>();
    private List<int> _objectiveBlockShuffleIndex;
    [SerializeField] private List<BlockData> intermediateBlockData = new List<BlockData>();
    private List<int> _intermediateBlockShuffleIndex;
    [SerializeField] private List<BlockData> exitBlockData = new List<BlockData>();
    private List<int> _exitBlockShuffleIndex;
    [SerializeField] private List<BlockData> capBlockData = new List<BlockData>();

    private TemplateScenarioSO _templateScenarioSo;
    private readonly List<PlacedBlock> _placedBlocks = new List<PlacedBlock>();
    private readonly Dictionary<int, PlacedBlock> _uniqueOwners = new Dictionary<int, PlacedBlock>();
    private List<GameObject> SpawnedBlocks = new List<GameObject>();
    private LevelRuntime runtime;
    private BlockData _block;
    
    Stopwatch sw = new Stopwatch();
    private int sw1 = 0;
    
    List<int> indices =  new List<int>();

    private void Process()
    {
        sw.Reset();
        sw1 = 0;
        
        sw.Start();
        if (_templateScenarioSo == null || _templateScenarioSo.ScenarioCount == 0)
        {
            Debug.LogWarning("시나리오가 비어 있습니다.", this);
            return;
        }

        ScenarioStep firstStep = _templateScenarioSo.GetStep(0);
        if (!ValidateScenario()) return;
        IReadOnlyList<BlockData> startPool = GetStepPool(firstStep, false);

        if (firstStep == null || !firstStep.IsStart || startPool == null || startPool.Count == 0)
        {
            Debug.LogWarning("첫 단계와 시작 블록 후보를 확인하세요.", this);
            return;
        }

        _startBlockShuffleIndex = Enumerable.Range(0, startBlockData.Count).ToList();
        _connectorBlockShuffleIndex = Enumerable.Range(0, connectorBlockData.Count).ToList();
        _objectiveBlockShuffleIndex = Enumerable.Range(0, objectiveBlockData.Count).ToList();
        _intermediateBlockShuffleIndex = Enumerable.Range(0, intermediateBlockData.Count).ToList();
        _exitBlockShuffleIndex = Enumerable.Range(0, exitBlockData.Count).ToList();

        _placedBlocks.Clear();
        _uniqueOwners.Clear();

        Pose startPose = new Pose(transform.position, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));

        List<int> indices = ShuffleList(Enumerable.Range(0, startPool.Count).ToList());

        BlockData data = startPool[indices[0]];

        if (data == null)
        {
            Debug.LogWarning("시작 블록 후보가 null입니다.", this);
            return;
        }

        if (!data.IsSpaceValidated)
        {
            Debug.LogWarning("시작 방의 공간 검증이 완료되지 않았습니다. 방 편집기에서 검증 후 저장하세요.", this);
            return;
        }
        var first = new PlacedBlock(data, startPose);
        first.depth = 0;
        
        _placedBlocks.Add(first);
        
        if (firstStep.uniqueAtDepth)
        {
            _uniqueOwners.Add(0, first);
        }

        bool success = ExpandBlock(first, 1);

        if (success)
        {
            FinalizePortals();
            SpawnFinalLayout();
        }

        
        Debug.Log(success ? "시나리오 배치 완료" : "시나리오 배치 실패", this);
        sw.Stop();
        Debug.Log("Process elapsed Time : " + sw.Elapsed);
        Debug.Log("Reservation Box calculate calls : " + sw1);
    }

    private bool TryPlaceFromPortal(PlacedBlock parent, int parentPortalIndex, int depth)
    {
        // 모든 단계를 배치한 경우
        if (depth >= _templateScenarioSo.ScenarioCount)
            return true;

        ScenarioStep step = _templateScenarioSo.GetStep(depth);
        if (step == null)
            return false;

        bool useTerminalFallback = step.uniqueAtDepth && _uniqueOwners.ContainsKey(depth);
        string selectedName = useTerminalFallback ? "Intermediate (대체)" : step.DisplayName;
        IReadOnlyList<BlockData> pool = GetStepPool(step, useTerminalFallback);

        if (pool == null || pool.Count == 0)
        {
            Debug.LogWarning($"깊이 {depth}: {selectedName} 후보 풀이 비어 있습니다.", this);
            return false;
        }

        if (parentPortalIndex < 0)
            return false;

        PortalData parentPortal = parent.data.portals[parentPortalIndex];
        indices = ShuffleList(Enumerable.Range(0, pool.Count).ToList());

        foreach (int index in indices)
        {
            BlockData data = pool[index];
            if (data == null) continue;

            // §2 예외 1: 호환 가능한 입구 목록을 셔플해 차례대로 시도.
            // 즉시 배치 실패뿐 아니라 확장 실패로 롤백된 경우에도 남은 입구를 시도한다.
            List<int> entryPortals = ShuffleList(GetCompatibleEntryPortals(data, parentPortal));
            if (entryPortals.Count == 0)
            {
                Debug.Log($"깊이 {depth}: {data.name} 호환 입구 없음, 다음 후보로", this);
                continue;
            }

            foreach (int entryIndex in entryPortals)
            {
                Debug.Log($"깊이 {depth} [단계:{step.DisplayName}, 선택:{selectedName}, 말단:{useTerminalFallback || step.IsTerminal}]: {data.name} 입구 {entryIndex} 배치 시도", this);

                int checkpoint = _placedBlocks.Count;
                bool placed = TryPlaceBlock(parent, parentPortalIndex, data, entryIndex, data.name, out PlacedBlock child);
                if (!placed) continue;  // 즉시 실패 — 다음 입구로

                child.depth = depth;
                child.isTerminal = useTerminalFallback || step.IsTerminal;

                // 대체 Intermediate는 유일 단계의 소유자가 아니다.
                if (step.uniqueAtDepth && !useTerminalFallback)
                    _uniqueOwners.Add(depth, child);

                if (ExpandBlock(child, depth + 1))
                    return true;

                // 확장 실패 — 이 입구 시도를 롤백하고 다음 입구로
                RollbackTo(checkpoint);
                Debug.Log($"깊이 {depth}: {data.name} 입구 {entryIndex} 확장 실패, 다음 입구 시도", this);
            }
            Debug.Log($"깊이 {depth}: {data.name} 모든 입구 실패, 다음 후보로", this);
        }

        Debug.LogWarning($"깊이 {depth}: {step.DisplayName} 후보를 모두 시도했지만 배치 실패", this);
        return false;
    }

    // §2 예외 1·2: parentPortal과 연결 가능한 candidate의 입구 인덱스 목록 반환.
    // Wall 타입, openingSize 일치, visualPrefab 동일성을 확인한다.
    private List<int> GetCompatibleEntryPortals(BlockData candidate, PortalData parentPortal)
    {
        var result = new List<int>();
        if (candidate?.portals == null || parentPortal == null) return result;
        for (int i = 0; i < candidate.portals.Count; i++)
        {
            PortalData cp = candidate.portals[i];
            if (cp == null) continue;
            if (cp.socketType != PortalSocketType.Wall) continue;
            if (cp.openingSize.x <= 0f || cp.openingSize.y <= 0f) continue;
            if (cp.openingSize != parentPortal.openingSize) continue;
            if (cp.visualPrefab == null || cp.visualPrefab != parentPortal.visualPrefab) continue;
            result.Add(i);
        }
        return result;
    }

    private void RollbackTo(int checkpoint)
    {
        if (checkpoint < 0 || checkpoint > _placedBlocks.Count)
        {
            throw new System.ArgumentOutOfRangeException(nameof(checkpoint));
        }

        while (_placedBlocks.Count > checkpoint)
        {
            int lastIndex = _placedBlocks.Count - 1;

            PlacedBlock block = _placedBlocks[lastIndex];

            // 자신이 소유한 유일 단계만 해제
            if (_uniqueOwners.TryGetValue(block.depth, out PlacedBlock owner) && owner == block)
            {
                _uniqueOwners.Remove(block.depth);
            }

            // 부모의 연결 포털을 다시 사용 가능하게 복구
            if (block.parent != null)
            {
                block.parent.usedPortalIndices.Remove(block.parentPortalIndex);
            }

            // 제거할 블록의 사용 상태 정리
            block.usedPortalIndices.Clear();

            _placedBlocks.RemoveAt(lastIndex);

            Debug.Log($"복구: 깊이 {block.depth}, {block.data.name} 제거", this);
        }
    }

    // Search and rollback operate on data only. Materialize after Cap finalization.
    private void SpawnFinalLayout()
    {
        LevelRuntime nextRuntime = new LevelRuntime(_placedBlocks, _generationSeed);
        runtime?.Dispose();
        runtime = nextRuntime;

        foreach (GameObject block in SpawnedBlocks)
        {
            if (block == null) continue;
            block.SetActive(false);
            Destroy(block);
        }
        SpawnedBlocks.Clear();

        // 방 아트 프리팹 인스턴스화
        foreach (PlacedBlock placed in _placedBlocks)
            CreateBlock(placed, runtime.GetRoom(placed));

        // §단계 5: 연결 쌍 포털 표현·봉쇄 포털 표현 배치
        SpawnPortalVisuals();
    }

    private void CreateBlock(PlacedBlock placed, RoomRuntime room)
    {
        // 빈 컨테이너를 만들고 BlockInstance.Initialize 가 시각 프리팹을 자식으로 생성하게 한다.
        // 직접 Instantiate 하지 않아야 BlockInstance 가 다시 Instantiate 하는 이중 생성을 막는다.
        var blockGo = new GameObject(placed.data.name);
        blockGo.transform.SetPositionAndRotation(placed.pose.position, placed.pose.rotation);
        blockGo.transform.localScale = Vector3.one;
        SpawnedBlocks.Add(blockGo);
        blockGo.AddComponent<BlockInstance>().Initialize(room);

        // §10: 포털 visualRoot 비활성화 — 연결 쌍 표현으로 대체
        foreach (var auth in blockGo.GetComponentsInChildren<RoomPortalAuthoring>(true))
        {
            if (auth.visualRoot != null) auth.visualRoot.gameObject.SetActive(false);
            var embeddedController = auth.GetComponent<PortalVisualController>();
            if (embeddedController != null && embeddedController.blockingCollider != null)
                embeddedController.blockingCollider.enabled = false;
        }

        // §단계 5: 봉쇄된 포털 표현 (미연결 → 빨강·차단)
        SpawnSealedPortalVisuals(placed);
    }

    // §단계 5: 각 PortalConnection에 대해 디자인 프리팹 하나를 인스턴스화하고 PortalVisualController 초기화
    private void SpawnPortalVisuals()
    {
        if (runtime == null) return;
        foreach (PortalConnection conn in runtime.GetAllConnections())
        {
            if (conn.DesignPrefab == null) continue;
            var visual = Instantiate(conn.DesignPrefab, conn.WorldPosition, conn.WorldRotation);
            SpawnedBlocks.Add(visual);
            // 프리팹에 이미 컴포넌트가 있으면 AddComponent 가 null 반환하므로 GetOrAdd 패턴
            var ctrl = visual.GetComponent<PortalVisualController>()
                       ?? visual.AddComponent<PortalVisualController>();
            ctrl.Initialize(conn);
        }
    }

    // §단계 5 §10: 봉쇄 포털은 디자인 하나를 표시하고 PortalVisualController.InitializeUnconnected 호출
    private void SpawnSealedPortalVisuals(PlacedBlock placed)
    {
        foreach (int sealedIdx in placed.sealedPortalIndices)
        {
            if (sealedIdx < 0 || sealedIdx >= placed.data.portals.Count) continue;
            PortalData pd = placed.data.portals[sealedIdx];
            if (pd == null || pd.visualPrefab == null) continue;

            Vector3 worldPos = placed.pose.position + placed.pose.rotation * pd.localPosition;
            Quaternion worldRot = placed.pose.rotation * Quaternion.Euler(0f, pd.directionIndex * 45f, 0f);

            var visual = Instantiate(pd.visualPrefab, worldPos, worldRot);
            SpawnedBlocks.Add(visual);
            var ctrl = visual.GetComponent<PortalVisualController>()
                       ?? visual.AddComponent<PortalVisualController>();
            ctrl.InitializeUnconnected();
        }
    }

    private void OnDestroy() => runtime?.Dispose();
    
    private bool TryPlaceBlock(PlacedBlock target, int targetPortalIndex, BlockData newData, int newPortalIndex, string objectName, out PlacedBlock result)
    {
        result = null;

        if (target == null || !_placedBlocks.Contains(target))
        {
            Debug.LogWarning("배치 기준 블록이 목록에 없습니다.", this);
            return false;
        }

        if (target.usedPortalIndices.Contains(targetPortalIndex))
        {
            Debug.Log("배치 거부: 이미 사용한 포털입니다.", this);
            return false;
        }

        if (target.sealedPortalIndices.Contains(targetPortalIndex))
        {
            Debug.Log("배치 거부: 봉쇄된 포털입니다.", this);
            return false;
        }

        bool success = BlockPlacementCalculator.TryCalculate(target.data, target.pose, targetPortalIndex, newData, newPortalIndex, out Pose newPose);

        if (!success)
        {
            Debug.LogWarning("배치 계산 실패: 데이터와 포털 규격을 확인하세요.", this);
            return false;
        }
        
        // 신규 후보 예약 박스 생성
        List<ReservationBox> candidateBoxes = BlockReservationBuilder.Build(newData, newPose);

        // 신규 후보의 전체 AABB 계산
        Bounds candidateBounds = BlockReservationBuilder.CalculateAABB(candidateBoxes);

        // 기존 배치된 블록 순회
        foreach (PlacedBlock placed in _placedBlocks)
        {
            // 1차: AABB 검사
            if (!placed.worldBounds.Intersects(candidateBounds))
                continue;

            // 2차: OBB SAT 검사
            if (ReservationOverlap.AnyOverlap(placed.boxes, candidateBoxes, ref sw1))
            {
                Debug.Log($"배치 거부: {objectName}이 기존 블록과 겹칩니다.", this);
                return false;
            }
        }
        var candidate = new PlacedBlock(newData, newPose, candidateBoxes, candidateBounds);
        
        candidate.parent = target;
        candidate.parentPortalIndex = targetPortalIndex;
        candidate.entryPortalIndex = newPortalIndex;
        
        // 모든 검사를 통과한 뒤 배치를 확정한다.
        _placedBlocks.Add(candidate);

        target.usedPortalIndices.Add(targetPortalIndex);
        candidate.usedPortalIndices.Add(newPortalIndex);

        result = candidate;

        Debug.Log($"배치 성공: {objectName}", this);
        return true;
    }
    
    private bool ExpandBlock(PlacedBlock parent, int nextDepth)
    {
        if (parent == null)
            return false;

        // 대체 Intermediate는 더 확장하지 않는다.
        if (parent.isTerminal)
            return true;

        // 마지막 단계까지 도달했다.
        if (nextDepth >= _templateScenarioSo.ScenarioCount)
            return true;

        // 이 부모에서 새로 생성한 모든 하위 가지의 복구 기준
        int checkpoint = _placedBlocks.Count;
        bool hasOpenPortal = false;

        for (int portalIndex = 0; portalIndex < parent.data.portals.Count; portalIndex++)
        {
            // 부모와 연결된 입구 등 이미 사용한 포털은 제외
            if (parent.usedPortalIndices.Contains(portalIndex) || parent.sealedPortalIndices.Contains(portalIndex))
                continue;

            hasOpenPortal = true;

            Debug.Log($"분기 시도: {parent.data.name}, " + $"포털 {portalIndex}, 다음 깊이 {nextDepth}", this);

            if (!TryPlaceFromPortal(parent, portalIndex, nextDepth))
            {
                // 먼저 성공했던 형제 가지도 함께 제거
                RollbackTo(checkpoint);
                return false;
            }
        }

        // 다음 단계가 필요한데 열린 포털이 없다면 실패
        return hasOpenPortal;
    }

    private bool TryAttachCap(PlacedBlock parent, int portalIndex, out PlacedBlock cap)
    {
        cap = null;

        if (parent == null || !_placedBlocks.Contains(parent) ||
            parent.data == null || parent.data.portals == null ||
            portalIndex < 0 || portalIndex >= parent.data.portals.Count)
            return false;

        if (parent.usedPortalIndices.Contains(portalIndex) ||
            parent.sealedPortalIndices.Contains(portalIndex))
            return false;

        IReadOnlyList<BlockData> capPool = GetPool(ScenarioType.Cap);
        if (capPool == null)
            return false;

        var tried = new HashSet<BlockData>();
        foreach (BlockData data in capPool)
        {
            if (data == null || !tried.Add(data))
                continue;

            if (data.cells == null || data.portals == null ||
                data.portals.Count != 1 || data.portals[0] == null)
            {
                Debug.LogWarning($"Cap 데이터 확인 필요: {data.name}. 셀 목록과 정확히 하나의 포털이 필요합니다.", this);
                continue;
            }

            PortalData portal = data.portals[0];
            if (portal.useReservationBounds)
            {
                Vector3 size = portal.reservationBounds;
                if (size.x <= 0f || size.y <= 0f || size.z <= 0f)
                {
                    Debug.LogWarning($"Cap 예약 박스 크기 오류: {data.name}", this);
                    continue;
                }
            }
            else if (data.cells.Count == 0)
            {
                Debug.LogWarning($"Cap 예약 영역이 없습니다: {data.name}", this);
                continue;
            }

            Debug.Log($"Cap 시도: {parent.data.name}, 포털 {portalIndex} → {data.name}", this);
            if (!TryPlaceBlock(parent, portalIndex, data, 0, data.name, out PlacedBlock placedCap))
                continue;

            placedCap.depth = parent.depth + 1;
            placedCap.isTerminal = true;
            cap = placedCap;
            return true;
        }

        return false;
    }

    private List<(PlacedBlock parent, int portalIndex)> CollectUnresolvedPortals()
    {
        var targets = new List<(PlacedBlock parent, int portalIndex)>();

        foreach (PlacedBlock parent in _placedBlocks)
        {
            for (int i = 0; i < parent.data.portals.Count; i++)
            {
                if (parent.usedPortalIndices.Contains(i) ||
                    parent.sealedPortalIndices.Contains(i))
                    continue;

                targets.Add((parent, i));
            }
        }

        return targets;
    }

    private void FinalizePortals()
    {
        // Cap을 추가하기 전에 대상을 고정한다.
        var targets = CollectUnresolvedPortals();
        int attachedCount = 0;
        int sealedCount = 0;

        foreach (var target in targets)
        {
            PlacedBlock parent = target.parent;
            int portalIndex = target.portalIndex;

            if (TryAttachCap(parent, portalIndex, out PlacedBlock cap))
            {
                attachedCount++;
                Debug.Log($"Cap 확정: {parent.data.name}, 포털 {portalIndex} → {cap.data.name}", this);
            }
            else
            {
                parent.sealedPortalIndices.Add(portalIndex);
                sealedCount++;
                Debug.Log($"Cap 후보 소진: {parent.data.name}, 포털 {portalIndex} 봉쇄", this);
            }
        }

        Debug.Log(
            $"포털 마감 완료: 대상 {targets.Count}, Cap 연결 {attachedCount}, 봉쇄 {sealedCount}",
            this);
    }

    public void SetScenario(TemplateScenarioSO templateScenarioSo)
    {
        _templateScenarioSo = templateScenarioSo;
        Process();
    }

    // §G04: 시드 지정 오버로드 — 동일 시드·입력에서 동일 결과 재현 보장
    private int _generationSeed;
    public void SetScenario(TemplateScenarioSO templateScenarioSo, int seed)
    {
        _generationSeed = seed;
        Random.InitState(seed);
        SetScenario(templateScenarioSo);
    }

    private IReadOnlyList<BlockData> GetPool(ScenarioType type)
    {
        if (blockLibrary != null)
            return blockLibrary.GetPool(type);

        return type switch
        {
            ScenarioType.Start => startBlockData,
            ScenarioType.Connector => connectorBlockData,
            ScenarioType.Objective => objectiveBlockData,
            ScenarioType.Intermediate => intermediateBlockData,
            ScenarioType.Exit => exitBlockData,
            ScenarioType.Cap => capBlockData,
            _ => Array.Empty<BlockData>()
        };
    }

    private IReadOnlyList<BlockData> GetStepPool(ScenarioStep step, bool terminalFallback)
    {
        if (terminalFallback) return GetPool(ScenarioType.Intermediate);
        if (step.card != null) return step.card.Candidates;
        return GetPool(step.scenarioType);
    }

    private bool ValidateScenario()
    {
        for (int depth = 0; depth < _templateScenarioSo.ScenarioCount; depth++)
        {
            ScenarioStep step = _templateScenarioSo.GetStep(depth);
            if (step == null || (depth == 0 ? !step.IsStart : step.IsStart))
            {
                Debug.LogWarning($"깊이 {depth}: 첫 단계만 Start여야 하며 빈 단계는 허용하지 않습니다.", this);
                return false;
            }
            if (step.IsTerminal && depth != _templateScenarioSo.ScenarioCount - 1)
            {
                Debug.LogWarning($"깊이 {depth}: Terminal 카드는 뒤 단계를 건너뛰지 않도록 마지막에 배치하세요.", this);
                return false;
            }
            if (step.card == null && step.scenarioType == ScenarioType.Cap)
            {
                Debug.LogWarning("Cap은 시나리오 단계가 아닌 최종 마감에서 처리합니다.", this);
                return false;
            }
            if (step.card != null && !Enum.IsDefined(typeof(ScenarioCardRole), step.card.Role))
            {
                Debug.LogWarning($"깊이 {depth}: 카드 배치 역할이 올바르지 않습니다.", this);
                return false;
            }
        }
        return true;
    }
    
    //Utillity
 
    List<T> ShuffleList<T>(List<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            // 0부터 i 사이의 무작위 인덱스 선택
            int randomIndex = Random.Range(0, i + 1);

            // 값 교환 (Swap)
            (list[i], list[randomIndex]) = (list[randomIndex], list[i]);
        }
        
        return list;
    }
}
