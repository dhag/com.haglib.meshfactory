// Assets/Editor/SimpleMeshFactory.cs
// 階層型Undoシステム統合済みメッシュエディタ
// MeshData（Vertex/Face）ベース対応版

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.UndoSystem;
using MeshFactory.Data;
using MeshFactory.Transforms;
using MeshFactory.Tools;

public class SimpleMeshFactory : EditorWindow
{
    // ================================================================
    // メッシュデータ（内部クラス → MeshDataと名前衝突を避けるためリネーム）
    // ================================================================
    private class MeshEntry
    {
        public string Name;
        public Mesh Mesh;                           // Unity Mesh（表示用）
        public MeshData Data;                       // 新構造データ
        public Vector3[] OriginalPositions;         // 元の頂点位置（リセット用）
    }

    private List<MeshEntry> _meshList = new List<MeshEntry>();
    private int _selectedIndex = -1;
    private Vector2 _vertexScroll;

    // ================================================================
    // マテリアル管理
    // ================================================================
    private Material _registeredMaterial;

    // ================================================================
    // プレビュー
    // ================================================================
    private PreviewRenderUtility _preview;
    private float _rotationY = 0f;
    private float _rotationX = 20f;
    private float _cameraDistance = 2f;
    private Vector3 _cameraTarget = Vector3.zero;

    // ================================================================
    // 頂点編集
    // ================================================================
    private Vector3[] _vertexOffsets;       // 各Vertexのオフセット
    private Vector3[] _groupOffsets;        // グループオフセット（後方互換用、Vertexと1:1）

    // 頂点選択
    private HashSet<int> _selectedVertices = new HashSet<int>();

    // 編集状態（共通の選択処理用）
    private enum VertexEditState
    {
        Idle,              // 待機
        PendingAction,     // MouseDown後、ドラッグかクリックか判定中
        BoxSelecting       // 矩形選択中
    }
    private VertexEditState _editState = VertexEditState.Idle;

    // マウス操作用
    private Vector2 _mouseDownScreenPos;      // MouseDown時のスクリーン座標
    private int _hitVertexOnMouseDown = -1;   // MouseDown時にヒットした頂点（-1なら空白）
    private Vector2 _boxSelectStart;          // 矩形選択開始点
    private Vector2 _boxSelectEnd;            // 矩形選択終了点
    private const float DragThreshold = 4f;   // ドラッグ判定の閾値（ピクセル）

    // 表示設定
    private bool _showWireframe = true;
    private bool _showVertices = true;
    private bool _vertexEditMode = true;  // Show Verticesと連動

    // ================================================================
    // ツールモード
    // ================================================================
    private IEditTool _currentTool;
    private SelectTool _selectTool;
    private MoveTool _moveTool;
    private AddFaceTool _addFaceTool;
    private KnifeTool _knifeTool;
    private EdgeTopologyTool _edgeTopoTool;
    private AdvancedSelectTool _advancedSelectTool;
    private SculptTool _sculptTool;
    private ToolContext _toolContext;

    // ツール設定（シリアライズ対象）
    [SerializeField] private bool _useMagnet = false;
    [SerializeField] private float _magnetRadius = 0.5f;
    [SerializeField] private FalloffType _magnetFalloff = FalloffType.Smooth;

    // UIフォールドアウト状態
    private bool _foldDisplay = true;
    private bool _foldPrimitive = true;
    private bool _foldSelection = true;
    private bool _foldTools = true;
    private bool _foldWorkPlane = false;  // WorkPlaneセクション
    private Vector2 _leftPaneScroll;  // 左ペインのスクロール位置

    // WorkPlane表示設定
    private bool _showWorkPlaneGizmo = true;

    // ================================================================
    // Undoシステム統合
    // ================================================================
    private MeshFactoryUndoController _undoController;

    // スライダー編集用
    private bool _isSliderDragging = false;
    private Vector3[] _sliderDragStartOffsets;

    // カメラドラッグ用
    private bool _isCameraDragging = false;
    private float _cameraStartRotX, _cameraStartRotY;
    private float _cameraStartDistance;
    private Vector3 _cameraStartTarget;
    private WorkPlaneSnapshot? _cameraStartWorkPlaneSnapshot;

    // ================================================================
    // ウインドウ初期化
    // ================================================================
    [MenuItem("Tools/SimpleMeshFactory")]
    private static void Open()
    {
        var window = GetWindow<SimpleMeshFactory>("SimpleMeshFactory");
        window.minSize = new Vector2(700, 500);
    }

    private void OnEnable()
    {
        InitPreview();
        wantsMouseMove = true;

        // Undoコントローラー初期化
        _undoController = new MeshFactoryUndoController("SimpleMeshFactory");
        _undoController.OnUndoRedoPerformed += OnUndoRedoPerformed;

        // Show Verticesと編集モードを同期
        _vertexEditMode = _showVertices;

        // ツール初期化
        InitializeTools();

        // 初期ツール名をEditorStateに設定
        if (_undoController != null && _currentTool != null)
        {
            _undoController.EditorState.CurrentToolName = _currentTool.Name;
        }

        // WorkPlane UIイベントハンドラ設定
        SetupWorkPlaneEventHandlers();
    }

    private void InitializeTools()
    {
        _selectTool = new SelectTool();
        _moveTool = new MoveTool();
        _addFaceTool = new AddFaceTool();
        _knifeTool = new KnifeTool();
        _edgeTopoTool = new EdgeTopologyTool();
        _advancedSelectTool = new AdvancedSelectTool();
        _sculptTool = new SculptTool();
        _currentTool = _selectTool;

        // MoveToolに保存された設定を反映
        SyncToolSettings();

        _toolContext = new ToolContext
        {
            RecordSelectionChange = RecordSelectionChange,
            Repaint = Repaint,
            WorldToScreenPos = WorldToPreviewPos,
            ScreenDeltaToWorldDelta = ScreenDeltaToWorldDelta,
            FindVertexAtScreenPos = FindVertexAtScreenPos,
            ScreenPosToRay = ScreenPosToRay,
            WorkPlane = _undoController?.WorkPlane
        };
    }

    /// <summary>
    /// SimpleMeshFactoryの設定をToolに同期
    /// </summary>
    private void SyncToolSettings()
    {
        if (_moveTool != null)
        {
            _moveTool.UseMagnet = _useMagnet;
            _moveTool.MagnetRadius = _magnetRadius;
            _moveTool.MagnetFalloff = _magnetFalloff;
        }
    }

    /// <summary>
    /// Toolの設定をSimpleMeshFactoryに同期（シリアライズ用）
    /// </summary>
    private void SyncSettingsFromTool()
    {
        if (_moveTool != null)
        {
            _useMagnet = _moveTool.UseMagnet;
            _magnetRadius = _moveTool.MagnetRadius;
            _magnetFalloff = _moveTool.MagnetFalloff;
        }
    }

    private void UpdateToolContext(MeshEntry entry, Rect rect, Vector3 camPos, float camDist)
    {
        _toolContext.MeshData = entry?.Data;
        _toolContext.OriginalPositions = entry?.OriginalPositions;
        _toolContext.PreviewRect = rect;
        _toolContext.CameraPosition = camPos;
        _toolContext.CameraTarget = _cameraTarget;
        _toolContext.CameraDistance = camDist;
        _toolContext.SelectedVertices = _selectedVertices;
        _toolContext.VertexOffsets = _vertexOffsets;
        _toolContext.GroupOffsets = _groupOffsets;
        _toolContext.UndoController = _undoController;
        _toolContext.WorkPlane = _undoController?.WorkPlane;
        _toolContext.SyncMesh = () => SyncMeshFromData(entry);
    }

    private void OnDisable()
    {
        CleanupPreview();
        CleanupMeshes();

        if (_previewMaterial != null)
        {
            DestroyImmediate(_previewMaterial);
            _previewMaterial = null;
        }

        // WorkPlane UIイベントハンドラ解除
        CleanupWorkPlaneEventHandlers();

        // Undoコントローラー破棄
        if (_undoController != null)
        {
            _undoController.OnUndoRedoPerformed -= OnUndoRedoPerformed;
            _undoController.Dispose();
            _undoController = null;
        }
    }

    /// <summary>
    /// Undo/Redo実行後のコールバック
    /// </summary>
    private void OnUndoRedoPerformed()
    {
        // コンテキストからメッシュに反映
        if (_selectedIndex >= 0 && _selectedIndex < _meshList.Count)
        {
            var entry = _meshList[_selectedIndex];
            var ctx = _undoController.MeshContext;

            if (ctx.MeshData != null)
            {
                // MeshDataの変更をUnity Meshに反映（空のメッシュでも更新）
                entry.Data = ctx.MeshData.Clone();
                SyncMeshFromData(entry);

                // 頂点がある場合のみオフセット更新
                if (ctx.MeshData.VertexCount > 0)
                {
                    UpdateOffsetsFromData(entry);
                }
            }

            // 選択状態を同期
            if (ctx.SelectedVertices != null)
            {
                _selectedVertices = new HashSet<int>(ctx.SelectedVertices);
            }
        }

        // EditorStateContextからUI状態を復元
        var editorState = _undoController.EditorState;
        _rotationX = editorState.RotationX;
        _rotationY = editorState.RotationY;
        _cameraDistance = editorState.CameraDistance;
        _cameraTarget = editorState.CameraTarget;
        _showWireframe = editorState.ShowWireframe;
        _showVertices = editorState.ShowVertices;
        // _vertexEditMode はUndo対象外（ツールモードを維持）

        // ツールを復元
        RestoreToolFromName(editorState.CurrentToolName);

        // ツール状態をリセット（ドラッグ中などの状態をクリア）
        _currentTool?.Reset();
        ResetEditState();

        Repaint();
    }

    /// <summary>
    /// ツール名からツールを復元
    /// </summary>
    private void RestoreToolFromName(string toolName)
    {
        if (string.IsNullOrEmpty(toolName))
            return;

        IEditTool newTool = null;
        switch (toolName)
        {
            case "Select":
                newTool = _selectTool;
                break;
            case "Move":
                newTool = _moveTool;
                break;
            case "Add Face":
                newTool = _addFaceTool;
                break;
            case "Knife":
                newTool = _knifeTool;
                break;
            case "Wire":
            case "EdgeTopo":
                newTool = _edgeTopoTool;
                break;
            case "Sel+":
                newTool = _advancedSelectTool;
                break;
            case "Sculpt":
                newTool = _sculptTool;
                break;
            default:
                newTool = _selectTool;
                break;
        }

        if (newTool != null && newTool != _currentTool)
        {
            _currentTool?.OnDeactivate(_toolContext);
            _currentTool = newTool;
            _currentTool?.OnActivate(_toolContext);
        }
    }

    /// <summary>
    /// MeshDataからUnity Meshを再生成
    /// </summary>
    private void SyncMeshFromData(MeshEntry entry)
    {
        if (entry.Data == null || entry.Mesh == null)
            return;

        var newMesh = entry.Data.ToUnityMesh();
        entry.Mesh.Clear();
        entry.Mesh.vertices = newMesh.vertices;
        entry.Mesh.triangles = newMesh.triangles;
        entry.Mesh.uv = newMesh.uv;
        entry.Mesh.normals = newMesh.normals;
        entry.Mesh.RecalculateBounds();

        DestroyImmediate(newMesh);
    }

    /// <summary>
    /// MeshDataからオフセットを更新
    /// </summary>
    private void UpdateOffsetsFromData(MeshEntry entry)
    {
        if (entry.Data == null || _vertexOffsets == null)
            return;

        int count = Mathf.Min(entry.Data.VertexCount, _vertexOffsets.Length);
        for (int i = 0; i < count; i++)
        {
            if (i < entry.OriginalPositions.Length)
            {
                _vertexOffsets[i] = entry.Data.Vertices[i].Position - entry.OriginalPositions[i];
            }
        }

        // グループオフセットも更新（Vertexと1:1）
        if (_groupOffsets != null)
        {
            for (int i = 0; i < count && i < _groupOffsets.Length; i++)
            {
                _groupOffsets[i] = _vertexOffsets[i];
            }
        }
    }

