// Assets/Editor/SimpleMeshFactory.cs
// 階層型Undoシステム統合済みメッシュエディタ
// MeshData（Vertex/Face）ベース対応版
// DefaultMaterials対応版
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.UndoSystem;
using MeshFactory.Data;
using MeshFactory.Transforms;
using MeshFactory.Tools;
using MeshFactory.Serialization;
using MeshFactory.Selection;


public partial class SimpleMeshFactory : EditorWindow
{
    // ================================================================
    // ツールモード
    // ================================================================
    private IEditTool _currentTool;
    private SelectTool _selectTool;
    private MoveTool _moveTool;
    private AddFaceTool _addFaceTool;
    private KnifeTool _knifeTool;
    private LineExtrudeTool _lineExtrudeTool;
    private FlipFaceTool _flipFaceTool;
    private EdgeTopologyTool _edgeTopoTool;
    private AdvancedSelectTool _advancedSelectTool;
    private SculptTool _sculptTool;
    private MergeVerticesTool _mergeTool;
    private EdgeExtrudeTool _extrudeTool;
    private FaceExtrudeTool _faceExtrudeTool;
    private EdgeBevelTool _edgeBevelTool;
    private PivotOffsetTool _pivotOffsetTool;
    //
    private ToolContext _toolContext;

    // ツール設定（シリアライズ対象）
    [SerializeField] private bool _useMagnet = false;
    [SerializeField] private float _magnetRadius = 0.5f;
    [SerializeField] private FalloffType _magnetFalloff = FalloffType.Smooth;

    private void InitializeTools()
    {
        _selectTool = new SelectTool();
        _moveTool = new MoveTool();
        _addFaceTool = new AddFaceTool();
        _knifeTool = new KnifeTool();
        _lineExtrudeTool = new LineExtrudeTool();
        _flipFaceTool = new FlipFaceTool();
        _edgeTopoTool = new EdgeTopologyTool();
        _advancedSelectTool = new AdvancedSelectTool();
        _sculptTool = new SculptTool();
        _mergeTool = new MergeVerticesTool();
        _extrudeTool = new EdgeExtrudeTool();
        _faceExtrudeTool = new FaceExtrudeTool();
        _edgeBevelTool = new EdgeBevelTool();
        _pivotOffsetTool = new PivotOffsetTool();


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
            WorkPlane = _undoController?.WorkPlane,
            CurrentMaterialIndex = 0,
            Materials = null,
            SelectionState = _selectionState,
            TopologyCache = _meshTopology,
            SelectionOps = _selectionOps
        };


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

        // マルチマテリアル対応
        _toolContext.CurrentMaterialIndex = entry?.CurrentMaterialIndex ?? 0;
        _toolContext.Materials = entry?.Materials;

        // 選択システム
        _toolContext.SelectionState = _selectionState;
        _toolContext.TopologyCache = _meshTopology;
        _toolContext.SelectionOps = _selectionOps;

        // UndoコンテキストにもMaterialsを同期
        if (_undoController?.MeshContext != null && entry != null)
        {
            _undoController.MeshContext.Materials = entry.Materials;
            _undoController.MeshContext.CurrentMaterialIndex = entry.CurrentMaterialIndex;
        }

        // デフォルトマテリアルを同期
        if (_undoController?.MeshContext != null)
        {
            _undoController.MeshContext.DefaultMaterials = _defaultMaterials;
            _undoController.MeshContext.DefaultCurrentMaterialIndex = _defaultCurrentMaterialIndex;
            _undoController.MeshContext.AutoSetDefaultMaterials = _autoSetDefaultMaterials;
        }

        // MergeToolのUpdate（選択変更やマージ実行の処理）
        if (_currentTool == _mergeTool)
        {
            _mergeTool.Update(_toolContext);
        }

        // ExtrudeToolの選択更新
        if (_currentTool == _extrudeTool)
        {
            _extrudeTool.OnSelectionChanged(_toolContext);
        }

        // FaceExtrudeToolの選択更新
        if (_currentTool == _faceExtrudeTool)
        {
            _faceExtrudeTool.OnSelectionChanged(_toolContext);
        }

        // EdgeBevelToolの選択更新
        if (_currentTool == _edgeBevelTool)
        {
            _edgeBevelTool.OnSelectionChanged(_toolContext);
        }

        // LineExtrudeToolの選択更新
        if (_currentTool == _lineExtrudeTool)
        {
            _lineExtrudeTool.OnSelectionChanged();
        }
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
            case "EdgeTopo":
                newTool = _edgeTopoTool;
                break;
            case "Sel+":
                newTool = _advancedSelectTool;
                break;
            case "Sculpt":
                newTool = _sculptTool;
                break;
            case "Merge":
                newTool = _mergeTool;
                break;
            case "Extrude":
                newTool = _extrudeTool;
                break;
            case "Push":
                newTool = _faceExtrudeTool;
                break;
            case "Bevel":
                newTool = _edgeBevelTool;
                break;
            case "Line Ext":
                newTool = _lineExtrudeTool;
                break;
            case "Flip":
                newTool = _flipFaceTool;
                break;
            case "Pivot":
                newTool = _pivotOffsetTool;
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
}


