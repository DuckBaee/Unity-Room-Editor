using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

// §11 단계 2–3: 방 통합 편집 창
// 전용 편집 Scene, 방 루트 고정, 아트·셀 모드 전환, 포털 컴포넌트 편집
public sealed class RoomEditorWindow : EditorWindow
{
    private enum EditMode { Art, Cell }
    private enum WindowState { Idle, Editing }

    private const string RootName = "RoomRoot";

    // ── 직렬화 필드 (도메인 리로드 생존) ───────────────────────────────
    [SerializeField] private WindowState _state;
    [SerializeField] private EditMode _mode;
    [SerializeField] private List<Vector3Int> _cells = new List<Vector3Int>();
    [SerializeField] private bool _isDirty;
    [SerializeField] private string _blockDataPath = "";
    [SerializeField] private string _artPrefabPath = "";
    [SerializeField] private List<int> _initialRootIds = new List<int>();
    [SerializeField] private List<Vector3Int> _selectedCells = new List<Vector3Int>();
    [SerializeField] private int _cellInputMode; // 0=추가, 1=삭제, 2=선택
    [SerializeField] private Vector3Int _moveOffset;
    [SerializeField] private int _editingSceneHandle;
    [SerializeField] private int _previousSceneHandle;
    [Serializable]
    private sealed class SceneVisibilitySnapshot
    {
        public GameObject target;
        public bool hidden;
    }
    [SerializeField] private List<SceneVisibilitySnapshot> _isolatedObjects = new List<SceneVisibilitySnapshot>();
    private bool _reloading;
    private bool _movingCells;
    private Vector3Int _movePreview;
    private readonly HashSet<int> _knownChildren = new HashSet<int>();
    private readonly Dictionary<string, RoomPortalAuthoring> _portalOwners = new Dictionary<string, RoomPortalAuthoring>();

    // ── UI Toolkit 요소 참조 ─────────────────────────────────────────────
    private VisualElement _idlePanel;
    private VisualElement _editPanel;
    private Label         _lblStatus;
    private Button        _btnModeArt;
    private Button        _btnModeCell;
    private VisualElement _rootMissingPanel;
    private Label         _lblRoomStatus;
    private HelpBox       _hbxUnverified;
    private HelpBox       _hbxViolations;
    private HelpBox       _hbxSkipped;
    private VisualElement _artPanel;
    private VisualElement _portalInspectorPanel;
    private VisualElement _cellPanel;
    private Button        _btnCellAdd;
    private Button        _btnCellErase;
    private Button        _btnCellSelect;
    private HelpBox       _hbxCellHint;
    private VisualElement _selectControls;
    private Label         _lblPortalId;
    private HelpBox       _hbxFloorCeiling;
    private Vector2Field  _fldOpeningSize;
    private HelpBox       _hbxDesignMismatch;
    private HelpBox       _hbxDesignMissing;
    private ObjectField   _fldDesignPrefab;
    private FloatField    _fldReservationDepth;
    private HelpBox       _hbxDirError;
    private Label         _lblDirInfo;
    private HelpBox       _hbxVisualRootWarning;
    private RoomPortalAuthoring _lastRefreshedPortal;
    private IVisualElementScheduledItem _uiRefresh;
    private RoomInteractionPanel _interactionPanel;
    private RoomZonePanel _zonePanel;
    private RoomConditionalSpawnPanel _spawnPanel;
    private VisualElement _tabContentInteraction;
    private VisualElement _tabContentZone;
    private VisualElement _tabContentSpawn;
    private Button _btnTabInteraction;
    private Button _btnTabZone;
    private Button _btnTabSpawn;
    [SerializeField] private int _artTab;

    // ── 런타임 전용 ─────────────────────────────────────────────────────
    private GameObject _roomRoot;
    private bool _suppressCallback;
    private string _observedArt;
    private double _nextObserve;
    private double _nextSpaceCheck;
    private GameObject _spaceCheckRoot;
    private RoomSpaceChecker.CheckResult _cachedSpaceCheck;

    private string ArtFingerprint()
    {
        if (_roomRoot == null) return "";
        var text = new System.Text.StringBuilder();
        foreach (var cell in _cells) text.Append(cell.x).Append(",").Append(cell.y).Append(",").Append(cell.z).Append(";");
        foreach (var t in _roomRoot.GetComponentsInChildren<Transform>(true))
        {
            text.Append(t.GetInstanceID()).Append(':').Append(t.GetSiblingIndex());
            text.Append(EditorJsonUtility.ToJson(t.gameObject));
            foreach (var c in t.GetComponents<Component>())
                if (c != null) text.Append(EditorJsonUtility.ToJson(c));
        }
        return text.ToString();
    }