    private void InitPreview()
    {
        _preview = new PreviewRenderUtility();
        _preview.cameraFieldOfView = 30f;
        _preview.camera.nearClipPlane = 0.01f;
        _preview.camera.farClipPlane = 100f;
    }

    private void CleanupPreview()
    {
        if (_preview != null)
        {
            _preview.Cleanup();
            _preview = null;
        }
    }

    private void CleanupMeshes()
    {
        foreach (var entry in _meshList)
        {
            if (entry.Mesh != null)
                DestroyImmediate(entry.Mesh);
        }
        _meshList.Clear();
    }

    // ================================================================
    // メインGUI
    // ================================================================
    private void OnGUI()
    {
        // Undoショートカット処理
        if (_undoController != null)
        {
            _undoController.HandleKeyboardShortcuts(Event.current);
        }

        // グローバルなドラッグ終了検出
        Event e = Event.current;
        if (e.type == EventType.MouseUp)
        {
            if (_isSliderDragging)
            {
                EndSliderDrag();
            }
            if (_isCameraDragging)
            {
                EndCameraDrag();
            }
        }

        HandleScrollWheel();

        EditorGUILayout.BeginHorizontal();

        // 左ペイン：メッシュリスト
        DrawMeshList();

        // 中央ペイン：プレビュー
        DrawPreview();

        // 右ペイン：頂点編集
        DrawVertexEditor();

        EditorGUILayout.EndHorizontal();
    }

    private void HandleScrollWheel()
    {
        Event e = Event.current;

        // 中ボタンドラッグで視点XY移動（パン）
        if (e.type == EventType.MouseDrag && e.button == 2)
        {
            if (!_isCameraDragging)
            {
                BeginCameraDrag();
            }

            Quaternion rot = Quaternion.Euler(_rotationX, _rotationY, 0);
            Vector3 right = rot * Vector3.right;
            Vector3 up = rot * Vector3.up;

            float panSpeed = _cameraDistance * 0.002f;
            _cameraTarget -= right * e.delta.x * panSpeed;
            _cameraTarget += up * e.delta.y * panSpeed;

            e.Use();
            Repaint();
            return;
        }

        // ホイールでズーム
        if (e.type == EventType.ScrollWheel)
        {
            if (!_isCameraDragging)
            {
                BeginCameraDrag();
            }

            _cameraDistance *= (1f + e.delta.y * 0.05f);
            _cameraDistance = Mathf.Clamp(_cameraDistance, 0.1f, 10f);

            EndCameraDrag();

            e.Use();
            Repaint();
        }
    }

    // ================================================================
    // カメラドラッグのUndo
    // ================================================================

    private void BeginCameraDrag()
    {
        if (_isCameraDragging) return;

        _isCameraDragging = true;
        _cameraStartRotX = _rotationX;
        _cameraStartRotY = _rotationY;
        _cameraStartDistance = _cameraDistance;
        _cameraStartTarget = _cameraTarget;

        // WorkPlane連動用：開始時のスナップショットを保存
        var workPlane = _undoController?.WorkPlane;
        if (workPlane != null && workPlane.Mode == WorkPlaneMode.CameraParallel &&
            !workPlane.IsLocked && !workPlane.LockOrientation)
        {
            _cameraStartWorkPlaneSnapshot = workPlane.CreateSnapshot();
        }
        else
        {
            _cameraStartWorkPlaneSnapshot = null;
        }
    }

    private void EndCameraDrag()
    {
        if (!_isCameraDragging) return;
        _isCameraDragging = false;

        bool hasChanged =
            !Mathf.Approximately(_cameraStartRotX, _rotationX) ||
            !Mathf.Approximately(_cameraStartRotY, _rotationY) ||
            !Mathf.Approximately(_cameraStartDistance, _cameraDistance) ||
            Vector3.Distance(_cameraStartTarget, _cameraTarget) > 0.0001f;

        if (hasChanged && _undoController != null)
        {
            // WorkPlane連動チェック
            var workPlane = _undoController.WorkPlane;
            WorkPlaneSnapshot? oldWorkPlane = _cameraStartWorkPlaneSnapshot;
            WorkPlaneSnapshot? newWorkPlane = null;

            if (oldWorkPlane.HasValue && workPlane != null)
            {
                // 新しいカメラ姿勢でWorkPlane軸を更新
                Quaternion rot = Quaternion.Euler(_rotationX, _rotationY, 0);
                Vector3 camPos = _cameraTarget + rot * new Vector3(0, 0, -_cameraDistance);

                workPlane.UpdateFromCamera(camPos, _cameraTarget);
                newWorkPlane = workPlane.CreateSnapshot();

                // 変更がない場合はnullに戻す
                if (!oldWorkPlane.Value.IsDifferentFrom(newWorkPlane.Value))
                {
                    oldWorkPlane = null;
                    newWorkPlane = null;
                }
            }

            // Undo記録（WorkPlane連動版）
            _undoController.RecordViewChangeWithWorkPlane(
                _cameraStartRotX, _cameraStartRotY, _cameraStartDistance, _cameraStartTarget,
                _rotationX, _rotationY, _cameraDistance, _cameraTarget,
                oldWorkPlane, newWorkPlane);

            _undoController.SetEditorState(
                _rotationX, _rotationY, _cameraDistance, _cameraTarget,
                _showWireframe, _showVertices, _vertexEditMode);
        }

        _cameraStartWorkPlaneSnapshot = null;
    }

    // ================================================================
    // 左ペイン：メッシュリスト
    // ================================================================
    private void DrawMeshList()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(180)))
        {
            EditorGUILayout.LabelField("Mesh Factory", EditorStyles.boldLabel);

            // ================================================================
            // Undo/Redo ボタン（上部固定）
            // ================================================================
            EditorGUILayout.BeginHorizontal();
            using (new EditorGUI.DisabledScope(_undoController == null || !_undoController.CanUndo))
            {
                if (GUILayout.Button("↶ Undo"))
                {
                    _undoController?.Undo();
                }
            }
            using (new EditorGUI.DisabledScope(_undoController == null || !_undoController.CanRedo))
            {
                if (GUILayout.Button("Redo ↷"))
                {
                    _undoController?.Redo();
                }
            }
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(5);

            // ================================================================
            // スクロール領域開始（常にスクロールバー表示）
            // ================================================================
            _leftPaneScroll = EditorGUILayout.BeginScrollView(
                _leftPaneScroll,
                false,  // horizontal
                true,   // vertical - 常に表示
                GUIStyle.none,
                GUI.skin.verticalScrollbar,
                GUI.skin.scrollView);

            // ================================================================
            // Display セクション
            // ================================================================
            _foldDisplay = EditorGUILayout.Foldout(_foldDisplay, "Display", true);
            if (_foldDisplay)
            {
                EditorGUI.indentLevel++;

                EditorGUI.BeginChangeCheck();
                bool newShowWireframe = EditorGUILayout.Toggle("Wireframe", _showWireframe);
                bool newShowVertices = EditorGUILayout.Toggle("Show Vertices", _showVertices);
                bool newVertexEditMode = newShowVertices;

                if (EditorGUI.EndChangeCheck())
                {
                    bool hasDisplayChange =
                        newShowWireframe != _showWireframe ||
                        newShowVertices != _showVertices ||
                        newVertexEditMode != _vertexEditMode;

                    if (hasDisplayChange && _undoController != null)
                    {
                        _undoController.BeginEditorStateDrag();
                    }

                    _showWireframe = newShowWireframe;
                    _showVertices = newShowVertices;
                    _vertexEditMode = newVertexEditMode;

                    if (_undoController != null)
                    {
                        _undoController.EditorState.ShowWireframe = _showWireframe;
                        _undoController.EditorState.ShowVertices = _showVertices;
                        _undoController.EditorState.VertexEditMode = _vertexEditMode;
                        _undoController.EndEditorStateDrag("Change Display Settings");
                    }
                }

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Zoom", EditorStyles.miniLabel);
                EditorGUI.BeginChangeCheck();
                float newDist = EditorGUILayout.Slider(_cameraDistance, 0.1f, 10f);
                if (EditorGUI.EndChangeCheck() && !Mathf.Approximately(newDist, _cameraDistance))
                {
                    if (!_isCameraDragging) BeginCameraDrag();
                    _cameraDistance = newDist;
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(3);

            // ================================================================
            // Primitive セクション
            // ================================================================
            _foldPrimitive = EditorGUILayout.Foldout(_foldPrimitive, "Primitive", true);
            if (_foldPrimitive)
            {
                EditorGUI.indentLevel++;

                // Empty Mesh
                if (GUILayout.Button("+ Empty"))
                {
                    CreateEmptyMesh();
                }

                // Clear All
                if (GUILayout.Button("Clear All"))
                {
                    CleanupMeshes();
                    _selectedIndex = -1;
                    _vertexOffsets = null;
                    _groupOffsets = null;
                    _undoController?.VertexEditStack.Clear();
                }

                EditorGUILayout.Space(3);

                // Load Mesh
                EditorGUILayout.LabelField("Load Mesh", EditorStyles.miniBoldLabel);
                if (GUILayout.Button("From Mesh Asset..."))
                {
                    LoadMeshFromAsset();
                }
                if (GUILayout.Button("From Prefab..."))
                {
                    LoadMeshFromPrefab();
                }
                if (GUILayout.Button("From Selection"))
                {
                    LoadMeshFromSelection();
                }

                EditorGUILayout.Space(3);

                // Create Mesh
                EditorGUILayout.LabelField("Create Mesh", EditorStyles.miniBoldLabel);
                if (GUILayout.Button("+ Cube..."))
                {
                    CubeMeshCreatorWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ Plane..."))
                {
                    PlaneMeshCreatorWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ Pyramid..."))
                {
                    PyramidMeshCreatorWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ Capsule..."))
                {
                    CapsuleMeshCreatorWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ Cylinder..."))
                {
                    CylinderMeshCreatorWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ Sphere..."))
                {
                    SphereMeshCreatorWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ Revolution..."))
                {
                    RevolutionMeshCreatorWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ 2D Profile..."))
                {
                    Profile2DExtrudeWindow.Open(OnMeshDataCreated);
                }
                if (GUILayout.Button("+ NohMask..."))
                {
                    NohMaskMeshCreatorWindow.Open(OnMeshDataCreated);
                }

                EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space(3);

            // ================================================================
            // Selection セクション（編集モード時のみ）
            // ================================================================
            if (_vertexEditMode)
            {
                _undoController?.FocusVertexEdit();

                _foldSelection = EditorGUILayout.Foldout(_foldSelection, "Selection", true);
                if (_foldSelection)
                {
                    EditorGUI.indentLevel++;

                    int totalVertices = 0;
                    if (_selectedIndex >= 0 && _selectedIndex < _meshList.Count)
                    {
                        var entry = _meshList[_selectedIndex];
                        if (entry.Data != null)
                        {
                            totalVertices = entry.Data.VertexCount;
                        }
                    }

                    EditorGUILayout.LabelField($"Selected: {_selectedVertices.Count} / {totalVertices}", EditorStyles.miniLabel);

                    using (new EditorGUILayout.HorizontalScope())
                    {
                        if (GUILayout.Button("All", GUILayout.Width(40)))
                        {
                            SelectAllVertices();
                        }
                        if (GUILayout.Button("None", GUILayout.Width(40)))
                        {
                            ClearSelection();
                        }
                        if (GUILayout.Button("Invert", GUILayout.Width(50)))
                        {
                            InvertSelection();
                        }
                    }

                    // 削除ボタン（選択があるときのみ有効）
                    using (new EditorGUI.DisabledScope(_selectedVertices.Count == 0))
                    {
                        var oldColor = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(1f, 0.6f, 0.6f); // 薄い赤
                        if (GUILayout.Button("Delete Selected"))
                        {
                            DeleteSelectedVertices();
                        }
                        GUI.backgroundColor = oldColor;
                    }

                    // マージボタン（2つ以上選択があるときのみ有効）
                    using (new EditorGUI.DisabledScope(_selectedVertices.Count < 2))
                    {
                        var oldColor = GUI.backgroundColor;
                        GUI.backgroundColor = new Color(0.6f, 0.8f, 1f); // 薄い青
                        if (GUILayout.Button("Merge Selected"))
                        {
                            MergeSelectedVertices();
                        }
                        GUI.backgroundColor = oldColor;
                    }

                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(3);

                // ================================================================
                // Tools セクション
                // ================================================================
                _foldTools = EditorGUILayout.Foldout(_foldTools, "Tools", true);
                if (_foldTools)
                {
                    EditorGUI.indentLevel++;

                    // ツールボタン（排他選択）
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawToolButton(_selectTool, "Select");
                        DrawToolButton(_moveTool, "Move");
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawToolButton(_addFaceTool, "AddFace");
                        DrawToolButton(_knifeTool, "Knife");
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawToolButton(_edgeTopoTool, "EdgeTopo");
                        DrawToolButton(_advancedSelectTool, "Sel+");
                    }
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        DrawToolButton(_sculptTool, "Sculpt");
                    }

                    // 現在のツールの設定UI
                    EditorGUILayout.Space(3);
                    _currentTool?.DrawSettingsUI();

                    // ツール設定をシリアライズ用に同期
                    SyncSettingsFromTool();

                    EditorGUI.indentLevel--;
                }

                EditorGUILayout.Space(3);

                // ================================================================
                // Work Plane セクション
                // ================================================================
                // WorkPlane UIは内部でFoldout管理
                DrawWorkPlaneUI();

                // ギズモ表示トグル（WorkPlane展開時のみ表示）
                if (_undoController?.WorkPlane?.IsExpanded == true)
                {
                    EditorGUI.BeginChangeCheck();
                    _showWorkPlaneGizmo = EditorGUILayout.ToggleLeft("Show Gizmo", _showWorkPlaneGizmo);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Repaint();
                    }
                }
            }
            else
            {
                _undoController?.FocusView();
            }

            EditorGUILayout.Space(5);

            // ================================================================
            // メッシュリスト
            // ================================================================
            EditorGUILayout.LabelField("Mesh List", EditorStyles.miniBoldLabel);

            for (int i = 0; i < _meshList.Count; i++)
            {
                EditorGUILayout.BeginHorizontal();

                bool isSelected = (i == _selectedIndex);
                bool newSelected = GUILayout.Toggle(isSelected, _meshList[i].Name, "Button");

                if (newSelected && !isSelected)
                {
                    _selectedIndex = i;
                    _selectedVertices.Clear();
                    ResetEditState();
                    InitVertexOffsets();

                    var entry = _meshList[_selectedIndex];
                    LoadEntryToUndoController(entry);
                }

                if (GUILayout.Button("×", GUILayout.Width(20)))
                {
                    EditorGUILayout.EndHorizontal();
                    RemoveMesh(i);
                    break;
                }

                EditorGUILayout.EndHorizontal();
            }

            EditorGUILayout.EndScrollView();
        }
    }

    /// <summary>
    /// ツールボタンを描画（トグル形式）
    /// </summary>
    private void DrawToolButton(IEditTool tool, string label)
    {
        bool isActive = (_currentTool == tool);

        // アクティブなツールは色を変える
        var oldColor = GUI.backgroundColor;
        if (isActive)
        {
            GUI.backgroundColor = new Color(0.6f, 0.8f, 1f);
        }

        if (GUILayout.Toggle(isActive, label, "Button") && !isActive)
        {
            // ツール変更をUndo記録
            if (_undoController != null)
            {
                string oldToolName = _currentTool?.Name ?? "Select";
                _undoController.EditorState.CurrentToolName = oldToolName;
                _undoController.BeginEditorStateDrag();
            }

            _currentTool?.OnDeactivate(_toolContext);
            _currentTool = tool;
            _currentTool?.OnActivate(_toolContext);

            // 新しいツール名を記録
            if (_undoController != null)
            {
                _undoController.EditorState.CurrentToolName = tool.Name;
                _undoController.EndEditorStateDrag($"Switch to {tool.Name} Tool");
            }
        }

        GUI.backgroundColor = oldColor;
    }

    /// <summary>
    /// MeshEntryをUndoコントローラーに読み込む
    /// </summary>
    private void LoadEntryToUndoController(MeshEntry entry)
    {
        if (_undoController == null || entry == null)
            return;

        // 参照を共有（Cloneしない）- AddFaceToolなどで直接変更されるため
        _undoController.SetMeshData(entry.Data, entry.Mesh);
        _undoController.MeshContext.OriginalPositions = entry.OriginalPositions;
        // 選択状態を同期
        _undoController.MeshContext.SelectedVertices = new HashSet<int>(_selectedVertices);
    }

    // ================================================================
    // メッシュ読み出し機能
    // ================================================================

    /// <summary>
    /// メッシュアセットから読み込み
    /// </summary>
    private void LoadMeshFromAsset()
    {
        string path = EditorUtility.OpenFilePanel("Select Mesh Asset", "Assets", "asset,fbx,obj");
        if (string.IsNullOrEmpty(path))
            return;

        // プロジェクト相対パスに変換
        if (path.StartsWith(Application.dataPath))
        {
            path = "Assets" + path.Substring(Application.dataPath.Length);
        }

        Mesh loadedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (loadedMesh == null)
        {
            // FBX/OBJの場合、サブアセットからメッシュを探す
            var allAssets = AssetDatabase.LoadAllAssetsAtPath(path);
            foreach (var asset in allAssets)
            {
                if (asset is Mesh m)
                {
                    loadedMesh = m;
                    break;
                }
            }
        }

        if (loadedMesh != null)
        {
            AddLoadedMesh(loadedMesh, loadedMesh.name);
        }
        else
        {
            EditorUtility.DisplayDialog("Error", "メッシュを読み込めませんでした", "OK");
        }
    }

    /// <summary>
    /// プレファブから読み込み
    /// </summary>
    private void LoadMeshFromPrefab()
    {
        string path = EditorUtility.OpenFilePanel("Select Prefab", "Assets", "prefab");
        if (string.IsNullOrEmpty(path))
            return;

        if (path.StartsWith(Application.dataPath))
        {
            path = "Assets" + path.Substring(Application.dataPath.Length);
        }

        GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (prefab == null)
        {
            EditorUtility.DisplayDialog("Error", "プレファブを読み込めませんでした", "OK");
            return;
        }

        // MeshFilterからメッシュを取得
        var meshFilters = prefab.GetComponentsInChildren<MeshFilter>(true);
        if (meshFilters.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "プレファブにMeshFilterが見つかりませんでした", "OK");
            return;
        }

        // 複数メッシュがある場合は全て追加
        foreach (var mf in meshFilters)
        {
            if (mf.sharedMesh != null)
            {
                string meshName = $"{prefab.name}_{mf.sharedMesh.name}";
                AddLoadedMesh(mf.sharedMesh, meshName);

                // マテリアルも取得（最初のものだけ）
                if (_registeredMaterial == null)
                {
                    var renderer = mf.GetComponent<MeshRenderer>();
                    if (renderer != null && renderer.sharedMaterial != null)
                    {
                        _registeredMaterial = renderer.sharedMaterial;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 選択中のオブジェクトから読み込み
    /// </summary>
    private void LoadMeshFromSelection()
    {
        var selected = Selection.activeGameObject;
        if (selected == null)
        {
            // メッシュアセットが選択されている場合
            var selectedMesh = Selection.activeObject as Mesh;
            if (selectedMesh != null)
            {
                AddLoadedMesh(selectedMesh, selectedMesh.name);
                return;
            }

            EditorUtility.DisplayDialog("Info", "GameObjectまたはMeshを選択してください", "OK");
            return;
        }

        var meshFilters = selected.GetComponentsInChildren<MeshFilter>(true);
        if (meshFilters.Length == 0)
        {
            EditorUtility.DisplayDialog("Error", "選択オブジェクトにMeshFilterが見つかりませんでした", "OK");
            return;
        }

        foreach (var mf in meshFilters)
        {
            if (mf.sharedMesh != null)
            {
                string meshName = $"{selected.name}_{mf.sharedMesh.name}";
                AddLoadedMesh(mf.sharedMesh, meshName);

                if (_registeredMaterial == null)
                {
                    var renderer = mf.GetComponent<MeshRenderer>();
                    if (renderer != null && renderer.sharedMaterial != null)
                    {
                        _registeredMaterial = renderer.sharedMaterial;
                    }
                }
            }
        }
    }

    /// <summary>
    /// 読み込んだメッシュを追加（MeshDataに変換）
    /// </summary>
    private void AddLoadedMesh(Mesh sourceMesh, string name)
    {
        // Unity MeshからMeshDataに変換
        var meshData = new MeshData(name);
        meshData.FromUnityMesh(sourceMesh, true);

        // 表示用Unity Meshを作成
        Mesh displayMesh = meshData.ToUnityMesh();
        displayMesh.name = name;
        displayMesh.hideFlags = HideFlags.HideAndDontSave; // エディタ専用の一時メッシュ

        var entry = new MeshEntry
        {
            Name = name,
            Mesh = displayMesh,
            Data = meshData,
            OriginalPositions = meshData.Vertices.Select(v => v.Position).ToArray()
        };

        // Undo記録
        var beforeSnapshot = _undoController?.CaptureMeshDataSnapshot();

        _meshList.Add(entry);
        _selectedIndex = _meshList.Count - 1;
        InitVertexOffsets();

        LoadEntryToUndoController(entry);

        // Undo記録（メッシュ追加）
        if (_undoController != null && beforeSnapshot != null)
        {
            var afterSnapshot = _undoController.CaptureMeshDataSnapshot();
            _undoController.RecordTopologyChange(beforeSnapshot, afterSnapshot, $"Load Mesh: {name}");
        }

        Repaint();
    }

    /// <summary>
    /// メッシュ作成ウインドウからのコールバック（MeshData版 - 四角形を保持）
    /// </summary>
    private void OnMeshDataCreated(MeshData meshData, string name)
    {
        // MeshDataから表示用Unity Meshを生成
        Mesh mesh = meshData.ToUnityMesh();
        mesh.name = name;
        mesh.hideFlags = HideFlags.HideAndDontSave; // エディタ専用の一時メッシュ

        var entry = new MeshEntry
        {
            Name = name,
            Mesh = mesh,
            Data = meshData.Clone(),
            OriginalPositions = meshData.Vertices.Select(v => v.Position).ToArray()
        };

        _meshList.Add(entry);
        _selectedIndex = _meshList.Count - 1;
        InitVertexOffsets();

        LoadEntryToUndoController(entry);

        Repaint();
    }

    /// <summary>
    /// 空のメッシュを作成
    /// </summary>
    private void CreateEmptyMesh()
    {
        var meshData = new MeshData("Empty");
        OnMeshDataCreated(meshData, "Empty");
    }

    /// <summary>
    /// メッシュ作成ウインドウからのコールバック（従来版）
    /// </summary>
    private void OnMeshCreated(Mesh mesh, string name)
    {
        // Unity MeshからMeshDataに変換
        var meshData = new MeshData(name);
        meshData.FromUnityMesh(mesh, true);

        // エディタ専用の一時メッシュとしてマーク
        mesh.hideFlags = HideFlags.HideAndDontSave;

        // 元のMeshはそのまま表示用に使用
        var entry = new MeshEntry
        {
            Name = name,
            Mesh = mesh,
            Data = meshData,
            OriginalPositions = meshData.Vertices.Select(v => v.Position).ToArray()
        };

        _meshList.Add(entry);
        _selectedIndex = _meshList.Count - 1;
        InitVertexOffsets();

        LoadEntryToUndoController(entry);

        Repaint();
    }

    private void RemoveMesh(int index)
    {
        if (index < 0 || index >= _meshList.Count)
            return;

        var entry = _meshList[index];
        if (entry.Mesh != null)
        {
            DestroyImmediate(entry.Mesh);
        }

        _meshList.RemoveAt(index);

        // 頂点選択と編集状態をリセット
        _selectedVertices.Clear();
        ResetEditState();

        if (_selectedIndex >= _meshList.Count)
        {
            _selectedIndex = _meshList.Count - 1;
        }

        if (_selectedIndex >= 0)
        {
            InitVertexOffsets();
            var newEntry = _meshList[_selectedIndex];
            LoadEntryToUndoController(newEntry);
        }
        else
        {
            _vertexOffsets = null;
            _groupOffsets = null;
            _undoController?.VertexEditStack.Clear();
        }

        Repaint();
    }

    /// <summary>
    /// 頂点オフセット初期化（MeshDataベース）
    /// </summary>
    private void InitVertexOffsets()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _meshList.Count)
        {
            _vertexOffsets = null;
            _groupOffsets = null;
            return;
        }

        var entry = _meshList[_selectedIndex];
        var meshData = entry.Data;

        if (meshData == null)
        {
            _vertexOffsets = null;
            _groupOffsets = null;
            return;
        }

        // MeshDataのVertex数でオフセット配列を作成
        int vertexCount = meshData.VertexCount;
        _vertexOffsets = new Vector3[vertexCount];
        _groupOffsets = new Vector3[vertexCount];  // Vertexと1:1

        // カメラ設定
        var bounds = meshData.CalculateBounds();
        float radius = Mathf.Max(bounds.extents.magnitude, 0.5f);
        _cameraDistance = radius * 3.5f;
        _cameraTarget = bounds.center;
    }

    // ================================================================
    // 中央ペイン：プレビュー
    // ================================================================
    private void DrawPreview()
    {
        Rect rect = GUILayoutUtility.GetRect(
            200, 10000,
            200, 10000,
            GUILayout.ExpandWidth(true),
            GUILayout.ExpandHeight(true));

        if (_selectedIndex < 0 || _selectedIndex >= _meshList.Count || _preview == null)
        {
            EditorGUI.DrawRect(rect, new Color(0.2f, 0.2f, 0.2f));
            EditorGUI.LabelField(rect, "Select a mesh", EditorStyles.centeredGreyMiniLabel);
            return;
        }

        var entry = _meshList[_selectedIndex];
        var mesh = entry.Mesh;

        float dist = _cameraDistance;
        Quaternion rot = Quaternion.Euler(_rotationX, _rotationY, 0);
        Vector3 camPos = _cameraTarget + rot * new Vector3(0, 0, -dist);

        HandleInput(rect, entry, camPos, _cameraTarget, dist);

        if (Event.current.type != EventType.Repaint)
            return;

        _preview.BeginPreview(rect, GUIStyle.none);

        _preview.camera.clearFlags = CameraClearFlags.SolidColor;
        _preview.camera.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);

        _preview.camera.transform.position = camPos;
        _preview.camera.transform.LookAt(_cameraTarget);

        Material solidMat = GetPreviewMaterial();
        _preview.DrawMesh(mesh, Matrix4x4.identity, solidMat, 0);

        _preview.camera.Render();

        Texture result = _preview.EndPreview();
        GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);

        if (_showWireframe)
        {
            DrawWireframeOverlay(rect, entry.Data, camPos, _cameraTarget);
        }

        DrawVertexHandles(rect, entry.Data, camPos, _cameraTarget);

        // ローカル原点マーカー（点線、ドラッグ不可）
        DrawOriginMarker(rect, camPos, _cameraTarget);

        // ツールのギズモ描画
        UpdateToolContext(entry, rect, camPos, dist);
        _currentTool?.DrawGizmo(_toolContext);

        // WorkPlaneギズモ描画
        if (_showWorkPlaneGizmo && _vertexEditMode)
        {
            DrawWorkPlaneGizmo(rect, camPos, _cameraTarget);
        }
    }

    /// <summary>
    /// ローカル原点マーカーを点線で描画（ドラッグ不可）
    /// </summary>
    private void DrawOriginMarker(Rect previewRect, Vector3 camPos, Vector3 lookAt)
    {
        Vector3 origin = Vector3.zero;
        Vector2 originScreen = WorldToPreviewPos(origin, previewRect, camPos, lookAt);

        if (!previewRect.Contains(originScreen))
            return;

        float axisLength = 0.2f;

        // X軸（赤）点線
        Vector3 xEnd = origin + Vector3.right * axisLength;
        Vector2 xScreen = WorldToPreviewPos(xEnd, previewRect, camPos, lookAt);
        DrawDottedLine(originScreen, xScreen, new Color(1f, 0.3f, 0.3f, 0.7f));

        // Y軸（緑）点線
        Vector3 yEnd = origin + Vector3.up * axisLength;
        Vector2 yScreen = WorldToPreviewPos(yEnd, previewRect, camPos, lookAt);
        DrawDottedLine(originScreen, yScreen, new Color(0.3f, 1f, 0.3f, 0.7f));

        // Z軸（青）点線
        Vector3 zEnd = origin + Vector3.forward * axisLength;
        Vector2 zScreen = WorldToPreviewPos(zEnd, previewRect, camPos, lookAt);
        DrawDottedLine(originScreen, zScreen, new Color(0.3f, 0.3f, 1f, 0.7f));

        // 中心点（小さめ）
        float centerSize = 4f;
        EditorGUI.DrawRect(new Rect(
            originScreen.x - centerSize / 2,
            originScreen.y - centerSize / 2,
            centerSize,
            centerSize), new Color(1f, 1f, 1f, 0.7f));
    }

    /// <summary>
    /// 点線を描画
    /// </summary>
    private void DrawDottedLine(Vector2 from, Vector2 to, Color color)
    {
        Handles.BeginGUI();
        Handles.color = color;

        Vector2 dir = to - from;
        float length = dir.magnitude;
        dir.Normalize();

        float dashLength = 4f;
        float gapLength = 3f;
        float pos = 0f;

        while (pos < length)
        {
            float dashEnd = Mathf.Min(pos + dashLength, length);
            Vector2 dashStart = from + dir * pos;
            Vector2 dashEndPos = from + dir * dashEnd;
            Handles.DrawAAPolyLine(2f,
                new Vector3(dashStart.x, dashStart.y, 0),
                new Vector3(dashEndPos.x, dashEndPos.y, 0));
            pos += dashLength + gapLength;
        }

        Handles.EndGUI();
    }

    /// <summary>
    /// ワイヤーフレーム描画（MeshDataベース）
    /// </summary>
    private void DrawWireframeOverlay(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt)
    {
        if (meshData == null)
            return;

        var edges = new HashSet<(int, int)>();

        // 各面からエッジを抽出
        foreach (var face in meshData.Faces)
        {
            for (int i = 0; i < face.VertexCount; i++)
            {
                int a = face.VertexIndices[i];
                int b = face.VertexIndices[(i + 1) % face.VertexCount];
                AddEdge(edges, a, b);
            }
        }

        Handles.BeginGUI();
        Handles.color = new Color(0f, 1f, 0.5f, 0.9f);

        foreach (var edge in edges)
        {
            Vector3 p1World = meshData.Vertices[edge.Item1].Position;
            Vector3 p2World = meshData.Vertices[edge.Item2].Position;

            Vector2 p1 = WorldToPreviewPos(p1World, previewRect, camPos, lookAt);
            Vector2 p2 = WorldToPreviewPos(p2World, previewRect, camPos, lookAt);

            if (previewRect.Contains(p1) || previewRect.Contains(p2))
            {
                Handles.DrawLine(
                    new Vector3(p1.x, p1.y, 0),
                    new Vector3(p2.x, p2.y, 0));
            }
        }

        Handles.EndGUI();
    }

    private void AddEdge(HashSet<(int, int)> edges, int a, int b)
    {
        if (a > b) (a, b) = (b, a);
        edges.Add((a, b));
    }

    private Vector2 WorldToPreviewPos(Vector3 worldPos, Rect previewRect, Vector3 camPos, Vector3 lookAt)
    {
        Matrix4x4 view = Matrix4x4.LookAt(camPos, lookAt, Vector3.up);
        view = view.inverse;
        view.m20 *= -1; view.m21 *= -1; view.m22 *= -1; view.m23 *= -1;

        float aspect = previewRect.width / previewRect.height;
        Matrix4x4 proj = Matrix4x4.Perspective(_preview.cameraFieldOfView, aspect, 0.01f, 100f);

        Vector4 clipPos = proj * view * new Vector4(worldPos.x, worldPos.y, worldPos.z, 1f);

        if (clipPos.w <= 0)
            return new Vector2(-1000, -1000);

        Vector2 ndc = new Vector2(clipPos.x / clipPos.w, clipPos.y / clipPos.w);

        float screenX = previewRect.x + (ndc.x * 0.5f + 0.5f) * previewRect.width;
        float screenY = previewRect.y + (1f - (ndc.y * 0.5f + 0.5f)) * previewRect.height;

        return new Vector2(screenX, screenY);
    }

    /// <summary>
    /// スクリーン座標からレイを生成（WorldToPreviewPosの逆変換）
    /// </summary>
    private Ray ScreenPosToRay(Vector2 screenPos)
    {
        if (_toolContext == null)
            return new Ray(Vector3.zero, Vector3.forward);

        Rect previewRect = _toolContext.PreviewRect;
        Vector3 camPos = _toolContext.CameraPosition;
        Vector3 lookAt = _toolContext.CameraTarget;

        // スクリーン座標 → NDC (-1 to 1)
        float ndcX = ((screenPos.x - previewRect.x) / previewRect.width) * 2f - 1f;
        float ndcY = 1f - ((screenPos.y - previewRect.y) / previewRect.height) * 2f;

        // カメラの向きを計算
        Vector3 forward = (lookAt - camPos).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        if (right.sqrMagnitude < 0.001f)
        {
            right = Vector3.Cross(Vector3.forward, forward).normalized;
        }
        Vector3 up = Vector3.Cross(forward, right).normalized;

        // FOVからレイ方向を計算
        float fov = _preview != null ? _preview.cameraFieldOfView : 60f;
        float halfFovRad = fov * 0.5f * Mathf.Deg2Rad;
        float aspect = previewRect.width / previewRect.height;

        // NDCをカメラ空間の方向に変換
        Vector3 direction = forward
            + right * (ndcX * Mathf.Tan(halfFovRad) * aspect)
            + up * (ndcY * Mathf.Tan(halfFovRad));
        direction.Normalize();

        return new Ray(camPos, direction);
    }

    /// <summary>
    /// 頂点ハンドル描画（MeshDataベース）
    /// </summary>
    private void DrawVertexHandles(Rect previewRect, MeshData meshData, Vector3 camPos, Vector3 lookAt)
    {
        if (!_showVertices || meshData == null)
            return;

        float handleSize = 8f;

        for (int i = 0; i < meshData.VertexCount; i++)
        {
            Vector2 screenPos = WorldToPreviewPos(meshData.Vertices[i].Position, previewRect, camPos, lookAt);

            if (!previewRect.Contains(screenPos))
                continue;

            Rect handleRect = new Rect(
                screenPos.x - handleSize / 2,
                screenPos.y - handleSize / 2,
                handleSize,
                handleSize);

            // 選択状態で色分け
            bool isSelected = _selectedVertices.Contains(i);
            Color col = isSelected ? new Color(1f, 0.8f, 0f, 1f) : Color.white;  // 選択=オレンジ黄, 未選択=白
            EditorGUI.DrawRect(handleRect, col);

            Color borderCol = isSelected ? Color.red : Color.gray;
            DrawRectBorder(handleRect, borderCol);

            GUI.Label(new Rect(screenPos.x + 6, screenPos.y - 8, 30, 16), i.ToString(), EditorStyles.miniLabel);
        }

        // 矩形選択オーバーレイ
        if (_editState == VertexEditState.BoxSelecting)
        {
            DrawBoxSelectOverlay();
        }
    }

    /// <summary>
    /// 矩形選択オーバーレイを描画
    /// </summary>
    private void DrawBoxSelectOverlay()
    {
        Rect selectRect = new Rect(
            Mathf.Min(_boxSelectStart.x, _boxSelectEnd.x),
            Mathf.Min(_boxSelectStart.y, _boxSelectEnd.y),
            Mathf.Abs(_boxSelectEnd.x - _boxSelectStart.x),
            Mathf.Abs(_boxSelectEnd.y - _boxSelectStart.y)
        );

        // 半透明の塗りつぶし
        Color fillColor = new Color(0.3f, 0.6f, 1f, 0.2f);
        EditorGUI.DrawRect(selectRect, fillColor);

        // 枠線
        Color borderColor = new Color(0.3f, 0.6f, 1f, 0.8f);
        DrawRectBorder(selectRect, borderColor);
    }

    private void DrawRectBorder(Rect rect, Color color)
    {
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1, rect.width, 1), color);
        EditorGUI.DrawRect(new Rect(rect.x, rect.y, 1, rect.height), color);
        EditorGUI.DrawRect(new Rect(rect.xMax - 1, rect.y, 1, rect.height), color);
    }

    // ================================================================
    // 入力処理（MeshDataベース）
    // ================================================================
    private void HandleInput(Rect rect, MeshEntry entry, Vector3 camPos, Vector3 lookAt, float camDist)
    {
        Event e = Event.current;
        Vector2 mousePos = e.mousePosition;

        // ツールコンテキストを更新
        UpdateToolContext(entry, rect, camPos, camDist);

        // プレビュー外でのMouseUp処理
        if (!rect.Contains(mousePos))
        {
            if (e.type == EventType.MouseUp && e.button == 0)
            {
                HandleMouseUpOutside(entry, rect, camPos, lookAt);
            }
            return;
        }

        // 右ドラッグ: カメラ回転（常に有効）
        HandleCameraRotation(e);

        // 頂点編集モードでなければ終了
        if (!_vertexEditMode)
            return;

        var meshData = entry.Data;
        if (meshData == null)
            return;

        float handleRadius = 10f;

        // キーボードショートカット
        if (e.type == EventType.KeyDown)
        {
            HandleKeyboardShortcuts(e, entry);
        }

        switch (e.type)
        {
            case EventType.MouseDown:
                if (e.button == 0)
                {
                    // まずツールに処理を委譲
                    if (_currentTool != null && _currentTool.OnMouseDown(_toolContext, mousePos))
                    {
                        e.Use();
                    }
                    else
                    {
                        // ツールが処理しなければ共通の選択処理
                        OnMouseDown(e, mousePos, meshData, rect, camPos, lookAt, handleRadius, entry);
                    }
                }
                else if (e.button == 1)
                {
                    // 右クリック：まずツールに委譲（AddFaceTool等の点取り消し用）
                    if (_currentTool != null && _currentTool.OnMouseDown(_toolContext, mousePos))
                    {
                        e.Use();
                    }
                    // ツールが処理しなければカメラ操作（下のHandleCameraInputで処理）
                }
                break;

            case EventType.MouseDrag:
                if (e.button == 0)
                {
                    // ツールに処理を委譲
                    if (_currentTool != null && _currentTool.OnMouseDrag(_toolContext, mousePos, e.delta))
                    {
                        e.Use();
                    }
                    else
                    {
                        OnMouseDrag(e, mousePos, meshData, rect, camPos, lookAt, camDist, entry);
                    }
                }
                break;

            case EventType.MouseUp:
                if (e.button == 0)
                {
                    // ツールに処理を委譲
                    if (_currentTool != null && _currentTool.OnMouseUp(_toolContext, mousePos))
                    {
                        ResetEditState();  // ツールが処理した場合も状態リセット
                        e.Use();
                    }
                    else
                    {
                        OnMouseUp(e, mousePos, meshData, rect, camPos, lookAt, handleRadius, entry);
                    }
                }
                break;

            case EventType.MouseMove:
                // マウス移動時にツールのプレビュー更新を呼ぶ
                if (_currentTool != null)
                {
                    _currentTool.OnMouseDrag(_toolContext, mousePos, Vector2.zero);
                    Repaint();
                }
                break;
        }
    }

    /// <summary>
    /// MouseDown処理（共通の選択処理）
    /// </summary>
    private void OnMouseDown(Event e, Vector2 mousePos, MeshData meshData, Rect rect,
        Vector3 camPos, Vector3 lookAt, float handleRadius, MeshEntry entry)
    {
        if (_editState != VertexEditState.Idle)
            return;

        _mouseDownScreenPos = mousePos;

        // 頂点のヒットテスト
        _hitVertexOnMouseDown = FindVertexAtScreenPos(mousePos, meshData, rect, camPos, lookAt, handleRadius);
        _editState = VertexEditState.PendingAction;

        e.Use();
    }

    /// <summary>
    /// MouseDrag処理
    /// </summary>
    private void OnMouseDrag(Event e, Vector2 mousePos, MeshData meshData, Rect rect,
        Vector3 camPos, Vector3 lookAt, float camDist, MeshEntry entry)
    {
        switch (_editState)
        {
            case VertexEditState.PendingAction:
                // ドラッグ閾値を超えたか判定
                float dragDistance = Vector2.Distance(mousePos, _mouseDownScreenPos);
                if (dragDistance > DragThreshold)
                {
                    // 空白から開始 → 矩形選択モード
                    if (_hitVertexOnMouseDown < 0)
                    {
                        StartBoxSelect(mousePos);
                    }
                    else
                    {
                        // 頂点上からのドラッグはツールに委譲済み
                        // Selectツール時は何もしない
                        _editState = VertexEditState.Idle;
                    }
                }
                e.Use();
                Repaint();
                break;

            case VertexEditState.BoxSelecting:
                // 矩形選択範囲を更新
                _boxSelectEnd = mousePos;
                e.Use();
                Repaint();
                break;
        }
    }

    /// <summary>
    /// MouseUp処理（共通の選択処理）
    /// </summary>
    private void OnMouseUp(Event e, Vector2 mousePos, MeshData meshData, Rect rect,
        Vector3 camPos, Vector3 lookAt, float handleRadius, MeshEntry entry)
    {
        bool shiftHeld = e.shift;
        bool ctrlHeld = e.control;

        switch (_editState)
        {
            case VertexEditState.PendingAction:
                // ドラッグなし = クリック
                HandleClick(shiftHeld, meshData, rect, camPos, lookAt, handleRadius);
                break;

            case VertexEditState.BoxSelecting:
                // 矩形選択完了
                FinishBoxSelect(shiftHeld, ctrlHeld, meshData, rect, camPos, lookAt);
                break;
        }

        ResetEditState();
        e.Use();
        Repaint();
    }

    /// <summary>
    /// プレビュー外でのMouseUp処理
    /// </summary>
    private void HandleMouseUpOutside(MeshEntry entry, Rect rect, Vector3 camPos, Vector3 lookAt)
    {
        // ツールの状態をリセット
        _currentTool?.Reset();

        ResetEditState();
        Repaint();
    }

    /// <summary>
    /// 編集状態をリセット
    /// </summary>
    private void ResetEditState()
    {
        _editState = VertexEditState.Idle;
        _hitVertexOnMouseDown = -1;
        _boxSelectStart = Vector2.zero;
        _boxSelectEnd = Vector2.zero;
    }

    // ================================================================
    // クリック処理
    // ================================================================
    private void HandleClick(bool shiftHeld, MeshData meshData, Rect rect, Vector3 camPos, Vector3 lookAt, float handleRadius)
    {
        var oldSelection = new HashSet<int>(_selectedVertices);
        bool selectionChanged = false;

        if (_hitVertexOnMouseDown >= 0)
        {
            // 頂点上でクリック
            if (shiftHeld)
            {
                // Shift+クリック: トグル
                if (_selectedVertices.Contains(_hitVertexOnMouseDown))
                    _selectedVertices.Remove(_hitVertexOnMouseDown);
                else
                    _selectedVertices.Add(_hitVertexOnMouseDown);
                selectionChanged = true;
            }
            else
            {
                // クリック: その頂点のみ選択
                if (_selectedVertices.Count != 1 || !_selectedVertices.Contains(_hitVertexOnMouseDown))
                {
                    _selectedVertices.Clear();
                    _selectedVertices.Add(_hitVertexOnMouseDown);
                    selectionChanged = true;
                }
            }
        }
        else
        {
            // 空白でクリック
            if (!shiftHeld && _selectedVertices.Count > 0)
            {
                // 全選択解除
                _selectedVertices.Clear();
                selectionChanged = true;
            }
            // Shift+空白クリック: 何もしない
        }

        // 選択変更を記録
        if (selectionChanged)
        {
            RecordSelectionChange(oldSelection, _selectedVertices);
        }
    }

    /// <summary>
    /// 選択変更をUndoスタックに記録（WorkPlane原点連動）
    /// </summary>
    private void RecordSelectionChange(HashSet<int> oldSelection, HashSet<int> newSelection)
    {
        if (_undoController == null)
            return;

        // MeshContextの選択状態も更新
        _undoController.MeshContext.SelectedVertices = new HashSet<int>(newSelection);

        var workPlane = _undoController.WorkPlane;
        WorkPlaneSnapshot? oldWorkPlane = null;
        WorkPlaneSnapshot? newWorkPlane = null;

        // AutoUpdate有効かつロックされていない場合、WorkPlane原点も連動
        if (workPlane != null && workPlane.AutoUpdateOriginOnSelection && !workPlane.IsLocked)
        {
            // 変更前のスナップショット
            oldWorkPlane = workPlane.CreateSnapshot();

            // WorkPlane原点を更新
            if (_selectedIndex >= 0 && _selectedIndex < _meshList.Count)
            {
                var entry = _meshList[_selectedIndex];
                if (entry.Data != null && newSelection.Count > 0)
                {
                    workPlane.UpdateOriginFromSelection(entry.Data, newSelection);
                }
            }

            // 変更後のスナップショット
            newWorkPlane = workPlane.CreateSnapshot();

            // 変更がない場合はnullに戻す
            if (oldWorkPlane.HasValue && newWorkPlane.HasValue &&
                !oldWorkPlane.Value.IsDifferentFrom(newWorkPlane.Value))
            {
                oldWorkPlane = null;
                newWorkPlane = null;
            }
        }

        // Undo記録（WorkPlane連動版）
        _undoController.RecordSelectionChangeWithWorkPlane(
            oldSelection, newSelection,
            oldWorkPlane, newWorkPlane);
    }

    // ================================================================
    // 矩形選択
    // ================================================================
    private void StartBoxSelect(Vector2 startPos)
    {
        _boxSelectStart = startPos;
        _boxSelectEnd = startPos;
        _editState = VertexEditState.BoxSelecting;
    }

    private void FinishBoxSelect(bool shiftHeld, bool ctrlHeld, MeshData meshData, Rect previewRect, Vector3 camPos, Vector3 lookAt)
    {
        var oldSelection = new HashSet<int>(_selectedVertices);

        // 矩形を正規化
        Rect selectRect = new Rect(
            Mathf.Min(_boxSelectStart.x, _boxSelectEnd.x),
            Mathf.Min(_boxSelectStart.y, _boxSelectEnd.y),
            Mathf.Abs(_boxSelectEnd.x - _boxSelectStart.x),
            Mathf.Abs(_boxSelectEnd.y - _boxSelectStart.y)
        );

        // 矩形内の頂点を収集
        var verticesInRect = new HashSet<int>();
        for (int i = 0; i < meshData.VertexCount; i++)
        {
            Vector2 screenPos = WorldToPreviewPos(meshData.Vertices[i].Position, previewRect, camPos, lookAt);
            if (selectRect.Contains(screenPos))
            {
                verticesInRect.Add(i);
            }
        }

        if (ctrlHeld)
        {
            // Ctrl: 矩形内の選択をトグル
            foreach (int idx in verticesInRect)
            {
                if (_selectedVertices.Contains(idx))
                    _selectedVertices.Remove(idx);
                else
                    _selectedVertices.Add(idx);
            }
        }
        else if (shiftHeld)
        {
            // Shift: 矩形内を追加選択
            foreach (int idx in verticesInRect)
            {
                _selectedVertices.Add(idx);
            }
        }
        else
        {
            // 通常: 矩形内で選択置換
            _selectedVertices.Clear();
            foreach (int idx in verticesInRect)
            {
                _selectedVertices.Add(idx);
            }
        }

        // 選択が変更されていたら記録
        if (!oldSelection.SetEquals(_selectedVertices))
        {
            RecordSelectionChange(oldSelection, _selectedVertices);
        }
    }

    // ================================================================
    // 選択ヘルパー
    // ================================================================
    private void SelectAllVertices()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _meshList.Count)
            return;

        var entry = _meshList[_selectedIndex];
        if (entry.Data == null)
            return;

        var oldSelection = new HashSet<int>(_selectedVertices);

        _selectedVertices.Clear();
        for (int i = 0; i < entry.Data.VertexCount; i++)
        {
            _selectedVertices.Add(i);
        }

        if (!oldSelection.SetEquals(_selectedVertices))
        {
            RecordSelectionChange(oldSelection, _selectedVertices);
        }
        Repaint();
    }

    private void InvertSelection()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _meshList.Count)
            return;

        var entry = _meshList[_selectedIndex];
        if (entry.Data == null)
            return;

        var oldSelection = new HashSet<int>(_selectedVertices);

        var newSelection = new HashSet<int>();
        for (int i = 0; i < entry.Data.VertexCount; i++)
        {
            if (!_selectedVertices.Contains(i))
            {
                newSelection.Add(i);
            }
        }
        _selectedVertices = newSelection;

        if (!oldSelection.SetEquals(_selectedVertices))
        {
            RecordSelectionChange(oldSelection, _selectedVertices);
        }
        Repaint();
    }

    private void ClearSelection()
    {
        if (_selectedVertices.Count == 0)
            return;

        var oldSelection = new HashSet<int>(_selectedVertices);
        _selectedVertices.Clear();
        RecordSelectionChange(oldSelection, _selectedVertices);
        Repaint();
    }

    /// <summary>
    /// 選択中の頂点を削除
    /// </summary>
    private void DeleteSelectedVertices()
    {
        if (_selectedVertices.Count == 0) return;
        if (_selectedIndex < 0 || _selectedIndex >= _meshList.Count) return;

        var entry = _meshList[_selectedIndex];
        if (entry.Data == null) return;

        // スナップショット取得（操作前）
        var before = MeshDataSnapshot.Capture(_undoController.MeshContext);

        // 削除処理
        ExecuteDeleteVertices(entry.Data, new HashSet<int>(_selectedVertices));

        // 選択クリア
        _selectedVertices.Clear();
        _undoController.MeshContext.SelectedVertices.Clear();

        // スナップショット取得（操作後）
        var after = MeshDataSnapshot.Capture(_undoController.MeshContext);

        // Undo記録
        _undoController.RecordDeleteVertices(before, after);

        // メッシュ更新
        SyncMeshFromData(entry);
        Repaint();
    }

    /// <summary>
    /// 頂点削除の実行
    /// </summary>
    private void ExecuteDeleteVertices(MeshData meshData, HashSet<int> verticesToDelete)
    {
        int originalCount = meshData.VertexCount;
        if (originalCount == 0) return;

        // 1. 新しいインデックスへのマッピングを作成
        // oldIndex -> newIndex (-1 if deleted)
        var indexMap = new int[originalCount];
        int newIndex = 0;
        for (int i = 0; i < originalCount; i++)
        {
            if (verticesToDelete.Contains(i))
            {
                indexMap[i] = -1; // 削除される
            }
            else
            {
                indexMap[i] = newIndex++;
            }
        }

        // 2. 面を処理（インデックス更新＆無効な面の削除）
        for (int f = meshData.FaceCount - 1; f >= 0; f--)
        {
            var face = meshData.Faces[f];
            var newVertexIndices = new List<int>();
            var newUVIndices = new List<int>();
            var newNormalIndices = new List<int>();

            for (int i = 0; i < face.VertexIndices.Count; i++)
            {
                int oldIdx = face.VertexIndices[i];
                if (oldIdx >= 0 && oldIdx < originalCount)
                {
                    int mappedIdx = indexMap[oldIdx];
                    if (mappedIdx >= 0)
                    {
                        newVertexIndices.Add(mappedIdx);
                        if (i < face.UVIndices.Count) newUVIndices.Add(face.UVIndices[i]);
                        if (i < face.NormalIndices.Count) newNormalIndices.Add(face.NormalIndices[i]);
                    }
                }
            }

            if (newVertexIndices.Count < 3)
            {
                // 頂点数が3未満なら面を削除
                meshData.Faces.RemoveAt(f);
            }
            else
            {
                // 面を更新
                face.VertexIndices = newVertexIndices;
                face.UVIndices = newUVIndices;
                face.NormalIndices = newNormalIndices;
            }
        }

        // 3. 頂点を削除（降順で）
        var sortedIndices = verticesToDelete.OrderByDescending(i => i).ToList();
        foreach (var idx in sortedIndices)
        {
            if (idx >= 0 && idx < meshData.VertexCount)
            {
                meshData.Vertices.RemoveAt(idx);
            }
        }
    }

    /// <summary>
    /// 選択中の頂点を1つにマージ
    /// </summary>
    private void MergeSelectedVertices()
    {
        if (_selectedVertices.Count < 2) return;
        if (_selectedIndex < 0 || _selectedIndex >= _meshList.Count) return;

        var entry = _meshList[_selectedIndex];
        if (entry.Data == null) return;

        // スナップショット取得（操作前）
        var before = MeshDataSnapshot.Capture(_undoController.MeshContext);

        // マージ処理
        int mergedVertex = ExecuteMergeVertices(entry.Data, new HashSet<int>(_selectedVertices));

        // 選択を更新（マージ後の1頂点のみ選択）
        _selectedVertices.Clear();
        if (mergedVertex >= 0)
        {
            _selectedVertices.Add(mergedVertex);
        }
        _undoController.MeshContext.SelectedVertices = new HashSet<int>(_selectedVertices);

        // スナップショット取得（操作後）
        var after = MeshDataSnapshot.Capture(_undoController.MeshContext);

        // Undo記録
        _undoController.RecordTopologyChange(before, after, "Merge Vertices");

        // メッシュ更新
        SyncMeshFromData(entry);
        Repaint();
    }

    /// <summary>
    /// 頂点マージの実行
    /// </summary>
    /// <returns>マージ後の頂点インデックス</returns>
    private int ExecuteMergeVertices(MeshData meshData, HashSet<int> verticesToMerge)
    {
        if (verticesToMerge.Count < 2) return -1;
        int originalCount = meshData.VertexCount;
        if (originalCount == 0) return -1;

        // 1. マージ先の頂点を決定（重心を計算）
        Vector3 centroid = Vector3.zero;
        foreach (int idx in verticesToMerge)
        {
            if (idx >= 0 && idx < originalCount)
            {
                centroid += meshData.Vertices[idx].Position;
            }
        }
        centroid /= verticesToMerge.Count;

        // 最小インデックスの頂点をマージ先とし、位置を重心に更新
        int targetVertex = verticesToMerge.Min();
        meshData.Vertices[targetVertex].Position = centroid;

        // 2. インデックスマッピングを作成
        // targetVertexは残す、他のマージ対象は削除
        var indexMap = new int[originalCount];
        int newIndex = 0;

        for (int i = 0; i < originalCount; i++)
        {
            if (verticesToMerge.Contains(i) && i != targetVertex)
            {
                // マージ対象（targetVertex以外）は削除される
                indexMap[i] = -2; // 後でtargetの新インデックスに更新
            }
            else
            {
                // targetVertexを含む、残る頂点
                indexMap[i] = newIndex++;
            }
        }

        // targetVertexの新インデックスを取得
        int targetNewIndex = indexMap[targetVertex];

        // マージ対象のインデックスをtargetNewIndexに更新
        for (int i = 0; i < originalCount; i++)
        {
            if (indexMap[i] == -2)
            {
                indexMap[i] = targetNewIndex;
            }
        }

        // 3. 面を処理
        for (int f = meshData.FaceCount - 1; f >= 0; f--)
        {
            var face = meshData.Faces[f];
            var newVertexIndices = new List<int>();
            var newUVIndices = new List<int>();
            var newNormalIndices = new List<int>();

            for (int i = 0; i < face.VertexIndices.Count; i++)
            {
                int oldIdx = face.VertexIndices[i];
                if (oldIdx >= 0 && oldIdx < originalCount)
                {
                    int mappedIdx = indexMap[oldIdx];

                    // 連続する同じ頂点を避ける（縮退した辺を防ぐ）
                    if (newVertexIndices.Count == 0 || newVertexIndices[newVertexIndices.Count - 1] != mappedIdx)
                    {
                        newVertexIndices.Add(mappedIdx);
                        if (i < face.UVIndices.Count) newUVIndices.Add(face.UVIndices[i]);
                        if (i < face.NormalIndices.Count) newNormalIndices.Add(face.NormalIndices[i]);
                    }
                }
            }

            // 最初と最後が同じなら最後を除去
            if (newVertexIndices.Count > 1 && newVertexIndices[0] == newVertexIndices[newVertexIndices.Count - 1])
            {
                newVertexIndices.RemoveAt(newVertexIndices.Count - 1);
                if (newUVIndices.Count > 0) newUVIndices.RemoveAt(newUVIndices.Count - 1);
                if (newNormalIndices.Count > 0) newNormalIndices.RemoveAt(newNormalIndices.Count - 1);
            }

            if (newVertexIndices.Count < 3)
            {
                // 頂点数が3未満なら面を削除
                meshData.Faces.RemoveAt(f);
            }
            else
            {
                // 面を更新
                face.VertexIndices = newVertexIndices;
                face.UVIndices = newUVIndices;
                face.NormalIndices = newNormalIndices;
            }
        }

        // 4. 頂点を削除（マージ先以外、降順で）
        var verticesToRemove = verticesToMerge.Where(i => i != targetVertex).OrderByDescending(i => i).ToList();
        foreach (var idx in verticesToRemove)
        {
            if (idx >= 0 && idx < meshData.VertexCount)
            {
                meshData.Vertices.RemoveAt(idx);
            }
        }

        return targetNewIndex;
    }

    /// <summary>
    /// キーボードショートカット処理
    /// </summary>
    private void HandleKeyboardShortcuts(Event e, MeshEntry entry)
    {
        switch (e.keyCode)
        {
            case KeyCode.A:
                // A: 全選択トグル
                if (entry.Data != null)
                {
                    if (_selectedVertices.Count == entry.Data.VertexCount)
                    {
                        ClearSelection();
                    }
                    else
                    {
                        SelectAllVertices();
                    }
                    e.Use();
                }
                break;

            case KeyCode.Escape:
                // Escape: 選択解除
                ClearSelection();
                ResetEditState();
                e.Use();
                break;

            case KeyCode.Delete:
            case KeyCode.Backspace:
                // Delete/Backspace: 選択頂点を削除
                if (_selectedVertices.Count > 0)
                {
                    DeleteSelectedVertices();
                    e.Use();
                }
                break;
        }
    }

    private void HandleCameraRotation(Event e)
    {
        if (e.type == EventType.MouseDown && e.button == 1)
        {
            BeginCameraDrag();
        }

        if (e.type == EventType.MouseDrag && e.button == 1)
        {
            _rotationY += e.delta.x * 0.5f;
            _rotationX += e.delta.y * 0.5f;
            _rotationX = Mathf.Clamp(_rotationX, -89f, 89f);
            e.Use();
            Repaint();
        }

        if (e.type == EventType.MouseUp && e.button == 1)
        {
            EndCameraDrag();
        }
    }

    /// <summary>
    /// スクリーン位置から頂点を検索（MeshDataベース）
    /// </summary>
    private int FindVertexAtScreenPos(Vector2 screenPos, MeshData meshData, Rect previewRect, Vector3 camPos, Vector3 lookAt, float radius)
    {
        if (meshData == null)
            return -1;

        int closestVertex = -1;
        float closestDist = radius;

        for (int i = 0; i < meshData.VertexCount; i++)
        {
            Vector2 vertScreenPos = WorldToPreviewPos(meshData.Vertices[i].Position, previewRect, camPos, lookAt);
            float dist = Vector2.Distance(screenPos, vertScreenPos);

            if (dist < closestDist)
            {
                closestDist = dist;
                closestVertex = i;
            }
        }

        return closestVertex;
    }

    private Vector3 ScreenDeltaToWorldDelta(Vector2 screenDelta, Vector3 camPos, Vector3 lookAt, float camDist, Rect previewRect)
    {
        Vector3 forward = (lookAt - camPos).normalized;
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        Vector3 up = Vector3.Cross(forward, right).normalized;

        float fovRad = _preview.cameraFieldOfView * Mathf.Deg2Rad;
        float worldHeightAtDist = 2f * camDist * Mathf.Tan(fovRad / 2f);
        float pixelToWorld = worldHeightAtDist / previewRect.height;

        Vector3 worldDelta = right * screenDelta.x * pixelToWorld
                           - up * screenDelta.y * pixelToWorld;

        return worldDelta;
    }

    // ================================================================
    // 右ペイン：頂点エディタ（MeshDataベース）
    // ================================================================
    private void DrawVertexEditor()
    {
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(280)))
        {
            EditorGUILayout.LabelField("Vertex Editor", EditorStyles.boldLabel);

            if (_selectedIndex < 0 || _selectedIndex >= _meshList.Count)
            {
                EditorGUILayout.HelpBox("メッシュを選択してください", MessageType.Info);
                return;
            }

            var entry = _meshList[_selectedIndex];
            var meshData = entry.Data;

            if (meshData == null)
            {
                EditorGUILayout.HelpBox("MeshDataが無効です", MessageType.Warning);
                return;
            }

            // メッシュ情報表示
            EditorGUILayout.LabelField($"Vertices: {meshData.VertexCount}");
            EditorGUILayout.LabelField($"Faces: {meshData.FaceCount}");
            EditorGUILayout.LabelField($"Triangles: {meshData.TriangleCount}");

            // 面タイプ内訳
            int triCount = meshData.Faces.Count(f => f.IsTriangle);
            int quadCount = meshData.Faces.Count(f => f.IsQuad);
            int nGonCount = meshData.FaceCount - triCount - quadCount;
            EditorGUILayout.LabelField($"  (Tri:{triCount}, Quad:{quadCount}, NGon:{nGonCount})", EditorStyles.miniLabel);

            EditorGUILayout.Space(5);

            if (GUILayout.Button("Reset to Original"))
            {
                var before = _undoController?.CaptureMeshDataSnapshot();

                ResetMesh(entry);

                if (_undoController != null && before != null)
                {
                    var after = _undoController.CaptureMeshDataSnapshot();
                    _undoController.RecordTopologyChange(before, after, "Reset Mesh");
                }
            }

            EditorGUILayout.Space(5);

            // ================================================================
            // マテリアル登録機能
            // ================================================================
            EditorGUILayout.LabelField("Material", EditorStyles.miniBoldLabel);

            EditorGUI.BeginChangeCheck();
            Material newMat = (Material)EditorGUILayout.ObjectField(
                _registeredMaterial,
                typeof(Material),
                false);
            if (EditorGUI.EndChangeCheck())
            {
                _registeredMaterial = newMat;
            }

            if (_registeredMaterial == null)
            {
                EditorGUILayout.HelpBox("None: デフォルト使用", MessageType.None);
            }

            EditorGUILayout.Space(5);

            // ================================================================
            // 保存機能
            // ================================================================
            EditorGUILayout.LabelField("Save", EditorStyles.miniBoldLabel);

            if (GUILayout.Button("Save Mesh Asset..."))
            {
                SaveMesh(entry);
            }

            if (GUILayout.Button("Save as Prefab..."))
            {
                SaveAsPrefab(entry);
            }

            if (GUILayout.Button("Add to Hierarchy"))
            {
                AddToHierarchy(entry);
            }

            EditorGUILayout.Space(10);

            // 頂点リスト
            _vertexScroll = EditorGUILayout.BeginScrollView(_vertexScroll);

            if (_vertexOffsets == null || _groupOffsets == null)
            {
                InitVertexOffsets();
            }

            if (_vertexOffsets == null || _groupOffsets == null)
            {
                EditorGUILayout.EndScrollView();
                return;
            }

            bool changed = false;

            if (Event.current.type == EventType.MouseDown && !_isSliderDragging)
            {
                BeginSliderDrag();
            }

            // 表示する頂点数を制限（パフォーマンス対策）
            const int MaxDisplayVertices = 20;
            int displayCount = Mathf.Min(meshData.VertexCount, MaxDisplayVertices);

            for (int i = 0; i < displayCount; i++)
            {
                var vertex = meshData.Vertices[i];

                EditorGUILayout.LabelField($"Vertex {i}", EditorStyles.miniBoldLabel);

                using (new EditorGUI.IndentLevelScope())
                {
                    Vector3 offset = i < _vertexOffsets.Length ? _vertexOffsets[i] : Vector3.zero;

                    // X
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("X", GUILayout.Width(15));
                    float newX = EditorGUILayout.Slider(offset.x, -1f, 1f);
                    EditorGUILayout.EndHorizontal();

                    // Y
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Y", GUILayout.Width(15));
                    float newY = EditorGUILayout.Slider(offset.y, -1f, 1f);
                    EditorGUILayout.EndHorizontal();

                    // Z
                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Z", GUILayout.Width(15));
                    float newZ = EditorGUILayout.Slider(offset.z, -1f, 1f);
                    EditorGUILayout.EndHorizontal();

                    Vector3 newOffset = new Vector3(newX, newY, newZ);
                    if (newOffset != offset && i < _vertexOffsets.Length)
                    {
                        _vertexOffsets[i] = newOffset;
                        _groupOffsets[i] = newOffset;

                        // MeshDataの頂点位置を更新
                        vertex.Position = entry.OriginalPositions[i] + newOffset;
                        changed = true;
                    }

                    // UV/Normal情報表示
                    EditorGUILayout.LabelField($"UVs: {vertex.UVs.Count}, Normals: {vertex.Normals.Count}", EditorStyles.miniLabel);
                }

                EditorGUILayout.Space(3);
            }

            // 残りの頂点数を表示
            if (meshData.VertexCount > MaxDisplayVertices)
            {
                int remaining = meshData.VertexCount - MaxDisplayVertices;
                EditorGUILayout.LabelField($"... and {remaining} more vertices", EditorStyles.centeredGreyMiniLabel);
            }

            if (changed)
            {
                SyncMeshFromData(entry);

                if (_undoController != null)
                {
                    _undoController.MeshContext.MeshData = meshData;
                }

                Repaint();
            }

            EditorGUILayout.EndScrollView();
        }
    }

    /// <summary>
    /// スライダードラッグ開始
    /// </summary>
    private void BeginSliderDrag()
    {
        if (_isSliderDragging) return;
        if (_vertexOffsets == null) return;

        _isSliderDragging = true;
        _sliderDragStartOffsets = (Vector3[])_vertexOffsets.Clone();
    }

    /// <summary>
    /// スライダードラッグ終了（Undo記録）
    /// </summary>
    private void EndSliderDrag()
    {
        if (!_isSliderDragging) return;
        _isSliderDragging = false;

        if (_sliderDragStartOffsets == null || _vertexOffsets == null) return;
        if (_selectedIndex < 0 || _selectedIndex >= _meshList.Count) return;

        var entry = _meshList[_selectedIndex];

        // 変更された頂点を検出
        var changedIndices = new List<int>();
        var oldPositions = new List<Vector3>();
        var newPositions = new List<Vector3>();

        for (int i = 0; i < _vertexOffsets.Length && i < _sliderDragStartOffsets.Length; i++)
        {
            if (Vector3.Distance(_vertexOffsets[i], _sliderDragStartOffsets[i]) > 0.0001f)
            {
                changedIndices.Add(i);
                oldPositions.Add(entry.OriginalPositions[i] + _sliderDragStartOffsets[i]);
                newPositions.Add(entry.OriginalPositions[i] + _vertexOffsets[i]);
            }
        }

        if (changedIndices.Count > 0 && _undoController != null)
        {
            var record = new VertexMoveRecord(
                changedIndices.ToArray(),
                oldPositions.ToArray(),
                newPositions.ToArray()
            );
            _undoController.VertexEditStack.Record(record, "Move Vertices");
        }

        _sliderDragStartOffsets = null;
    }

    /// <summary>
    /// メッシュをリセット
    /// </summary>
    private void ResetMesh(MeshEntry entry)
    {
        if (entry.Data == null || entry.OriginalPositions == null)
            return;

        // 元の位置に戻す
        for (int i = 0; i < entry.Data.VertexCount && i < entry.OriginalPositions.Length; i++)
        {
            entry.Data.Vertices[i].Position = entry.OriginalPositions[i];
        }

        SyncMeshFromData(entry);

        if (_vertexOffsets != null)
        {
            for (int i = 0; i < _vertexOffsets.Length; i++)
                _vertexOffsets[i] = Vector3.zero;
        }

        if (_groupOffsets != null)
        {
            for (int i = 0; i < _groupOffsets.Length; i++)
                _groupOffsets[i] = Vector3.zero;
        }

        if (_undoController != null)
        {
            _undoController.MeshContext.MeshData = entry.Data;
        }

        Repaint();
    }

    // ================================================================
    // 保存機能
    // ================================================================

    /// <summary>
    /// メッシュアセットとして保存
    /// </summary>
    private void SaveMesh(MeshEntry entry)
    {
        if (entry == null || entry.Data == null)
            return;

        string defaultName = string.IsNullOrEmpty(entry.Name) ? "Mesh" : entry.Name;
        string path = EditorUtility.SaveFilePanelInProject(
            "Save Mesh",
            defaultName,
            "asset",
            "メッシュを保存する場所を選択してください");

        if (string.IsNullOrEmpty(path))
            return;

        // MeshDataからUnity Meshを生成
        Mesh meshToSave = entry.Data.ToUnityMesh();
        meshToSave.name = System.IO.Path.GetFileNameWithoutExtension(path);

        AssetDatabase.DeleteAsset(path);
        AssetDatabase.CreateAsset(meshToSave, path);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var savedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(path);
        if (savedMesh != null)
        {
            EditorGUIUtility.PingObject(savedMesh);
            Selection.activeObject = savedMesh;
        }

        Debug.Log($"Mesh saved: {path}");
    }

    /// <summary>
    /// プレファブとして保存
    /// </summary>
    private void SaveAsPrefab(MeshEntry entry)
    {
        if (entry == null || entry.Data == null)
            return;

        string defaultName = string.IsNullOrEmpty(entry.Name) ? "MeshObject" : entry.Name;
        string path = EditorUtility.SaveFilePanelInProject(
            "Save as Prefab",
            defaultName,
            "prefab",
            "プレファブを保存する場所を選択してください");

        if (string.IsNullOrEmpty(path))
            return;

        // GameObjectを作成
        GameObject go = new GameObject(entry.Name);
        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();

        // MeshDataからUnity Meshを生成して保存
        Mesh meshCopy = entry.Data.ToUnityMesh();
        meshCopy.name = entry.Name;

        // メッシュを同じディレクトリに保存
        string meshPath = System.IO.Path.ChangeExtension(path, null) + "_Mesh.asset";
        AssetDatabase.DeleteAsset(meshPath);
        AssetDatabase.CreateAsset(meshCopy, meshPath);

        mf.sharedMesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);

        // マテリアル設定
        mr.sharedMaterial = GetMaterialForSave();

        // プレファブとして保存
        AssetDatabase.DeleteAsset(path);
        PrefabUtility.SaveAsPrefabAsset(go, path);

        // 一時オブジェクト削除
        DestroyImmediate(go);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var savedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        if (savedPrefab != null)
        {
            EditorGUIUtility.PingObject(savedPrefab);
            Selection.activeObject = savedPrefab;
        }

        Debug.Log($"Prefab saved: {path}");
    }

    /// <summary>
    /// ヒエラルキーに追加
    /// </summary>
    private void AddToHierarchy(MeshEntry entry)
    {
        if (entry == null || entry.Data == null)
            return;

        // GameObjectを作成
        GameObject go = new GameObject(entry.Name);
        MeshFilter mf = go.AddComponent<MeshFilter>();
        MeshRenderer mr = go.AddComponent<MeshRenderer>();

        // MeshDataからUnity Meshを生成
        Mesh meshCopy = entry.Data.ToUnityMesh();
        meshCopy.name = entry.Name;
        mf.sharedMesh = meshCopy;

        // マテリアル設定
        mr.sharedMaterial = GetMaterialForSave();

        // 選択中のオブジェクトがあれば子として追加
        Transform parent = null;
        if (Selection.gameObjects.Length > 0)
        {
            parent = Selection.gameObjects[0].transform;
        }

        if (parent != null)
        {
            go.transform.SetParent(parent, false);
            go.transform.localPosition = Vector3.zero;
            go.transform.localRotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;
        }

        // Undo登録（Unity標準のUndo）
        Undo.RegisterCreatedObjectUndo(go, $"Create {entry.Name}");

        // 選択
        Selection.activeGameObject = go;
        EditorGUIUtility.PingObject(go);

        Debug.Log($"Added to hierarchy: {go.name}" + (parent != null ? $" (parent: {parent.name})" : ""));
    }

    /// <summary>
    /// 保存用のマテリアルを取得
    /// </summary>
    private Material GetMaterialForSave()
    {
        // 登録されたマテリアルがあればそれを使用
        if (_registeredMaterial != null)
        {
            return _registeredMaterial;
        }

        // なければデフォルトマテリアルを作成/取得
        return GetOrCreateDefaultMaterial();
    }

    /// <summary>
    /// デフォルトマテリアルを取得または作成
    /// </summary>
    private Material GetOrCreateDefaultMaterial()
    {
        // 既存のデフォルトマテリアルを探す
        string[] guids = AssetDatabase.FindAssets("t:Material Default-Material");
        if (guids.Length > 0)
        {
            string path = AssetDatabase.GUIDToAssetPath(guids[0]);
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat != null) return mat;
        }

        // URPのLitシェーダーでマテリアル作成
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null)
            shader = Shader.Find("HDRP/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader != null)
        {
            Material mat = new Material(shader);
            mat.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f, 1f));
            mat.SetColor("_Color", new Color(0.7f, 0.7f, 0.7f, 1f));
            return mat;
        }

        return null;
    }

    // ================================================================
    // プレビュー用マテリアル
    // ================================================================
    private Material _previewMaterial;

    private Material GetPreviewMaterial()
    {
        if (_previewMaterial != null)
            return _previewMaterial;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Simple Lit");
        if (shader == null)
            shader = Shader.Find("HDRP/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");

        if (shader != null)
        {
            _previewMaterial = new Material(shader);
            _previewMaterial.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f, 1f));
            _previewMaterial.SetColor("_Color", new Color(0.7f, 0.7f, 0.7f, 1f));
        }

        return _previewMaterial;
    }

    // ================================================================
    // WorkPlane関連
    // ================================================================

    /// <summary>
    /// WorkPlane UIイベントハンドラ設定
    /// </summary>
    private void SetupWorkPlaneEventHandlers()
    {
        // "From Selection"ボタンクリック時
        WorkPlaneUI.OnFromSelectionClicked += OnWorkPlaneFromSelectionClicked;

        // WorkPlane変更時（Undo記録）
        WorkPlaneUI.OnChanged += OnWorkPlaneChanged;
    }

    /// <summary>
    /// WorkPlane UIイベントハンドラ解除
    /// </summary>
    private void CleanupWorkPlaneEventHandlers()
    {
        WorkPlaneUI.OnFromSelectionClicked -= OnWorkPlaneFromSelectionClicked;
        WorkPlaneUI.OnChanged -= OnWorkPlaneChanged;
    }

    /// <summary>
    /// WorkPlane "From Selection"ボタンクリック
    /// </summary>
    private void OnWorkPlaneFromSelectionClicked()
    {
        if (_undoController == null) return;

        var workPlane = _undoController.WorkPlane;
        if (workPlane == null || workPlane.IsLocked) return;

        if (_selectedIndex < 0 || _selectedIndex >= _meshList.Count) return;
        var entry = _meshList[_selectedIndex];
        if (entry.Data == null || _selectedVertices.Count == 0) return;

        var before = workPlane.CreateSnapshot();

        if (workPlane.UpdateOriginFromSelection(entry.Data, _selectedVertices))
        {
            var after = workPlane.CreateSnapshot();
            if (before.IsDifferentFrom(after))
            {
                _undoController.RecordWorkPlaneChange(before, after, "Set WorkPlane Origin from Selection");
            }
            Repaint();
        }
    }

    /// <summary>
    /// WorkPlane変更時のコールバック（Undo記録）
    /// </summary>
    private void OnWorkPlaneChanged(WorkPlaneSnapshot before, WorkPlaneSnapshot after, string description)
    {
        if (_undoController == null) return;

        _undoController.RecordWorkPlaneChange(before, after, description);
        Repaint();
    }

    /// <summary>
    /// 選択変更時にWorkPlane原点を更新
    /// </summary>
    /// <summary>
    /// WorkPlane UI描画
    /// </summary>
    private void DrawWorkPlaneUI()
    {
        if (_undoController?.WorkPlane == null) return;

        WorkPlaneUI.DrawUI(_undoController.WorkPlane);
    }

    /// <summary>
    /// WorkPlaneギズモ描画（プレビュー内）
    /// </summary>
    private void DrawWorkPlaneGizmo(Rect previewRect, Vector3 camPos, Vector3 lookAt)
    {
        if (_undoController?.WorkPlane == null) return;

        var workPlane = _undoController.WorkPlane;

        // CameraParallelモードの場合、カメラ情報を更新（ロックされていない場合のみ）
        // 注：Undo記録はEndCameraDrag()で行われる
        if (workPlane.Mode == WorkPlaneMode.CameraParallel &&
            !workPlane.IsLocked && !workPlane.LockOrientation)
        {
            workPlane.UpdateFromCamera(camPos, lookAt);
        }

        Vector3 origin = workPlane.Origin;
        Vector3 axisU = workPlane.AxisU;
        Vector3 axisV = workPlane.AxisV;
        Vector3 normal = workPlane.Normal;

        // グリッドサイズ（バウンディングボックスに基づく）
        float gridSize = 0.5f;
        if (_selectedIndex >= 0 && _selectedIndex < _meshList.Count)
        {
            var entry = _meshList[_selectedIndex];
            if (entry.Data != null)
            {
                var bounds = entry.Data.CalculateBounds();
                gridSize = Mathf.Max(bounds.size.magnitude * 0.3f, 0.3f);
            }
        }

        Handles.BeginGUI();

        // グリッド線の色
        Color gridColor;
        if (workPlane.IsLocked)
        {
            gridColor = new Color(1f, 0.5f, 0.2f, 0.15f);  // 全ロック時はオレンジ
        }
        else if (workPlane.LockOrientation)
        {
            gridColor = new Color(1f, 0.8f, 0.4f, 0.15f);  // 軸ロック時は薄いオレンジ
        }
        else
        {
            gridColor = new Color(0.5f, 0.8f, 1f, 0.15f);  // 通常は水色
        }
        Handles.color = gridColor;

        int gridLines = 5;
        float halfSize = gridSize * 0.5f;

        for (int i = -gridLines; i <= gridLines; i++)
        {
            float t = i / (float)gridLines;

            // U方向の線
            Vector3 startU = origin + axisV * (t * gridSize) - axisU * halfSize;
            Vector3 endU = origin + axisV * (t * gridSize) + axisU * halfSize;
            Vector2 startUScreen = WorldToPreviewPos(startU, previewRect, camPos, lookAt);
            Vector2 endUScreen = WorldToPreviewPos(endU, previewRect, camPos, lookAt);
            if (previewRect.Contains(startUScreen) || previewRect.Contains(endUScreen))
            {
                Handles.DrawAAPolyLine(1f,
                    new Vector3(startUScreen.x, startUScreen.y, 0),
                    new Vector3(endUScreen.x, endUScreen.y, 0));
            }

            // V方向の線
            Vector3 startV = origin + axisU * (t * gridSize) - axisV * halfSize;
            Vector3 endV = origin + axisU * (t * gridSize) + axisV * halfSize;
            Vector2 startVScreen = WorldToPreviewPos(startV, previewRect, camPos, lookAt);
            Vector2 endVScreen = WorldToPreviewPos(endV, previewRect, camPos, lookAt);
            if (previewRect.Contains(startVScreen) || previewRect.Contains(endVScreen))
            {
                Handles.DrawAAPolyLine(1f,
                    new Vector3(startVScreen.x, startVScreen.y, 0),
                    new Vector3(endVScreen.x, endVScreen.y, 0));
            }
        }

        // 軸（U: 赤、V: 緑、Normal: 青）
        float axisLen = gridSize * 0.25f;

        // U軸（赤）
        Vector2 originScreen = WorldToPreviewPos(origin, previewRect, camPos, lookAt);
        Vector2 uEndScreen = WorldToPreviewPos(origin + axisU * axisLen, previewRect, camPos, lookAt);
        Handles.color = new Color(1f, 0.3f, 0.3f, 0.8f);
        Handles.DrawAAPolyLine(2f,
            new Vector3(originScreen.x, originScreen.y, 0),
            new Vector3(uEndScreen.x, uEndScreen.y, 0));

        // V軸（緑）
        Vector2 vEndScreen = WorldToPreviewPos(origin + axisV * axisLen, previewRect, camPos, lookAt);
        Handles.color = new Color(0.3f, 1f, 0.3f, 0.8f);
        Handles.DrawAAPolyLine(2f,
            new Vector3(originScreen.x, originScreen.y, 0),
            new Vector3(vEndScreen.x, vEndScreen.y, 0));

        // 法線（青）
        Vector2 nEndScreen = WorldToPreviewPos(origin + normal * axisLen * 0.5f, previewRect, camPos, lookAt);
        Handles.color = new Color(0.3f, 0.5f, 1f, 0.6f);
        Handles.DrawAAPolyLine(2f,
            new Vector3(originScreen.x, originScreen.y, 0),
            new Vector3(nEndScreen.x, nEndScreen.y, 0));

        // 原点マーカー
        if (previewRect.Contains(originScreen))
        {
            Color markerColor = workPlane.IsLocked
                ? new Color(1f, 0.6f, 0.2f, 0.9f)
                : new Color(0.5f, 0.9f, 1f, 0.9f);
            float markerSize = 6f;
            EditorGUI.DrawRect(new Rect(
                originScreen.x - markerSize / 2,
                originScreen.y - markerSize / 2,
                markerSize,
                markerSize), markerColor);
        }

        Handles.EndGUI();
    }
}