    private void ObserveChanges()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (_state != WindowState.Editing || _suppressCallback) return;
        string current = ArtFingerprint();
        if (_observedArt != null && current != _observedArt) _isDirty = true;
        _observedArt = current;
        hasUnsavedChanges = _isDirty;
        saveChangesMessage = "방 편집 내용을 저장하시겠습니까?";
        Repaint();
    }

    private void OnEditorUpdate()
    {
        rootVisualElement.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode);
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (EditorApplication.timeSinceStartup < _nextObserve) return;
        _nextObserve = EditorApplication.timeSinceStartup + 0.2;
        if ((_movingCells || _dragging) && !(focusedWindow is SceneView)) ReleaseCellInput();
        if (_state == WindowState.Editing && _editingSceneHandle != 0) HideOtherScenes();
        ObserveChanges();
    }

    public override void SaveChanges()
    {
        if (!TrySave(null)) throw new IOException("방 저장에 실패했습니다. 편집 내용을 유지합니다.");
        base.SaveChanges();
    }

    public override void DiscardChanges()
    {
        ResetToIdle();
        base.DiscardChanges();
    }

    private void ReleaseCellInput()
    {
        if (_dragging || _movingCells) GUIUtility.hotControl = 0;
        _movingCells = false;
        _movePreview = Vector3Int.zero;
        _moveOffset = Vector3Int.zero;
        _dragging = false;
        _heightAdjust = false;
        _rangeDragged = false;
        _hasHovered = false;
        _dragTouched.Clear();
    }

    private void OnLostFocus() => ReleaseCellInput();

    // 셀 편집
    private Vector3Int _hoveredCell;
    private bool _hasHovered;
    private bool _dragging;
    private bool _dragErase;
    private bool _heightAdjust, _rangeDragged;
    private Vector3Int _rangeStart, _rangeEnd;
    private Vector2 _rangePress;
    private float _heightMouseY;
    private const int MaxRangeCells = 100000;
    private readonly HashSet<Vector3Int> _dragTouched = new HashSet<Vector3Int>();

    // 포털 편집
    private RoomPortalAuthoring _selectedPortal;

    // 색상
    private static readonly Color ColCell     = new Color(0.3f, 0.7f, 1f, 0.22f);
    private static readonly Color ColCellEdge = new Color(0.3f, 0.7f, 1f, 0.9f);
    private static readonly Color ColHover    = new Color(1f, 0.85f, 0.2f, 0.42f);
    private static readonly Color ColErase    = new Color(1f, 0.25f, 0.2f, 0.42f);
    private static readonly Color ColPortal   = new Color(0.2f, 1f, 0.5f, 0.35f);
    private static readonly Color ColPortalDir = new Color(0.1f, 0.9f, 0.3f, 0.9f);
    private static readonly Color ColReserv   = new Color(1f, 0.5f, 0.1f, 0.22f);
    private static readonly Color ColReservEdge = new Color(1f, 0.6f, 0.1f, 0.7f);

    // ── 메뉴 · 초기화 ───────────────────────────────────────────────────
    [MenuItem("Tools/Room Editor")]
    public static void Open() => GetWindow<RoomEditorWindow>("Room Editor");

    private void OnEnable()
    {
        minSize = new Vector2(960, 620);
        EditorApplication.update += OnEditorUpdate;
        Undo.undoRedoPerformed += ObserveChanges;
        SceneView.duringSceneGui += OnSceneGUI;
        EditorApplication.hierarchyChanged += OnHierarchyChanged;
        AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
        Selection.selectionChanged += OnSelectionChanged;
        EditorApplication.playModeStateChanged += OnPlayModeChanged;
        Undo.postprocessModifications += OnUndoModifications;

        if (_state == WindowState.Editing && !EditorApplication.isPlayingOrWillChangePlaymode)
            TryRecoverRoomRoot();
    }

    private void OnDisable()
    {
        _uiRefresh?.Pause();
        _uiRefresh = null;
        ReleaseCellInput();
        EditorApplication.update -= OnEditorUpdate;
        Undo.undoRedoPerformed -= ObserveChanges;
        SceneView.duringSceneGui -= OnSceneGUI;
        EditorApplication.hierarchyChanged -= OnHierarchyChanged;
        AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
        Selection.selectionChanged -= OnSelectionChanged;
        EditorApplication.playModeStateChanged -= OnPlayModeChanged;
        Undo.postprocessModifications -= OnUndoModifications;
        if (!_reloading && !EditorApplication.isCompiling) CloseEditingScene();
    }

    // S03: 씬 오브젝트(포털 위치·회전 등)가 Undo 로 기록된 변경이면 dirty 갱신
    private UndoPropertyModification[] OnUndoModifications(UndoPropertyModification[] modifications)
    {
        if (_state == WindowState.Editing && _roomRoot != null)
        {
            foreach (var mod in modifications)
            {
                var target = mod.currentValue?.target;
                var go = target as GameObject ?? (target as Component)?.gameObject;
                if (go != null && go.transform.IsChildOf(_roomRoot.transform))
                {
                    _isDirty = true;
                    hasUnsavedChanges = true;
                    Repaint();
                    break;
                }
            }
        }
        return modifications;
    }

    private void OnBeforeAssemblyReload() { _reloading = true; }

    private void OnSelectionChanged()
    {
        if (_state != WindowState.Editing || _mode != EditMode.Art) return;
        // Keep the room target while choosing/dragging a Project asset into its ObjectField.
        // Persistent design assets must never become room-editing targets.
        if (!IsRoomPortal(_selectedPortal)) _selectedPortal = null;
        var selected = Selection.activeGameObject;
        if (selected != null && EditorUtility.IsPersistent(selected)) { Repaint(); return; }
        var portal = selected != null ? selected.GetComponent<RoomPortalAuthoring>() : null;
        _selectedPortal = IsRoomPortal(portal) ? portal : null;
        Repaint();
        RefreshUI();
    }

    private bool IsRoomPortal(RoomPortalAuthoring portal) => portal != null &&
        _roomRoot != null && !EditorUtility.IsPersistent(portal) &&
        portal.gameObject.scene == _roomRoot.scene && portal.transform.IsChildOf(_roomRoot.transform);

    // ── UI Toolkit ───────────────────────────────────────────────────────
    public void CreateGUI()
    {
        _uiRefresh?.Pause();
        rootVisualElement.Clear();
        _lastRefreshedPortal = null;
        var vt = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/Editor/RoomEditorWindow.uxml");
        if (vt == null) { rootVisualElement.Add(new Label("[RoomEditor] UXML not found")); return; }
        var root = vt.Instantiate();
        root.style.flexGrow = 1;
        rootVisualElement.Add(root);

        var ss = AssetDatabase.LoadAssetAtPath<StyleSheet>("Assets/Editor/RoomEditorWindow.uss");
        if (ss != null && !rootVisualElement.styleSheets.Contains(ss)) rootVisualElement.styleSheets.Add(ss);

        _idlePanel = root.Q("idle-panel");
        _editPanel = root.Q("edit-panel");

        root.Q<Button>("btn-new").clicked  += CmdNewRoom;
        root.Q<Button>("btn-open").clicked += CmdOpenRoom;

        _lblStatus = root.Q<Label>("lbl-status");
        root.Q<Button>("btn-toolbar-new").clicked    += () => EditorApplication.delayCall += CmdNewRoom;
        root.Q<Button>("btn-toolbar-open").clicked   += () => EditorApplication.delayCall += CmdOpenRoom;
        root.Q<Button>("btn-toolbar-save").clicked   += CmdSave;
        root.Q<Button>("btn-toolbar-saveas").clicked += CmdSaveAs;
        root.Q<Button>("btn-toolbar-exit").clicked   += CmdExit;

        _btnModeArt  = root.Q<Button>("btn-mode-art");
        _btnModeCell = root.Q<Button>("btn-mode-cell");
        _btnModeArt.clicked  += () => SetMode(EditMode.Art);
        _btnModeCell.clicked += () => SetMode(EditMode.Cell);

        _rootMissingPanel = root.Q("rootmissing-panel");
        root.Q<Button>("btn-restore-root").clicked += EnsureRoomRoot;

        _lblRoomStatus = root.Q<Label>("lbl-roomstatus");
        _hbxUnverified = root.Q<HelpBox>("hbx-unverified");
        _hbxViolations = root.Q<HelpBox>("hbx-violations");
        _hbxSkipped    = root.Q<HelpBox>("hbx-skipped");

        _artPanel              = root.Q("art-panel");
        _portalInspectorPanel  = root.Q("portal-inspector");
        _tabContentInteraction = root.Q("tab-content-interaction");
        _tabContentZone        = root.Q("tab-content-zone");
        _tabContentSpawn       = root.Q("tab-content-spawn");
        _btnTabInteraction     = root.Q<Button>("btn-tab-interaction");
        _btnTabZone            = root.Q<Button>("btn-tab-zone");
        _btnTabSpawn           = root.Q<Button>("btn-tab-spawn");
        _btnTabInteraction.clicked += () => SetArtTab(0);
        _btnTabZone.clicked        += () => SetArtTab(1);
        _btnTabSpawn.clicked       += () => SetArtTab(2);
        _interactionPanel = new RoomInteractionPanel();
        _tabContentInteraction.Add(_interactionPanel);
        _zonePanel = new RoomZonePanel();
        _tabContentZone.Add(_zonePanel);
        _spawnPanel = new RoomConditionalSpawnPanel();
        _tabContentSpawn.Add(_spawnPanel);
        _cellPanel            = root.Q("cell-panel");


        _btnCellAdd    = root.Q<Button>("btn-cell-add");
        _btnCellErase  = root.Q<Button>("btn-cell-erase");
        _btnCellSelect = root.Q<Button>("btn-cell-select");
        _btnCellAdd.clicked    += () => SetCellInputMode(0);
        _btnCellErase.clicked  += () => SetCellInputMode(1);
        _btnCellSelect.clicked += () => SetCellInputMode(2);

        _hbxCellHint    = root.Q<HelpBox>("hbx-cell-hint");
        _selectControls = root.Q("select-controls");
        root.Q<Button>("btn-deselect").clicked += () =>
        {
            _selectedCells.Clear();
            SceneView.RepaintAll();
            RefreshUI();
        };
        root.Q<Button>("btn-delete-selected").clicked += () =>
        {
            if (_selectedCells.Count == 0) return;
            Undo.RecordObject(this, "선택 셀 삭제");
            foreach (var c in _selectedCells) _cells.Remove(c);
            _selectedCells.Clear();
            _isDirty = true;
            SceneView.RepaintAll();
            RefreshUI();
        };
        root.Q<Button>("btn-clear-all").clicked += () =>
        {
            if (_cells.Count == 0) return;
            if (EditorUtility.DisplayDialog("모든 셀 지우기", "예약 셀 전부를 지우겠습니까?", "지우기", "취소"))
            {
                Undo.RecordObject(this, "모든 셀 지우기");
                _cells.Clear();
                _selectedCells.Clear();
                _isDirty = true;
                SceneView.RepaintAll();
                RefreshUI();
            }
        };

        _lblPortalId          = root.Q<Label>("lbl-portal-id");
        _hbxFloorCeiling      = root.Q<HelpBox>("hbx-floorceil");
        _fldOpeningSize       = root.Q<Vector2Field>("fld-opening-size");
        _fldOpeningSize.SetEnabled(false);
        _hbxDesignMismatch    = root.Q<HelpBox>("hbx-design-mismatch");
        _hbxDesignMissing     = root.Q<HelpBox>("hbx-design-missing");
        _fldDesignPrefab      = root.Q<ObjectField>("fld-design-prefab");
        _fldDesignPrefab.objectType = typeof(GameObject);
        _fldDesignPrefab.RegisterValueChangedCallback(evt =>
        {
            var newDesign = evt.newValue as GameObject;
            if (newDesign != null && _selectedPortal != null)
            {
                AssignPortalDesign(_selectedPortal, newDesign);
                _fldDesignPrefab.SetValueWithoutNotify(null);
            }
        });
        _fldReservationDepth  = root.Q<FloatField>("fld-reservation-depth");
        _fldReservationDepth.isDelayed = true;
        _fldReservationDepth.RegisterValueChangedCallback(evt =>
        {
            if (!IsRoomPortal(_selectedPortal)) return;
            if (float.IsNaN(evt.newValue) || float.IsInfinity(evt.newValue))
            {
                _fldReservationDepth.SetValueWithoutNotify(_selectedPortal.reservationDepth);
                return;
            }
            float depth = Mathf.Max(0f, evt.newValue);
            if (_selectedPortal.reservationDepth == depth) return;
            Undo.RecordObject(_selectedPortal, "포털 예약 깊이 변경");
            _selectedPortal.reservationDepth = depth;
            EditorUtility.SetDirty(_selectedPortal);
            _isDirty = true;
            _fldReservationDepth.SetValueWithoutNotify(_selectedPortal.reservationDepth);
        });
        _hbxDirError          = root.Q<HelpBox>("hbx-dir-error");
        _lblDirInfo           = root.Q<Label>("lbl-dir-info");
        _hbxVisualRootWarning = root.Q<HelpBox>("hbx-visualroot");

        _uiRefresh = root.schedule.Execute(RefreshUI).Every(100);
        RefreshUI();
    }

    private void RefreshUI()
    {
        if (_idlePanel == null) return;
        bool editing = _state == WindowState.Editing;
        _idlePanel.style.display = editing ? DisplayStyle.None : DisplayStyle.Flex;
        _editPanel.style.display = editing ? DisplayStyle.Flex : DisplayStyle.None;
        if (!editing) return;

        _lblStatus.text = _isDirty ? "● 편집 중 (미저장)" : "편집 중";
        _btnModeArt.EnableInClassList("mode-active",  _mode == EditMode.Art);
        _btnModeCell.EnableInClassList("mode-active", _mode == EditMode.Cell);

        bool rootMissing = _roomRoot == null;
        _rootMissingPanel.style.display = rootMissing ? DisplayStyle.Flex : DisplayStyle.None;
        _lblRoomStatus.style.display    = rootMissing ? DisplayStyle.None : DisplayStyle.Flex;
        _hbxUnverified.style.display    = DisplayStyle.None;
        _hbxViolations.style.display    = DisplayStyle.None;
        _hbxSkipped.style.display       = DisplayStyle.None;
        if (rootMissing)
        {
            _artPanel.style.display = DisplayStyle.None;
            _cellPanel.style.display = DisplayStyle.None;
            return;
        }

        _lblRoomStatus.text = $"루트: {_roomRoot.name}  |  셀: {_cells.Count}";

        var check = RunSpaceCheck();
        if (check.unverified != null && check.unverified.Count > 0)
        {
            _hbxUnverified.style.display = DisplayStyle.Flex;
            _hbxUnverified.text = "공간 검사 미검증 — 일반 생성 사용 불가:\n" + string.Join("\n", check.unverified);
        }
        if (check.violations != null && check.violations.Count > 0)
        {
            _hbxViolations.style.display = DisplayStyle.Flex;
            _hbxViolations.text = $"[V01] Collider {check.violations.Count}개가 예약 공간 밖에 있습니다:\n" + string.Join("\n", check.violations);
        }
        if (check.skipped != null && check.skipped.Count > 0)
        {
            _hbxSkipped.style.display = DisplayStyle.Flex;
            _hbxSkipped.text = $"[V02] 검사 생략 {check.skipped.Count}개 (Trigger·Collider 없음·포털 하위):\n" + string.Join("\n", check.skipped);
        }

        if (_mode == EditMode.Art)
        {
            _artPanel.style.display  = DisplayStyle.Flex;
            _cellPanel.style.display = DisplayStyle.None;
            RefreshPortalInspector();
            _tabContentInteraction.style.display = _artTab == 0 ? DisplayStyle.Flex : DisplayStyle.None;
            _tabContentZone.style.display        = _artTab == 1 ? DisplayStyle.Flex : DisplayStyle.None;
            _tabContentSpawn.style.display       = _artTab == 2 ? DisplayStyle.Flex : DisplayStyle.None;
            _btnTabInteraction.EnableInClassList("tab-active", _artTab == 0);
            _btnTabZone.EnableInClassList("tab-active",        _artTab == 1);
            _btnTabSpawn.EnableInClassList("tab-active",       _artTab == 2);
            if (_artTab == 0)      _interactionPanel.Refresh(_roomRoot);
            else if (_artTab == 1) _zonePanel.Refresh(_roomRoot);
            else                   _spawnPanel.Refresh(_roomRoot);
        }
        else
        {
            _artPanel.style.display  = DisplayStyle.None;
            _cellPanel.style.display = DisplayStyle.Flex;
            RefreshCellPanel();
        }
    }

    private void RefreshPortalInspector()
    {
        bool hasPortal = IsRoomPortal(_selectedPortal);
        _portalInspectorPanel.style.display = hasPortal ? DisplayStyle.Flex : DisplayStyle.None;
        if (!hasPortal) { _lastRefreshedPortal = null; return; }

        var auth = _selectedPortal;
        if (_lastRefreshedPortal != auth)
        {
            _lastRefreshedPortal = auth;
            _fldDesignPrefab.SetValueWithoutNotify(null);
        }
        _lblPortalId.text = string.IsNullOrEmpty(auth.portalId)
            ? "(없음)"
            : auth.portalId.Substring(0, Math.Min(8, auth.portalId.Length)) + "…";
        if (_fldOpeningSize.value != auth.openingSize)
            _fldOpeningSize.SetValueWithoutNotify(auth.openingSize);
        if (_fldReservationDepth.value != auth.reservationDepth)
            _fldReservationDepth.SetValueWithoutNotify(auth.reservationDepth);

        _hbxFloorCeiling.style.display = (auth.socketType == PortalSocketType.FloorCeiling) ? DisplayStyle.Flex : DisplayStyle.None;

        var resolvedDesign = ResolvePortalDesign(auth);
        bool hasMismatch = false;
        if (resolvedDesign != null)
        {
            var da = resolvedDesign.GetComponent<RoomPortalAuthoring>();
            hasMismatch = da != null && da.openingSize != auth.openingSize;
            if (hasMismatch)
                _hbxDesignMismatch.text = $"인스턴스 크기 {auth.openingSize}가 디자인 프리팹 선언값 {da.openingSize}과 다릅니다.";
        }
        _hbxDesignMismatch.style.display = hasMismatch ? DisplayStyle.Flex : DisplayStyle.None;
        bool missingDesign = resolvedDesign == null;
        _hbxDesignMissing.style.display = missingDesign ? DisplayStyle.Flex : DisplayStyle.None;
        _fldDesignPrefab.style.display  = missingDesign ? DisplayStyle.Flex : DisplayStyle.None;

        float yRot    = auth.transform.eulerAngles.y;
        float snappedY = Mathf.Round(yRot / 45f) * 45f;
        bool  isSnapped = Mathf.Abs(Mathf.DeltaAngle(yRot, snappedY)) < 0.5f;
        if (!isSnapped)
        {
            _hbxDirError.style.display = DisplayStyle.Flex;
            _hbxDirError.text          = $"방향 {yRot:F1}°는 45도 단위가 아닙니다.";
            _lblDirInfo.style.display  = DisplayStyle.None;
        }
        else
        {
            _hbxDirError.style.display = DisplayStyle.None;
            _lblDirInfo.style.display  = DisplayStyle.Flex;
            int dirIdx = ((Mathf.RoundToInt(snappedY / 45f)) % 8 + 8) % 8;
            _lblDirInfo.text = $"방향: {dirIdx * 45}°  (인덱스 {dirIdx})";
        }

        _hbxVisualRootWarning.style.display = (auth.visualRoot == null) ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void RefreshCellPanel()
    {
        _btnCellAdd.EnableInClassList("mode-active",    _cellInputMode == 0);
        _btnCellErase.EnableInClassList("mode-active",  _cellInputMode == 1);
        _btnCellSelect.EnableInClassList("mode-active", _cellInputMode == 2);

        _hbxCellHint.text = _cellInputMode == 0
            ? "클릭: 한 칸 추가 / 드래그: 바닥 범위 → 놓고 위아래로 높이 → 클릭 확정 / Esc 취소"
            : _cellInputMode == 1
                ? "클릭: 한 칸 삭제 / 드래그: 범위 → 높이 → 클릭 확정 / Esc 취소"
                : $"클릭·Shift 클릭: 선택 / W: 이동 / Q: 뷰 / F: 선택 보기 / Delete: 삭제 ({_selectedCells.Count}개)";

        bool showSelect = _cellInputMode == 2 && _selectedCells.Count > 0;
        _selectControls.style.display = showSelect ? DisplayStyle.Flex : DisplayStyle.None;
    }

    private void SetCellInputMode(int mode)
    {
        ReleaseCellInput();
        _cellInputMode = mode;
        if (mode == 2) Tools.current = Tool.Move;
        else if (Tools.current == Tool.View) Tools.current = Tool.Move;
        _selectedCells.Clear();
        SceneView.RepaintAll();
        RefreshUI();
    }

    // 실제 Collider 합집합 판정 (§7)
    private RoomSpaceChecker.CheckResult RunSpaceCheck(bool force = false)
    {
        if (!force && _spaceCheckRoot == _roomRoot && EditorApplication.timeSinceStartup < _nextSpaceCheck && _cachedSpaceCheck.violations != null)
            return _cachedSpaceCheck;
        var portals = _roomRoot != null
            ? _roomRoot.GetComponentsInChildren<RoomPortalAuthoring>(true)
            : System.Array.Empty<RoomPortalAuthoring>();
        _cachedSpaceCheck = RoomSpaceChecker.Run(_roomRoot, _cells, portals);
        foreach (var portal in portals)
        {
            var design = ResolvePortalDesign(portal);
            var declaration = design != null ? design.GetComponent<RoomPortalAuthoring>() : null;
            string designError = RoomPortalDesignGuard.FindViolation(portal, design);
            if (designError != null) _cachedSpaceCheck.unverified.Add(portal.name + ": " + designError);
            if (declaration == null || declaration.visualRoot == null ||
                portal.socketType != PortalSocketType.Wall || portal.openingSize != declaration.openingSize)
                _cachedSpaceCheck.unverified.Add("포털 디자인/규격 미확정: " + portal.name);
        }
        _spaceCheckRoot = _roomRoot;
        _nextSpaceCheck = EditorApplication.timeSinceStartup + 0.5;
        return _cachedSpaceCheck;
    }

    private void CmdMoveSelectedCells()
    {
        if (_selectedCells.Count == 0 || (_moveOffset.x == 0 && _moveOffset.y == 0 && _moveOffset.z == 0)) return;
        var selSet = new HashSet<Vector3Int>(_selectedCells);
        var dstSet = new HashSet<Vector3Int>();
        foreach (var c in _selectedCells) dstSet.Add(c + _moveOffset);

        int overwrite = 0;
        foreach (var c in _cells)
            if (!selSet.Contains(c) && dstSet.Contains(c)) overwrite++;

        string msg = $"선택한 {_selectedCells.Count}개 셀을 이동할까요?";
        if (overwrite > 0) msg += $"\n기존 {overwrite}개 셀을 교체합니다.";
        if (!EditorUtility.DisplayDialog("셀 이동", msg, "이동", "취소")) { _moveOffset = Vector3Int.zero; SceneView.RepaintAll(); return; }

        Undo.RecordObject(this, "선택 셀 이동");
        // 이동 대상을 임시 저장 후 소스 제거 → 덮어쓸 대상 제거 → 이동 완료 위치 추가
        var moved = new List<Vector3Int>(_selectedCells.Count);
        foreach (var c in _selectedCells) moved.Add(c + _moveOffset);
        foreach (var c in _selectedCells) _cells.Remove(c);
        foreach (var d in moved) _cells.Remove(d); // overwrite 충돌 제거
        foreach (var d in moved) _cells.Add(d);

        _selectedCells.Clear();
        _selectedCells.AddRange(moved);
        _moveOffset = Vector3Int.zero;
        _isDirty = true;
        SceneView.RepaintAll();
    }

    // ── Scene 관리 ──────────────────────────────────────────────────────
    private void CmdNewRoom()
    {
        if (!ConfirmPendingChanges()) return;
        var scene = BeginEditingScene();
        if (!scene.IsValid()) return;

        AddTestLight();
        _blockDataPath = "";
        _artPrefabPath = "";
        _cells = new List<Vector3Int>();
        _selectedCells.Clear();
        _cellInputMode = 0;
        _isDirty = false;

        EnsureRoomRoot();
        RecordInitialRootIds(scene);
        _state = WindowState.Editing;
        _mode = EditMode.Art;
        SceneView.RepaintAll();
        Repaint();
    }

    private void CmdOpenRoom()
    {
        string abs = EditorUtility.OpenFilePanelWithFilters(
            "방 BlockData 열기", "Assets", new[] { "BlockData", "asset", "All files", "*" });
        if (string.IsNullOrEmpty(abs)) return;

        string rel = FileUtil.GetProjectRelativePath(abs);
        if (string.IsNullOrEmpty(rel))
        { EditorUtility.DisplayDialog("오류", "Assets 폴더 안의 파일을 선택하세요.", "확인"); return; }

        if (AssetDatabase.LoadAssetAtPath<BlockData>(rel) == null)
        { EditorUtility.DisplayDialog("오류", "BlockData 에셋을 선택하세요.", "확인"); return; }
        if (!ConfirmPendingChanges()) return;
        var scene = BeginEditingScene();
        if (!scene.IsValid()) return;

        AddTestLight();
        _blockDataPath = rel;
        _artPrefabPath = "";
        _cells = new List<Vector3Int>();
        _selectedCells.Clear();
        _cellInputMode = 0;
        _isDirty = false;

        EnsureRoomRoot();
        LoadFromBlockData(rel);
        RecordInitialRootIds(scene);
        _state = WindowState.Editing;
        _mode = EditMode.Art;
        SceneView.RepaintAll();
        Repaint();
    }

    private bool ConfirmPendingChanges()
    {
        ObserveChanges();
        if (!_isDirty) return true;
        int choice = EditorUtility.DisplayDialogComplex("미저장 변경", "저장하지 않은 변경이 있습니다.",
            "저장 후 계속", "취소", "변경 버리기");
        return choice == 2 || (choice == 0 && TrySave(null));
    }

    private Scene BeginEditingScene()
    {
        if (SetTemporaryHidden == null || SetTemporaryHiddenBatch == null ||
            TemporaryDefaultParent?.SetMethod == null || TemporaryDefaultParent.PropertyType != typeof(EntityId))
        {
            ShowNotification(new GUIContent("이 Unity 버전의 임시 씬 격리를 지원하지 않습니다. 기존 작업을 유지합니다."));
            return default;
        }
        // Unity cannot create an additive editor scene beside an unsaved untitled
        // scene. Keep that user's scene intact and report the prerequisite.
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var existing = SceneManager.GetSceneAt(i);
            if (existing.handle != _editingSceneHandle && string.IsNullOrEmpty(existing.path))
            {
                ShowNotification(new GUIContent("이름 없는 기존 씬을 먼저 저장한 뒤 방 편집을 시작하세요."));
                return default;
            }
        }
        // Preserve loaded scenes and their per-scene default parent.
        ResetToIdle();
        var previous = SceneManager.GetActiveScene();
        var editing = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        _previousSceneHandle = previous.handle;
        _editingSceneHandle = editing.handle;
        SceneManager.SetActiveScene(editing);
        HideOtherScenes();
        return editing;
    }

    private void HideOtherScenes()
    {
        var visibility = SceneVisibilityManager.instance;
        var tracked = new Dictionary<GameObject, SceneVisibilitySnapshot>();
        for (int i = _isolatedObjects.Count - 1; i >= 0; i--)
        {
            var entry = _isolatedObjects[i];
            if (entry.target == null) { _isolatedObjects.RemoveAt(i); continue; }
            // Moving an existing object into the editing scene must not carry
            // the temporary isolation flags into the room.
            if (entry.target.scene.handle == _editingSceneHandle)
            {
                RestoreVisibility(entry);
                _isolatedObjects.RemoveAt(i);
                continue;
            }
            tracked[entry.target] = entry;
        }
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (!scene.isLoaded || scene.handle == _editingSceneHandle) continue;
            foreach (var root in scene.GetRootGameObjects())
                foreach (var t in root.GetComponentsInChildren<Transform>(true))
                {
                    var go = t.gameObject;
                    if (!tracked.ContainsKey(go))
                    {
                        var entry = new SceneVisibilitySnapshot
                        {
                            target = go,
                            hidden = visibility.IsHidden(go, false)
                        };
                        _isolatedObjects.Add(entry);
                        tracked.Add(go, entry);
                    }
                }
        }
        // Snapshot every object before changing a parent/scene's effective flags.
        var toHide = new List<GameObject>();
        foreach (var entry in _isolatedObjects)
        {
            if (entry.target == null) continue;
            if (!visibility.IsHidden(entry.target, false)) toHide.Add(entry.target);
            // Hidden objects are excluded from SceneView picking. Preserve the
            // user's separate picking flags: changing them across scene moves
            // can leave stale disabled state in Unity 6000.3.
        }
        if (toHide.Count > 0) SetTemporaryHiddenBatch(toHide.ToArray(), true, false);
    }

    private static void RestoreVisibility(SceneVisibilitySnapshot entry)
    {
        if (entry.target == null) return;
        var visibility = SceneVisibilityManager.instance;
        if (visibility.IsHidden(entry.target, false) == entry.hidden) return;
        if (SetTemporaryHidden != null) SetTemporaryHidden(entry.target, entry.hidden, false);
        // Recovery only: restore a serialized session even if an Editor upgrade
        // removed the internal API. New sessions are refused above in that case.
        else if (entry.hidden) visibility.Hide(entry.target, false);
        else visibility.Show(entry.target, false);
    }

    // The public Hide/Show methods record the global visibility state in Undo.
    // Temporary isolation must not consume a user's Undo step or clear their
    // visibility history. Keep this version-sensitive API behind one cached delegate.
    private static readonly Action<GameObject, bool, bool> SetTemporaryHidden = CreateTemporaryVisibilitySetter();
    private static readonly Action<GameObject[], bool, bool> SetTemporaryHiddenBatch = CreateTemporaryVisibilityBatchSetter();

    private static Action<GameObject[], bool, bool> CreateTemporaryVisibilityBatchSetter()
    {
        var state = typeof(SceneVisibilityManager).Assembly.GetType("UnityEditor.SceneVisibilityState");
        var method = state?.GetMethod("SetGameObjectsHidden",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic, null,
            new[] { typeof(GameObject[]), typeof(bool), typeof(bool) }, null);
        if (method == null || method.ReturnType != typeof(void)) return null;
        try { return (Action<GameObject[], bool, bool>)Delegate.CreateDelegate(typeof(Action<GameObject[], bool, bool>), method); }
        catch (ArgumentException) { return null; }
        catch (MemberAccessException) { return null; }
    }

    private static Action<GameObject, bool, bool> CreateTemporaryVisibilitySetter()
    {
        var state = typeof(SceneVisibilityManager).Assembly.GetType("UnityEditor.SceneVisibilityState");
        var method = state?.GetMethod("SetGameObjectHidden",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.NonPublic, null,
            new[] { typeof(GameObject), typeof(bool), typeof(bool) }, null);
        if (method == null || method.ReturnType != typeof(void)) return null;
        try { return (Action<GameObject, bool, bool>)Delegate.CreateDelegate(typeof(Action<GameObject, bool, bool>), method); }
        catch (ArgumentException) { return null; }
        catch (MemberAccessException) { return null; }
    }

    private void RestoreOtherScenes()
    {
        if (SetTemporaryHiddenBatch == null)
            foreach (var entry in _isolatedObjects) RestoreVisibility(entry);
        else
        {
            var hide = new List<GameObject>();
            var show = new List<GameObject>();
            var visibility = SceneVisibilityManager.instance;
            foreach (var entry in _isolatedObjects)
                if (entry.target != null && visibility.IsHidden(entry.target, false) != entry.hidden)
                    (entry.hidden ? hide : show).Add(entry.target);
            if (hide.Count > 0) SetTemporaryHiddenBatch(hide.ToArray(), true, false);
            if (show.Count > 0) SetTemporaryHiddenBatch(show.ToArray(), false, false);
        }
        _isolatedObjects.Clear();
    }

    private static Scene FindLoadedScene(int handle)
    {
        for (int i = 0; i < SceneManager.sceneCount; i++)
        {
            var scene = SceneManager.GetSceneAt(i);
            if (scene.handle == handle) return scene;
        }
        return default;
    }

    private void CloseEditingScene()
    {
        RestoreOtherScenes();
        if (_editingSceneHandle == 0) return;
        var editing = FindLoadedScene(_editingSceneHandle);
        var previous = FindLoadedScene(_previousSceneHandle);
        _editingSceneHandle = _previousSceneHandle = 0;
        _state = WindowState.Idle;
        if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        if (editing.IsValid() && editing.isLoaded)
        {
            // Closing our owned scene removes its default parent; the public
            // ClearDefaultParentObject would add an unrelated Undo operation.
            EditorSceneManager.CloseScene(editing, true);
        }
    }

    private void UseRoomDefaultParent()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (_editingSceneHandle != 0 && _roomRoot != null && SceneManager.GetActiveScene() == _roomRoot.scene)
            TemporaryDefaultParent?.SetValue(_roomRoot.scene, _roomRoot.GetEntityId());
    }

    private void OnPlayModeChanged(PlayModeStateChange state)
    {
        if (_state != WindowState.Editing) return;
        if (state == PlayModeStateChange.ExitingEditMode)
        {
            ReleaseCellInput();
            var editing = FindLoadedScene(_editingSceneHandle);
            if (editing.IsValid()) TemporaryDefaultParent?.SetValue(editing, default(EntityId));
            var previous = FindLoadedScene(_previousSceneHandle);
            if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        }
        else if (state == PlayModeStateChange.EnteredEditMode)
        {
            var editing = FindLoadedScene(_editingSceneHandle);
            if (editing.IsValid() && editing.isLoaded) SceneManager.SetActiveScene(editing);
            TryRecoverRoomRoot();
            UseRoomDefaultParent();
        }
    }

    // EditorUtility.SetDefaultParentObject records Undo even for repeated setup.
    // This parent belongs only to our disposable editing scene.
    private static readonly System.Reflection.PropertyInfo TemporaryDefaultParent =
        typeof(Scene).GetProperty("defaultParent", System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);

    private static void AddTestLight()
    {
        var go = new GameObject("TestLight");
        var lt = go.AddComponent<Light>();
        lt.type = LightType.Directional;
        lt.intensity = 1f;
        go.transform.eulerAngles = new Vector3(50f, -30f, 0f);
    }

    private void RecordInitialRootIds(Scene scene)
    {
        _observedArt = ArtFingerprint();
        _knownChildren.Clear();
        _portalOwners.Clear();
        if (_roomRoot != null)
            foreach (var portal in _roomRoot.GetComponentsInChildren<RoomPortalAuthoring>(true))
                if (!string.IsNullOrEmpty(portal.portalId) && !_portalOwners.ContainsKey(portal.portalId))
                    _portalOwners.Add(portal.portalId, portal);
        if (_roomRoot != null) foreach (var t in _roomRoot.GetComponentsInChildren<Transform>(true)) _knownChildren.Add(t.GetInstanceID());
        UseRoomDefaultParent();
        _initialRootIds.Clear();
        foreach (var go in scene.GetRootGameObjects())
            _initialRootIds.Add(go.GetInstanceID());
    }

    private void TryRecoverRoomRoot()
    {
        Scene active = _editingSceneHandle != 0 ? FindLoadedScene(_editingSceneHandle) : SceneManager.GetActiveScene();
        if (!active.IsValid() || !active.isLoaded)
        { ResetToIdle("편집 Scene을 찾을 수 없습니다. 편집 세션을 초기화합니다."); return; }

        foreach (var go in active.GetRootGameObjects())
        {
            if (go.name == RootName) { _roomRoot = go; break; }
        }

        if (_roomRoot == null) EnsureRoomRoot();

        // 도메인 리로드 후 인스턴스 ID가 무효화되므로 재스냅샷
        RecordInitialRootIds(active);
    }

    private void ResetToIdle(string msg = null)
    {
        _state = WindowState.Idle;
        CloseEditingScene();
        ReleaseCellInput();
        hasUnsavedChanges = false;
        _observedArt = null;
        _roomRoot = null;
        _isDirty = false;
        _cells = new List<Vector3Int>();
        _selectedCells.Clear();
        _cellInputMode = 0;
        _blockDataPath = "";
        _artPrefabPath = "";
        _initialRootIds.Clear();
        if (msg != null) Debug.LogWarning($"[RoomEditor] {msg}");
        _knownChildren.Clear();
        _portalOwners.Clear();
        Repaint();
        RefreshUI();
    }

    // ── 방 루트 ─────────────────────────────────────────────────────────
    private void EnsureRoomRoot()
    {
        if (_roomRoot != null) return;
        Scene active = _editingSceneHandle != 0 ? FindLoadedScene(_editingSceneHandle) : SceneManager.GetActiveScene();
        if (!active.IsValid() || !active.isLoaded) { ResetToIdle(); return; }
        foreach (var go in active.GetRootGameObjects())
        {
            if (go.name == RootName) { _roomRoot = go; return; }
        }
        _suppressCallback = true;
        _roomRoot = new GameObject(RootName);
        SceneManager.MoveGameObjectToScene(_roomRoot, active);
        _roomRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _roomRoot.transform.localScale = Vector3.one;
        _suppressCallback = false;
        UseRoomDefaultParent();
    }

    private void EnforceRootLock()
    {
        if (_roomRoot == null || _state != WindowState.Editing) return;
        if (_roomRoot.transform.position == Vector3.zero &&
            _roomRoot.transform.rotation == Quaternion.identity &&
            _roomRoot.transform.localScale == Vector3.one) return;

        _roomRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
        _roomRoot.transform.localScale = Vector3.one;
        Debug.LogWarning($"[RoomEditor] '{RootName}'은 (0,0,0)에 고정됩니다.", _roomRoot);
    }

    private void OnHierarchyChanged()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (_state != WindowState.Editing || _suppressCallback) return;

        // 루트 삭제 감지 → 재생성
        if (_roomRoot == null) { EnsureRoomRoot(); _isDirty = true; ObserveChanges(); return; }

        // 새로 추가된 씬 루트 오브젝트 → 방 루트 아래로 자동 편입
        Scene active = _roomRoot.scene;
        foreach (var go in active.GetRootGameObjects())
        {
            if (go == _roomRoot) continue;
            if (_initialRootIds.Contains(go.GetInstanceID())) continue;

            _suppressCallback = true;
            go.transform.SetParent(_roomRoot.transform, true);
            _suppressCallback = false;
            _isDirty = true;
            Debug.Log($"[RoomEditor] '{go.name}' → '{RootName}' 아래로 자동 편입", go);
            // A05: 드래그 중이던 셀 모드를 아트 모드로 전환
            if (_mode == EditMode.Cell) SetMode(EditMode.Art);
        }
        EnsurePortalIdentities();
        foreach (var t in _roomRoot.GetComponentsInChildren<Transform>(true))
            if (_knownChildren.Add(t.GetInstanceID()) && t != _roomRoot.transform && _mode == EditMode.Cell)
                SetMode(EditMode.Art);
        ObserveChanges();
    }

    private void EnsurePortalIdentities()
    {
        foreach (var portal in _roomRoot.GetComponentsInChildren<RoomPortalAuthoring>(true))
        {
            bool empty = string.IsNullOrEmpty(portal.portalId);
            bool owned = !empty && _portalOwners.TryGetValue(portal.portalId, out var owner) &&
                         owner != null && owner != portal;
            // A newly instantiated source prefab must not reuse its authoring ID.
            bool fromSource = !_knownChildren.Contains(portal.transform.GetInstanceID()) &&
                              PrefabUtility.GetCorrespondingObjectFromSource(portal) is RoomPortalAuthoring source &&
                              source.portalId == portal.portalId;
            if (empty || owned || fromSource)
            {
                Undo.RecordObject(portal, "포털 ID 발급");
                portal.portalId = Guid.NewGuid().ToString();
                PrefabUtility.RecordPrefabInstancePropertyModifications(portal);
            }
            _portalOwners[portal.portalId] = portal;
        }
    }

    // ── 모드 전환 ────────────────────────────────────────────────────────
    private void SetMode(EditMode mode)
    {
        if (_mode == mode) return;
        ReleaseCellInput();
        _mode = mode;
        if (mode == EditMode.Cell) Selection.activeGameObject = null;
        _selectedPortal = null;
        _selectedCells.Clear();
        SceneView.RepaintAll();
        Repaint();
        RefreshUI();
    }

    private void SetArtTab(int tab)
    {
        _artTab = tab;
        RefreshUI();
    }

    // ── 저장 · 열기 ──────────────────────────────────────────────────────
    private void CmdSave()
    {
        if (!TrySave(null)) Debug.LogWarning("[RoomEditor] 저장에 실패했습니다.");
    }

    private void CmdSaveAs()
    {
        string newPath = EditorUtility.SaveFilePanelInProject(
            "방 데이터 저장 (복제)", "NewRoomData", "asset", "방 BlockData 저장 경로를 선택하세요.");
        if (string.IsNullOrEmpty(newPath)) return;

        // Selecting the current file is an overwrite, not an independent room copy.
        if (!string.IsNullOrEmpty(_blockDataPath) &&
            string.Equals(Path.GetFullPath(newPath), Path.GetFullPath(_blockDataPath),
                StringComparison.OrdinalIgnoreCase))
        {
            CmdSave();
            return;
        }

        // §8.1: _blockDataPath를 비워 TrySave가 새 BlockData(새 blockId)를 생성하게 한다.
        // 포털 ID 재발급은 TrySave 내부에서 isNewAsset 조건으로 처리한다.
        string oldBlock = _blockDataPath;
        string oldArt   = _artPrefabPath;
        _blockDataPath = "";
        _artPrefabPath = "";

        if (!TrySave(newPath))
        {
            // 실패 시 경로 복원 — 이전 작업 세션 유지
            _blockDataPath = oldBlock;
            _artPrefabPath = oldArt;
            Debug.LogWarning("[RoomEditor] 다른 이름으로 저장 실패");
        }
        // 성공 시 _blockDataPath/_artPrefabPath는 TrySave 안에서 새 경로로 갱신됨
    }

    private void CmdExit()
    {
        if (!ConfirmPendingChanges()) return;
        ResetToIdle();
    }

    // blockPathOverride: null이면 _blockDataPath 또는 다이얼로그로 경로 결정
    private bool TrySave(string blockPathOverride)
    {
        string blockPath = blockPathOverride ?? _blockDataPath;
        if (string.IsNullOrEmpty(blockPath))
        {
            blockPath = EditorUtility.SaveFilePanelInProject(
                "방 데이터 저장", "NewRoomData", "asset", "방 BlockData 저장 경로를 선택하세요.");
            if (string.IsNullOrEmpty(blockPath)) return false;
        }

        string prefabPath = _artPrefabPath;
        if (string.IsNullOrEmpty(prefabPath))
        {
            string dir  = Path.GetDirectoryName(blockPath)?.Replace('\\', '/') ?? "Assets";
            string name = Path.GetFileNameWithoutExtension(blockPath);
            prefabPath = $"{dir}/{name}_Art.prefab";
        }

        bool isNewAsset = string.IsNullOrEmpty(_blockDataPath) || blockPathOverride != null;

        // P01: 저장 전 포털 규격 검증 — 비직교 회전·0 개구부 차단
        if (!ValidatePortalsBeforeSave()) return false;

        // V03: 아트 Collider 범위 검사 — 위반 시 사용자 확인 후 저장 허용, spaceCheckState 기록
        RoomSpaceChecker.CheckResult spaceCheck = default;
        if (_cells.Count > 0 && _roomRoot != null)
        {
            spaceCheck = RunSpaceCheck(true);
            if (spaceCheck.HasViolations)
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine($"Collider {spaceCheck.violations.Count}개가 예약 공간 밖에 있습니다.");
                sb.AppendLine("이 방은 저장 후 일반 생성에 사용할 수 없습니다.");
                foreach (var v in spaceCheck.violations) sb.AppendLine(v);
                bool proceed = EditorUtility.DisplayDialog(
                    "공간 검사 위반 — 저장 계속?",
                    sb.ToString(),
                    "저장 (생성 사용 불가)", "취소");
                if (!proceed) return false;
            }
        }

        var authors = _roomRoot.GetComponentsInChildren<RoomPortalAuthoring>(true);
        var oldIds = new Dictionary<RoomPortalAuthoring, string>();
        foreach (var auth in authors) oldIds[auth] = auth.portalId;
        List<PortalData> portals = ExtractPortals();

        // §8.1 S04: 신규 에셋(새 방·다른 이름 저장)이면 포털 ID 새로 발급하고 씬 오브젝트에 반영
        if (isNewAsset && _roomRoot != null)
        {
            var authByOldId = new Dictionary<string, RoomPortalAuthoring>();
            foreach (var auth in _roomRoot.GetComponentsInChildren<RoomPortalAuthoring>(true))
                if (auth != null && !string.IsNullOrEmpty(auth.portalId))
                    authByOldId[auth.portalId] = auth;

            foreach (var pd in portals)
            {
                string newId = Guid.NewGuid().ToString();
                if (!string.IsNullOrEmpty(pd.portalId) && authByOldId.TryGetValue(pd.portalId, out var auth))
                {
                    auth.portalId = newId;
                    EditorUtility.SetDirty(auth);
                }
                pd.portalId = newId;
            }
        }

        BlockData original = AssetDatabase.LoadAssetAtPath<BlockData>(blockPath);
        BlockData draft = original != null ? Instantiate(original) : CreateInstance<BlockData>();
        try
        {
            if (isNewAsset || string.IsNullOrEmpty(draft.blockId)) draft.blockId = Guid.NewGuid().ToString();
            draft.cells = new List<Vector3Int>(_cells);
            draft.portals = portals;
            draft.localBounds = CalculateBounds(_cells);
            draft.spaceCheckVersion = BlockData.CurrentSpaceCheckVersion;
            // V03: 공간 검사 결과 저장
            if (_cells.Count == 0 || _roomRoot == null)
                draft.spaceCheckState = SpaceCheckState.Unchecked;
            else if (spaceCheck.violations == null)
                draft.spaceCheckState = SpaceCheckState.Unchecked;
            else
                draft.spaceCheckState = spaceCheck.HasViolations
                    ? SpaceCheckState.HasViolations
                    : spaceCheck.IsUnverified ? SpaceCheckState.Inconclusive : SpaceCheckState.Valid;
            RoomAssetSaveTransaction.Save(_roomRoot, draft, blockPath, prefabPath);
            _blockDataPath = blockPath;
            _artPrefabPath = prefabPath;
            _isDirty = false;
            hasUnsavedChanges = false;
            _observedArt = ArtFingerprint();
            Repaint();
            return true;
        }
        catch (Exception ex)
        {
            foreach (var pair in oldIds) if (pair.Key != null) pair.Key.portalId = pair.Value;
            _isDirty = true;
            hasUnsavedChanges = true;
            Debug.LogError("[RoomEditor] 저장 실패. 기존 파일과 편집 내용을 유지합니다. " + ex.Message);
            return false;
        }
        finally { DestroyImmediate(draft); }
    }

    // P01: 저장 전 포털 유효성 검사. 하나라도 위반이면 대화상자 표시 후 false 반환.
    private bool ValidatePortalsBeforeSave()
    {
        if (_roomRoot == null) return true;
        var violations = new System.Text.StringBuilder();
        foreach (var auth in _roomRoot.GetComponentsInChildren<RoomPortalAuthoring>(true))
        {
            if (auth == null) continue;
            var t = auth.transform;

            // 개구부 크기 검사
            if (!FinitePositive(auth.openingSize.x) || !FinitePositive(auth.openingSize.y))
                violations.AppendLine($"  · {t.name}: 개구부 크기 0 이하 ({auth.openingSize})");

            var design = ResolvePortalDesign(auth);
            var declaration = design != null ? design.GetComponent<RoomPortalAuthoring>() : null;
            string designError = RoomPortalDesignGuard.FindViolation(auth, design);
            if (designError != null) violations.AppendLine($"  · {t.name}: {designError}");
            if (declaration != null && auth.openingSize != declaration.openingSize)
                violations.AppendLine($"  · {t.name}: 개구부 크기는 디자인 선언값 {declaration.openingSize}이어야 합니다.");
            if (float.IsNaN(auth.reservationDepth) || float.IsInfinity(auth.reservationDepth) || auth.reservationDepth < 0)
                violations.AppendLine($"  · {t.name}: 예약 깊이는 유한한 0 이상의 값이어야 합니다.");

            // pitch·roll 검사 (비직교)
            Vector3 euler = t.eulerAngles;
            float pitch = euler.x > 180f ? euler.x - 360f : euler.x;
            float roll  = euler.z > 180f ? euler.z - 360f : euler.z;
            if (Mathf.Abs(pitch) > 0.5f || Mathf.Abs(roll) > 0.5f)
                violations.AppendLine(
                    $"  · {t.name}: pitch={pitch:F1}° / roll={roll:F1}° — 포털은 Y축 회전만 허용합니다.");

            // Y 회전 45° 단위 검사
            float yRot = euler.y;
            float snapped = Mathf.Round(yRot / 45f) * 45f;
            if (Mathf.Abs(Mathf.DeltaAngle(yRot, snapped)) > 0.5f)
                violations.AppendLine(
                    $"  · {t.name}: Y회전 {yRot:F1}°는 45°단위가 아닙니다. (가장 가까운 값: {snapped}°)");

            // 스케일 검사
            if (!Mathf.Approximately(t.lossyScale.x, 1f) ||
                !Mathf.Approximately(t.lossyScale.y, 1f) ||
                !Mathf.Approximately(t.lossyScale.z, 1f))
                violations.AppendLine(
                    $"  · {t.name}: 스케일 {t.lossyScale} — 포털은 (1,1,1)이어야 합니다.");
        }

        if (violations.Length == 0) return true;

        EditorUtility.DisplayDialog("포털 규격 위반 — 저장 차단",
            "아래 포털을 수정한 뒤 다시 저장하세요:\n\n" + violations, "확인");
        return false;
    }

    // Replace a legacy placeholder with a real nested prefab instance, as one Undo step.
    private bool AssignPortalDesign(RoomPortalAuthoring placeholder, GameObject design)
    {
        if (!IsRoomPortal(placeholder) || design == null) return false;
        string path = AssetDatabase.GetAssetPath(design);
        var declaration = design.GetComponent<RoomPortalAuthoring>();
        if (string.IsNullOrEmpty(path) || AssetDatabase.LoadAssetAtPath<GameObject>(path) != design ||
            !PrefabUtility.IsPartOfPrefabAsset(design) || declaration == null ||
            declaration.visualRoot == null || !declaration.visualRoot.IsChildOf(design.transform) ||
            !FinitePositive(declaration.openingSize.x) || !FinitePositive(declaration.openingSize.y))
        {
            Debug.LogWarning("포털 디자인은 모델 루트와 양수 개구부 크기를 가진 프리팹 루트여야 합니다.");
            return false;
        }
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();
        Undo.SetCurrentGroupName("포털 디자인 지정");
        var previous = placeholder.transform;
        var replacement = (GameObject)PrefabUtility.InstantiatePrefab(design, previous.parent);
        Undo.RegisterCreatedObjectUndo(replacement, "포털 디자인 지정");
        replacement.transform.localPosition = previous.localPosition;
        replacement.transform.localRotation = previous.localRotation;
        replacement.transform.localScale = previous.localScale;
        replacement.transform.SetSiblingIndex(previous.GetSiblingIndex());
        var author = replacement.GetComponent<RoomPortalAuthoring>();
        author.portalId = string.IsNullOrEmpty(placeholder.portalId) ? Guid.NewGuid().ToString() : placeholder.portalId;
        author.designPrefab = design;
        author.reservationDepth = placeholder.reservationDepth;
        author.socketType = placeholder.socketType;
        PrefabUtility.RecordPrefabInstancePropertyModifications(author);
        PrefabUtility.RecordPrefabInstancePropertyModifications(replacement.transform);
        // Preserve user-added children of a legacy placeholder.
        var children = new List<Transform>();
        foreach (Transform child in previous) if (child != placeholder.visualRoot) children.Add(child);
        foreach (var child in children) Undo.SetTransformParent(child, replacement.transform, "포털 디자인 지정");
        Undo.DestroyObjectImmediate(placeholder.gameObject);
        Undo.CollapseUndoOperations(group);
        _selectedPortal = author;
        Selection.activeGameObject = replacement;
        _isDirty = true;
        hasUnsavedChanges = true;
        _nextSpaceCheck = 0;
        return true;
    }

    private static bool FinitePositive(float value) => !float.IsNaN(value) && !float.IsInfinity(value) && value > 0;

    private GameObject ResolvePortalDesign(RoomPortalAuthoring auth)
    {
        if (auth.designPrefab != null) return auth.designPrefab;
        var source = PrefabUtility.GetCorrespondingObjectFromSource(auth.gameObject);
        while (source != null)
        {
            string path = AssetDatabase.GetAssetPath(source);
            var root = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            // A portal design is a prefab ROOT, never a child inside the room art.
            if (root == source && root.GetComponent<RoomPortalAuthoring>() != null)
                return root;
            source = PrefabUtility.GetCorrespondingObjectFromSource(source);
        }
        return null;
    }

    private List<PortalData> ExtractPortals()
    {
        var result = new List<PortalData>();
        if (_roomRoot == null) return result;

        // P04: 저장 시점에 중복 ID 해소
        var seenIds = new HashSet<string>();

        foreach (var auth in _roomRoot.GetComponentsInChildren<RoomPortalAuthoring>(true))
        {
            if (auth == null) continue;

            // P05: 직접 소스(Variant 포함) 참조 — OriginalSource 는 Variant를 기본 프리팹으로 역추적함
            GameObject prefabAsset = ResolvePortalDesign(auth);
            auth.designPrefab = prefabAsset;

            Vector3 localPos = _roomRoot.transform.InverseTransformPoint(auth.transform.position);

            float yRot = auth.transform.eulerAngles.y;
            int dirIndex = ((Mathf.RoundToInt(yRot / 45f)) % 8 + 8) % 8;

            // P04: 비어 있거나 이미 사용된 ID → 새 GUID 발급
            if (string.IsNullOrEmpty(auth.portalId) || seenIds.Contains(auth.portalId))
            {
                auth.portalId = Guid.NewGuid().ToString();
                EditorUtility.SetDirty(auth);
            }
            seenIds.Add(auth.portalId);

            bool useReserv = auth.reservationDepth > 0f;
            result.Add(new PortalData
            {
                portalId          = auth.portalId,
                socketType        = auth.socketType,
                localPosition     = localPos,
                directionIndex    = dirIndex,
                openingSize       = auth.openingSize,
                useReservationBounds = useReserv,
                reservationBounds = new Vector3(auth.openingSize.x, auth.openingSize.y,
                                        useReserv ? auth.reservationDepth : 0f),
                visualPrefab      = prefabAsset,
            });
        }
        return result;
    }

    // §9 기존 방 호환: 포털 PortalData → 씬 오브젝트 복원
    private void RestorePortals(List<PortalData> portals)
    {
        if (_roomRoot == null || portals == null) return;
        foreach (var pd in portals)
        {
            if (pd == null) continue;
            GameObject go;
            if (pd.visualPrefab != null)
            {
                go = (GameObject)PrefabUtility.InstantiatePrefab(pd.visualPrefab, _roomRoot.transform);
            }
            else
            {
                // §M02: 디자인 미지정 — 임시 표시 오브젝트
                string shortId = string.IsNullOrEmpty(pd.portalId)
                    ? "?" : pd.portalId.Substring(0, Math.Min(6, pd.portalId.Length));
                go = new GameObject($"Portal_[미지정]_{shortId}");
                go.transform.SetParent(_roomRoot.transform, false);
            }
            if (go == null) continue;
            go.transform.localPosition    = pd.localPosition;
            go.transform.localEulerAngles = new Vector3(0f, pd.directionIndex * 45f, 0f);

            var auth = go.GetComponent<RoomPortalAuthoring>() ?? go.AddComponent<RoomPortalAuthoring>();
            auth.designPrefab     = pd.visualPrefab;
            auth.socketType       = pd.socketType;
            auth.portalId         = string.IsNullOrEmpty(pd.portalId) ? Guid.NewGuid().ToString() : pd.portalId;
            auth.openingSize      = pd.openingSize;
            auth.reservationDepth = pd.useReservationBounds ? pd.reservationBounds.z : 0f;
        }
    }

    private void LoadFromBlockData(string path)
    {
        var bd = AssetDatabase.LoadAssetAtPath<BlockData>(path);
        if (bd == null) { Debug.LogWarning($"[RoomEditor] BlockData 로드 실패: {path}"); return; }

        _cells       = new List<Vector3Int>(bd.cells ?? new List<Vector3Int>());
        _artPrefabPath = AssetDatabase.GetAssetPath(bd.visualPrefab) ?? "";

        if (bd.visualPrefab != null)
        {
            if (_roomRoot != null) DestroyImmediate(_roomRoot);
            _roomRoot = (GameObject)PrefabUtility.InstantiatePrefab(bd.visualPrefab);
            if (_roomRoot != null)
            {
                PrefabUtility.UnpackPrefabInstance(_roomRoot, PrefabUnpackMode.OutermostRoot, InteractionMode.AutomatedAction);
                _roomRoot.name = RootName;
                _roomRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
                _roomRoot.transform.localScale = Vector3.one;
            }
            else { EnsureRoomRoot(); }
        }
        else { EnsureRoomRoot(); }

        if (_roomRoot != null && bd.portals != null)
            foreach (var auth in _roomRoot.GetComponentsInChildren<RoomPortalAuthoring>(true))
            {
                if (auth.designPrefab != null) continue;
                var pd = bd.portals.Find(p => p.portalId == auth.portalId);
                var resolved = ResolvePortalDesign(auth);
                auth.designPrefab = resolved != null ? resolved :
                    (pd != null && pd.visualPrefab != bd.visualPrefab ? pd.visualPrefab : null);
            }

        // 기존 방 호환: 씬에 RoomPortalAuthoring이 없으면 PortalData에서 복원
        if (bd.portals != null && bd.portals.Count > 0 && _roomRoot != null)
        {
            int existing = _roomRoot.GetComponentsInChildren<RoomPortalAuthoring>(true).Length;
            if (existing == 0) RestorePortals(bd.portals);
        }
        _observedArt = ArtFingerprint();
    }

    private static Bounds CalculateBounds(List<Vector3Int> cells)
    {
        if (cells == null || cells.Count == 0) return new Bounds();
        var b = new Bounds(cells[0] + Vector3.one * 0.5f, Vector3.one);
        for (int i = 1; i < cells.Count; i++)
            b.Encapsulate(new Bounds(cells[i] + Vector3.one * 0.5f, Vector3.one));
        return b;
    }

    // ── SceneView GUI ────────────────────────────────────────────────────
    private void OnSceneGUI(SceneView sv)
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (_state != WindowState.Editing) return;
        EnforceRootLock();
        if (_mode == EditMode.Cell)
        {
            sv.wantsMouseEnterLeaveWindow = true;
            if (Event.current.type == EventType.MouseLeaveWindow && _dragging)
                ReleaseCellInput();
            DrawCellMoveHandle();
            if ((!_movingCells && GUIUtility.hotControl == 0 && Event.current.type != EventType.Used) || _dragging)
                HandleCellInput(sv);
        }
        DrawCellGizmos();
        DrawPortalGizmos();
    }

    private void DrawCellMoveHandle()
    {
        if (_cellInputMode != 2 || _selectedCells.Count == 0 || Tools.current != Tool.Move) return;
        var e = Event.current;
        if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
        { ReleaseCellInput(); e.Use(); SceneView.RepaintAll(); return; }
        Vector3 center = Vector3.zero;
        foreach (var cell in _selectedCells) center += (Vector3)cell + Vector3.one * .5f;
        center /= _selectedCells.Count;
        bool mouseUp = e.rawType == EventType.MouseUp && e.button == 0;
        EditorGUI.BeginChangeCheck();
        var position = Handles.PositionHandle(center + (Vector3)_movePreview, Quaternion.identity);
        if (EditorGUI.EndChangeCheck())
        {
            _movingCells = true;
            _movePreview = Vector3Int.RoundToInt(position - center);
            SceneView.RepaintAll();
        }
        if (_movingCells) Handles.Label(center + (Vector3)_movePreview, "이동 " + _movePreview);
        if (_movingCells && mouseUp)
        {
            var delta = _movePreview;
            ReleaseCellInput(); // release before native dialog changes focus
            _moveOffset = delta;
            e.Use();
            CmdMoveSelectedCells();
        }
    }

    private void HandleCellInput(SceneView sv)
    {
        Event e = Event.current;
        if (e.type == EventType.MouseLeaveWindow) { ReleaseCellInput(); return; }
        if (e.alt || e.button == 1 || e.button == 2 || (Tools.current == Tool.View && e.type != EventType.KeyDown))
        {
            if (_dragging && (e.alt || e.type == EventType.MouseDown || Tools.current == Tool.View)) ReleaseCellInput();
            return;
        }
        if (e.type == EventType.KeyDown && !EditorGUIUtility.editingTextField)
        {
            if (e.keyCode == KeyCode.Escape) { ReleaseCellInput(); e.Use(); return; }
            if (e.keyCode == KeyCode.W) { if (_cellInputMode != 2) SetCellInputMode(2); Tools.current = Tool.Move; e.Use(); return; }
            if (e.keyCode == KeyCode.Q) { ReleaseCellInput(); Tools.current = Tool.View; e.Use(); return; }
            if (e.keyCode == KeyCode.F && _selectedCells.Count > 0)
            { sv.Frame(CalculateBounds(_selectedCells.ToList()), false); e.Use(); return; }
            if ((e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace) && _selectedCells.Count > 0)
            {
                Undo.RecordObject(this, "선택 셀 삭제");
                _cells.RemoveAll(c => _selectedCells.Contains(c));
                _selectedCells.Clear(); _isDirty = true; e.Use(); return;
            }
        }
        int id = GUIUtility.GetControlID(FocusType.Passive);

        if (e.type == EventType.Layout) HandleUtility.AddDefaultControl(id);
        if (_heightAdjust)
        {
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
            {
                float delta = _heightMouseY - e.mousePosition.y;
                _rangeEnd.y = _rangeStart.y + (int)Mathf.Sign(delta) * Mathf.FloorToInt(Mathf.Abs(delta) / 24f);
                e.Use();
            }
            else if (e.type == EventType.MouseDown && e.button == 0)
            { CommitCellRange(); ReleaseCellInput(); e.Use(); }
            sv.Repaint(); return;
        }
        Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
        bool hit = TryGetCellTarget(ray, out Vector3Int cell);
        _hasHovered = hit;
        _hoveredCell = cell;

        if (_cellInputMode == 2)
        {
            if (e.type == EventType.MouseDown && e.button == 0)
            {
                GUIUtility.hotControl = id;
                if (hit)
                {
                    bool shift = e.shift;
                    if (shift)
                    {
                        if (_selectedCells.Contains(cell)) _selectedCells.Remove(cell);
                        else _selectedCells.Add(cell);
                    }
                    else
                    {
                        _selectedCells.Clear();
                        _selectedCells.Add(cell);
                    }
                }
                else if (!e.shift) _selectedCells.Clear();
                e.Use();
                GUIUtility.hotControl = 0;
            }
        }
        else
        {
            if (e.type == EventType.MouseDown && e.button == 0 && hit)
            {
                _dragging = true;
                _dragErase = _cellInputMode == 1;
                _rangeStart = _rangeEnd = cell;
                _rangePress = e.mousePosition;
                _rangeDragged = false;
                GUIUtility.hotControl = id;
                e.Use();
            }
            else if (e.type == EventType.MouseDrag && _dragging)
            {
                if ((e.mousePosition - _rangePress).sqrMagnitude >= 16f) _rangeDragged = true;
                if (hit) _rangeEnd = cell;
                e.Use();
            }
            else if (e.type == EventType.MouseUp && e.button == 0 && _dragging)
            {
                if (!new Rect(0, 0, sv.position.width, sv.position.height).Contains(e.mousePosition)) ReleaseCellInput();
                else if (_rangeDragged)
                { _heightAdjust = true; _heightMouseY = e.mousePosition.y; }
                else { _rangeEnd = _rangeStart; CommitCellRange(); ReleaseCellInput(); }
                e.Use();
            }
        }

        sv.Repaint();
    }

    private bool TryGetCellTarget(Ray ray, out Vector3Int cell)
    {
        cell = Vector3Int.zero;
        if (!_dragging)
        {
            float nearest = float.PositiveInfinity;
            Vector3Int normal = Vector3Int.zero;
            bool found = false;
            foreach (var candidate in _cells)
            {
                var center = (Vector3)candidate + Vector3.one * .5f;
                if (!new Bounds(center, Vector3.one).IntersectRay(ray, out float distance) || distance >= nearest) continue;
                nearest = distance;
                cell = candidate;
                found = true;
                var offset = ray.GetPoint(distance) - center;
                var abs = new Vector3(Mathf.Abs(offset.x), Mathf.Abs(offset.y), Mathf.Abs(offset.z));
                normal = abs.y >= abs.x && abs.y >= abs.z ? new Vector3Int(0, offset.y >= 0 ? 1 : -1, 0)
                    : abs.x >= abs.z ? new Vector3Int(offset.x >= 0 ? 1 : -1, 0, 0)
                    : new Vector3Int(0, 0, offset.z >= 0 ? 1 : -1);
            }
            if (found)
            {
                if (_cellInputMode == 0) cell += normal;
                return true;
            }
            if (_cellInputMode == 2) return false;
        }
        int y = _dragging ? _rangeStart.y : 0;
        if (!new Plane(Vector3.up, new Vector3(0, y, 0)).Raycast(ray, out float hitDistance)) return false;
        var point = ray.GetPoint(hitDistance);
        cell = new Vector3Int(Mathf.FloorToInt(point.x), y, Mathf.FloorToInt(point.z));
        return true;
    }

    private void CommitCellRange()
    {
        var min = Vector3Int.Min(_rangeStart, _rangeEnd);
        var max = Vector3Int.Max(_rangeStart, _rangeEnd);
        long width = (long)max.x - min.x + 1, height = (long)max.y - min.y + 1, depth = (long)max.z - min.z + 1;
        long count = width > MaxRangeCells || height > MaxRangeCells || depth > MaxRangeCells ? MaxRangeCells + 1L : width * height * depth;
        if (count > MaxRangeCells || count <= 0) { ShowNotification(new GUIContent("범위는 100,000셀 이하로 지정하세요.")); return; }
        var next = new HashSet<Vector3Int>(_cells);
        for (int x = 0; x < width; x++)
            for (int y = 0; y < height; y++)
                for (int z = 0; z < depth; z++)
                { var c = new Vector3Int(min.x + x, min.y + y, min.z + z); if (_dragErase) next.Remove(c); else next.Add(c); }
        if (next.SetEquals(_cells)) return;
        Undo.RegisterCompleteObjectUndo(this, _dragErase ? "셀 범위 삭제" : "셀 범위 추가");
        _cells = next.ToList();
        _selectedCells.RemoveAll(c => !next.Contains(c));
        _isDirty = true;
    }

    private static readonly Color ColSelected = new Color(1f, 0.85f, 0f, 0.55f);

    private void DrawCellGizmos()
    {
        if (_dragging)
        {
            var min = Vector3Int.Min(_rangeStart, _rangeEnd);
            var max = Vector3Int.Max(_rangeStart, _rangeEnd);
            Vector3 size = (Vector3)max - (Vector3)min + Vector3.one;
            Vector3 center = (Vector3)min + size * .5f;
            Handles.color = _dragErase ? ColErase : ColCellEdge;
            Handles.DrawWireCube(center, size);
            Handles.Label(center + Vector3.up * size.y * .5f,
                $"{size.x} × {size.y} × {size.z}  " + (_heightAdjust ? "위아래: 높이 / 클릭: 확정 / Esc: 취소" : "놓기: 높이 조절"));
        }
        bool inSelectMode = _cellInputMode == 2 && _mode == EditMode.Cell;
        foreach (var c in _cells)
        {
            bool hover  = _hasHovered && _mode == EditMode.Cell && c == _hoveredCell;
            bool sel    = inSelectMode && _selectedCells.Contains(c);
            Color fill  = sel    ? ColSelected
                        : hover  ? (_dragging && _dragErase ? ColErase : ColHover)
                                 : ColCell;
            DrawCell(sel ? c + _movePreview : c, fill, ColCellEdge);
        }

        // 호버 미리보기 (셀 없는 위치)
        if (_hasHovered && _mode == EditMode.Cell && !_cells.Contains(_hoveredCell))
            DrawCell(_hoveredCell, _dragging && _dragErase ? ColErase : ColHover, ColCellEdge);

        // 편집 평면 그리드
        if (_mode == EditMode.Cell)
        {
            Handles.color = new Color(1f, 1f, 1f, 0.07f);
            float y = _dragging ? _rangeStart.y : (_hasHovered ? _hoveredCell.y : 0);
            for (int i = -10; i <= 10; i++)
            {
                Handles.DrawLine(new Vector3(-10, y, i),  new Vector3(10, y, i));
                Handles.DrawLine(new Vector3(i,  y, -10), new Vector3(i, y, 10));
            }
        }
    }

    private void DrawPortalGizmos()
    {
        if (_roomRoot == null) return;
        foreach (var auth in _roomRoot.GetComponentsInChildren<RoomPortalAuthoring>(true))
        {
            if (auth == null) continue;
            bool sel = auth == _selectedPortal;

            Vector3 pos  = auth.transform.position;
            Quaternion q = Quaternion.Euler(0f, auth.transform.eulerAngles.y, 0f);
            Vector3 fwd  = q * Vector3.forward;
            Vector3 rgt  = q * Vector3.right;
            float w = auth.openingSize.x, h = auth.openingSize.y;

            // 개구부 사각형 — pos는 개구부 중심이므로 ±h/2 로 그림 (BlockReservationBuilder 기준 일치)
            Color fc = sel ? new Color(0.2f, 1f, 0.5f, 0.55f) : ColPortal;
            var corners = new Vector3[]
            {
                pos + rgt * (-w * 0.5f) - Vector3.up * (h * 0.5f),
                pos + rgt * ( w * 0.5f) - Vector3.up * (h * 0.5f),
                pos + rgt * ( w * 0.5f) + Vector3.up * (h * 0.5f),
                pos + rgt * (-w * 0.5f) + Vector3.up * (h * 0.5f),
            };
            Handles.DrawSolidRectangleWithOutline(corners, fc, ColPortalDir);

            // 방향 화살표
            Handles.color = sel ? ColPortalDir : new Color(0.1f, 0.9f, 0.3f, 0.6f);
            Handles.ArrowHandleCap(0, pos, Quaternion.LookRotation(fwd), 0.6f, EventType.Repaint);

            // 예약 박스 — BlockReservationBuilder 와 동일: -forward 방향 오프셋, 수직 오프셋 없음
            if (auth.reservationDepth > 0f)
            {
                Vector3 boxCenter = pos - fwd * (auth.reservationDepth * 0.5f);
                Handles.color = ColReservEdge;
                Matrix4x4 old = Handles.matrix;
                Handles.matrix = Matrix4x4.TRS(boxCenter, q, Vector3.one);
                Handles.DrawWireCube(Vector3.zero, new Vector3(w, h, auth.reservationDepth));
                Handles.matrix = old;
            }
        }
    }

    private static void DrawCell(Vector3Int pos, Color fill, Color edge)
    {
        Vector3 c = new Vector3(pos.x + 0.5f, pos.y + 0.5f, pos.z + 0.5f);
        // 상단 면 (상면만 솔리드)
        Handles.DrawSolidRectangleWithOutline(new[]
        {
            c + new Vector3(-0.5f, 0.5f,  0.5f),
            c + new Vector3( 0.5f, 0.5f,  0.5f),
            c + new Vector3( 0.5f, 0.5f, -0.5f),
            c + new Vector3(-0.5f, 0.5f, -0.5f),
        }, fill, edge);
        Handles.color = edge;
        Handles.DrawWireCube(c, Vector3.one);
    }
}


