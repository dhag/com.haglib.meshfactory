// Assets/Editor/MeshCreators/Profile2DExtrudeWindow.cs
// CSV で読み込んだ 2D 閉曲線群から
// Poly2Tri を用いて押し出しメッシュを生成するサブウインドウ
// 穴あきポリゴンにも対応、手動編集機能付き
//
// 角処理（セグメント数で制御）:
//   0 = 角処理なし
//   1 = ベベル（直線面取り）
//   2以上 = ラウンド（円弧丸め）
//
// 角処理モード:
//   通常（デフォルト）: 面が縮小、角処理は内→外へ広がる
//   Outward: 面は元のサイズを維持、角処理は外→内へ削る

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;
using Poly2Tri;

public class Profile2DExtrudeWindow : EditorWindow
{
    // ================================================================
    // Undo用スナップショット構造体
    // ================================================================
    private struct Profile2DSnapshot : IEquatable<Profile2DSnapshot>
    {
        public string MeshName;
        public string CsvPath;
        public float Scale;
        public Vector2 Offset;
        public bool FlipY;
        public float Thickness;
        public int SegmentsFront, SegmentsBack;
        public float EdgeSizeFront, EdgeSizeBack;
        public bool EdgeInward;
        public LoopData[] Loops;
        public int SelectedLoopIndex;
        public int SelectedPointIndex;
        public float RotationX, RotationY;

        [Serializable]
        public struct LoopData
        {
            public Vector2[] Points;
            public bool IsHole;
        }

        public bool Equals(Profile2DSnapshot o)
        {
            if (MeshName != o.MeshName) return false;
            if (CsvPath != o.CsvPath) return false;
            if (!Mathf.Approximately(Scale, o.Scale)) return false;
            if (Offset != o.Offset) return false;
            if (FlipY != o.FlipY) return false;
            if (!Mathf.Approximately(Thickness, o.Thickness)) return false;
            if (SegmentsFront != o.SegmentsFront || SegmentsBack != o.SegmentsBack) return false;
            if (!Mathf.Approximately(EdgeSizeFront, o.EdgeSizeFront)) return false;
            if (!Mathf.Approximately(EdgeSizeBack, o.EdgeSizeBack)) return false;
            if (EdgeInward != o.EdgeInward) return false;
            if (SelectedLoopIndex != o.SelectedLoopIndex) return false;
            if (SelectedPointIndex != o.SelectedPointIndex) return false;
            if (!Mathf.Approximately(RotationX, o.RotationX)) return false;
            if (!Mathf.Approximately(RotationY, o.RotationY)) return false;

            // ループ比較
            if (Loops == null && o.Loops == null) return true;
            if (Loops == null || o.Loops == null) return false;
            if (Loops.Length != o.Loops.Length) return false;
            for (int i = 0; i < Loops.Length; i++)
            {
                if (Loops[i].IsHole != o.Loops[i].IsHole) return false;
                if (Loops[i].Points == null && o.Loops[i].Points == null) continue;
                if (Loops[i].Points == null || o.Loops[i].Points == null) return false;
                if (Loops[i].Points.Length != o.Loops[i].Points.Length) return false;
                for (int j = 0; j < Loops[i].Points.Length; j++)
                {
                    if (!Mathf.Approximately(Loops[i].Points[j].x, o.Loops[i].Points[j].x) ||
                        !Mathf.Approximately(Loops[i].Points[j].y, o.Loops[i].Points[j].y))
                        return false;
                }
            }
            return true;
        }

        public override bool Equals(object obj) => obj is Profile2DSnapshot s && Equals(s);
        public override int GetHashCode() => MeshName?.GetHashCode() ?? 0;
    }

    // メッシュ生成完了時のコールバック
    private Action<MeshData, string> _onMeshDataCreated;

    // 2D ループ定義
    [Serializable]
    private class Loop
    {
        public List<Vector2> Points = new List<Vector2>();
        public bool IsHole = false;
    }

    // パラメータ
    [SerializeField] private string _meshName = "Profile2DExtrude";
    [SerializeField] private string _csvPath = "";
    [SerializeField] private float _scale = 1.0f;
    [SerializeField] private Vector2 _offset = Vector2.zero;
    [SerializeField] private bool _flipY = false;
    [SerializeField] private float _thickness = 0f;         // 厚み（0なら平面）
    [SerializeField] private int _segmentsFront = 0;        // 表面の角処理セグメント数
    [SerializeField] private int _segmentsBack = 0;         // 裏面の角処理セグメント数
    [SerializeField] private float _edgeSizeFront = 0.1f;   // 表面の角処理サイズ
    [SerializeField] private float _edgeSizeBack = 0.1f;    // 裏面の角処理サイズ
    [SerializeField] private bool _edgeInward = false;      // false=通常（面が縮小）, true=外向き（面は元のサイズ）

    // 読み込んだループ
    [SerializeField] private List<Loop> _loops = new List<Loop>();

    // 2D エディタ
    private int _selectedLoopIndex = 0;
    private int _selectedPointIndex = -1;
    private int _dragPointIndex = -1;
    private bool _isDragging = false;
    private Vector2 _dragStartPos;
    private float _editorZoom = 1f;
    private Vector2 _editorOffset = Vector2.zero;

    // 3D プレビュー
    private PreviewRenderUtility _preview;
    private Mesh _previewMesh;
    private MeshData _previewMeshData;
    private Material _previewMaterial;
    private float _rotationY = 20f;
    private float _rotationX = 30f;

    // スクロール位置
    private Vector2 _leftScrollPos = Vector2.zero;

    // Undo
    private ParameterUndoHelper<Profile2DSnapshot> _undoHelper;

    // SessionState永続化キー
    private const string SESSION_STATE_KEY = "Profile2DExtrudeWindow_State";

    //======================================================================
    // エントリポイント
    //======================================================================

    public static Profile2DExtrudeWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<Profile2DExtrudeWindow>(
            utility: true,
            title: "2D Profile Extrude",
            focus: true);

        window.minSize = new Vector2(800, 600);
        window.maxSize = new Vector2(1200, 900);
        window._onMeshDataCreated = onMeshDataCreated;
        window.InitPreview();

        if (window._loops.Count == 0)
        {
            window.InitializeDefaultLoops();
        }

        window.UpdatePreviewMesh();
        window.Show();
        return window;
    }

    private void OnEnable()
    {
        InitPreview();
        InitUndo();
        LoadState();
        if (_loops.Count == 0)
        {
            InitializeDefaultLoops();
        }
        UpdatePreviewMesh();
    }

    private void OnDisable()
    {
        SaveState();
        CleanupPreview();
        _undoHelper?.Dispose();
    }

    private void InitUndo()
    {
        _undoHelper = new ParameterUndoHelper<Profile2DSnapshot>(
            "Profile2DExtrude",
            "Profile2D Parameters",
            () => CaptureSnapshot(),
            (s) => { ApplySnapshot(s); UpdatePreviewMesh(); },
            () => Repaint()
        );
    }

    private Profile2DSnapshot CaptureSnapshot()
    {
        var loopData = new Profile2DSnapshot.LoopData[_loops.Count];
        for (int i = 0; i < _loops.Count; i++)
        {
            loopData[i] = new Profile2DSnapshot.LoopData
            {
                Points = _loops[i].Points.ToArray(),
                IsHole = _loops[i].IsHole
            };
        }

        return new Profile2DSnapshot
        {
            MeshName = _meshName,
            CsvPath = _csvPath,
            Scale = _scale,
            Offset = _offset,
            FlipY = _flipY,
            Thickness = _thickness,
            SegmentsFront = _segmentsFront,
            SegmentsBack = _segmentsBack,
            EdgeSizeFront = _edgeSizeFront,
            EdgeSizeBack = _edgeSizeBack,
            EdgeInward = _edgeInward,
            Loops = loopData,
            SelectedLoopIndex = _selectedLoopIndex,
            SelectedPointIndex = _selectedPointIndex,
            RotationX = _rotationX,
            RotationY = _rotationY
        };
    }

    private void ApplySnapshot(Profile2DSnapshot s)
    {
        _meshName = s.MeshName;
        _csvPath = s.CsvPath;
        _scale = s.Scale;
        _offset = s.Offset;
        _flipY = s.FlipY;
        _thickness = s.Thickness;
        _segmentsFront = s.SegmentsFront;
        _segmentsBack = s.SegmentsBack;
        _edgeSizeFront = s.EdgeSizeFront;
        _edgeSizeBack = s.EdgeSizeBack;
        _edgeInward = s.EdgeInward;
        _selectedLoopIndex = s.SelectedLoopIndex;
        _selectedPointIndex = s.SelectedPointIndex;
        _rotationX = s.RotationX;
        _rotationY = s.RotationY;

        _loops.Clear();
        if (s.Loops != null)
        {
            foreach (var ld in s.Loops)
            {
                var loop = new Loop
                {
                    IsHole = ld.IsHole,
                    Points = ld.Points != null ? new List<Vector2>(ld.Points) : new List<Vector2>()
                };
                _loops.Add(loop);
            }
        }
    }

    private void SaveState()
    {
        try
        {
            var snapshot = CaptureSnapshot();
            string json = JsonUtility.ToJson(new Profile2DStateWrapper(snapshot));
            SessionState.SetString(SESSION_STATE_KEY, json);
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Profile2DExtrudeWindow: Failed to save state: {e.Message}");
        }
    }

    private void LoadState()
    {
        try
        {
            string json = SessionState.GetString(SESSION_STATE_KEY, "");
            if (!string.IsNullOrEmpty(json))
            {
                var wrapper = JsonUtility.FromJson<Profile2DStateWrapper>(json);
                if (wrapper != null)
                {
                    ApplySnapshot(wrapper.ToSnapshot());
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Profile2DExtrudeWindow: Failed to load state: {e.Message}");
        }
    }

    // JsonUtility用のシリアライズ可能なラッパークラス
    [Serializable]
    private class Profile2DStateWrapper
    {
        public string MeshName;
        public string CsvPath;
        public float Scale;
        public Vector2 Offset;
        public bool FlipY;
        public float Thickness;
        public int SegmentsFront, SegmentsBack;
        public float EdgeSizeFront, EdgeSizeBack;
        public bool EdgeInward;
        public LoopWrapper[] Loops;
        public int SelectedLoopIndex;
        public int SelectedPointIndex;
        public float RotationX, RotationY;

        [Serializable]
        public class LoopWrapper
        {
            public Vector2[] Points;
            public bool IsHole;
        }

        public Profile2DStateWrapper() { }

        public Profile2DStateWrapper(Profile2DSnapshot s)
        {
            MeshName = s.MeshName;
            CsvPath = s.CsvPath;
            Scale = s.Scale;
            Offset = s.Offset;
            FlipY = s.FlipY;
            Thickness = s.Thickness;
            SegmentsFront = s.SegmentsFront;
            SegmentsBack = s.SegmentsBack;
            EdgeSizeFront = s.EdgeSizeFront;
            EdgeSizeBack = s.EdgeSizeBack;
            EdgeInward = s.EdgeInward;
            SelectedLoopIndex = s.SelectedLoopIndex;
            SelectedPointIndex = s.SelectedPointIndex;
            RotationX = s.RotationX;
            RotationY = s.RotationY;

            if (s.Loops != null)
            {
                Loops = new LoopWrapper[s.Loops.Length];
                for (int i = 0; i < s.Loops.Length; i++)
                {
                    Loops[i] = new LoopWrapper
                    {
                        Points = s.Loops[i].Points,
                        IsHole = s.Loops[i].IsHole
                    };
                }
            }
        }

        public Profile2DSnapshot ToSnapshot()
        {
            var loopData = new Profile2DSnapshot.LoopData[Loops?.Length ?? 0];
            if (Loops != null)
            {
                for (int i = 0; i < Loops.Length; i++)
                {
                    loopData[i] = new Profile2DSnapshot.LoopData
                    {
                        Points = Loops[i].Points,
                        IsHole = Loops[i].IsHole
                    };
                }
            }

            return new Profile2DSnapshot
            {
                MeshName = MeshName,
                CsvPath = CsvPath,
                Scale = Scale,
                Offset = Offset,
                FlipY = FlipY,
                Thickness = Thickness,
                SegmentsFront = SegmentsFront,
                SegmentsBack = SegmentsBack,
                EdgeSizeFront = EdgeSizeFront,
                EdgeSizeBack = EdgeSizeBack,
                EdgeInward = EdgeInward,
                Loops = loopData,
                SelectedLoopIndex = SelectedLoopIndex,
                SelectedPointIndex = SelectedPointIndex,
                RotationX = RotationX,
                RotationY = RotationY
            };
        }
    }

    private void InitPreview()
    {
        if (_preview != null) return;

        _preview = new PreviewRenderUtility();
        _preview.cameraFieldOfView = 30f;
        _preview.camera.nearClipPlane = 0.01f;
        _preview.camera.farClipPlane = 100f;

        Shader shader = Shader.Find("Universal Render Pipeline/Lit")
                     ?? Shader.Find("Standard")
                     ?? Shader.Find("Unlit/Color");

        if (shader != null)
        {
            _previewMaterial = new Material(shader);
            _previewMaterial.SetColor("_BaseColor", new Color(0.7f, 0.7f, 0.7f, 1f));
            _previewMaterial.SetColor("_Color", new Color(0.7f, 0.7f, 0.7f, 1f));
        }
    }

    private void CleanupPreview()
    {
        if (_preview != null)
        {
            _preview.Cleanup();
            _preview = null;
        }

        if (_previewMesh != null)
        {
            DestroyImmediate(_previewMesh);
            _previewMesh = null;
        }

        if (_previewMaterial != null)
        {
            DestroyImmediate(_previewMaterial);
            _previewMaterial = null;
        }
    }

    private void InitializeDefaultLoops()
    {
        _loops.Clear();

        // 外側: 四角形
        var outer = new Loop();
        outer.Points.Add(new Vector2(-1f, -1f));
        outer.Points.Add(new Vector2(1f, -1f));
        outer.Points.Add(new Vector2(1f, 1f));
        outer.Points.Add(new Vector2(-1f, 1f));
        outer.IsHole = false;
        _loops.Add(outer);

        // 穴: 小さな四角形
        var hole = new Loop();
        hole.Points.Add(new Vector2(-0.3f, -0.3f));
        hole.Points.Add(new Vector2(0.3f, -0.3f));
        hole.Points.Add(new Vector2(0.3f, 0.3f));
        hole.Points.Add(new Vector2(-0.3f, 0.3f));
        hole.IsHole = true;
        _loops.Add(hole);

        _selectedLoopIndex = 0;
    }

    //======================================================================
    // OnGUI
    //======================================================================

    private void OnGUI()
    {
        _undoHelper?.HandleGUIEvents(Event.current);

        EditorGUILayout.Space(10);

        EditorGUILayout.BeginHorizontal();

        // 左側：パラメータと2Dエディタ
        using (new EditorGUILayout.VerticalScope(GUILayout.Width(350)))
        {
            _leftScrollPos = EditorGUILayout.BeginScrollView(_leftScrollPos, GUILayout.ExpandHeight(true));
            DrawParameters();
            EditorGUILayout.Space(10);
            DrawLoopList();
            EditorGUILayout.Space(10);
            Draw2DEditor();
            EditorGUILayout.EndScrollView();
        }

        EditorGUILayout.Space(10);

        // 右側：3Dプレビュー
        using (new EditorGUILayout.VerticalScope(GUILayout.ExpandWidth(true)))
        {
            Draw3DPreview();
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10);
        DrawButtons();
    }

    private void DrawParameters()
    {
        EditorGUILayout.LabelField("2D Profile Extrude Parameters", EditorStyles.boldLabel);

        _undoHelper?.DrawUndoRedoButtons();
        EditorGUILayout.Space(5);

        EditorGUI.BeginChangeCheck();

        _meshName = EditorGUILayout.TextField("Name", _meshName);

        EditorGUILayout.Space(5);

        // CSV パス
        using (new EditorGUILayout.HorizontalScope())
        {
            EditorGUILayout.PrefixLabel("CSV File");
            string displayPath = string.IsNullOrEmpty(_csvPath) ? "<none>" : Path.GetFileName(_csvPath);
            EditorGUILayout.LabelField(displayPath, EditorStyles.miniLabel);

            if (GUILayout.Button("...", GUILayout.Width(30)))
            {
                var path = EditorUtility.OpenFilePanel(
                    "Select CSV File",
                    Application.dataPath,
                    "csv");

                if (!string.IsNullOrEmpty(path))
                {
                    _csvPath = path;
                    LoadCsv(_csvPath);
                    _undoHelper?.RecordImmediate("Load CSV");
                    UpdatePreviewMesh();
                }
            }
        }

        EditorGUILayout.Space(5);

        _scale = EditorGUILayout.Slider("Scale", _scale, 0.01f, 10f);
        _offset = EditorGUILayout.Vector2Field("Offset", _offset);
        _flipY = EditorGUILayout.Toggle("Flip Y", _flipY);
        _thickness = EditorGUILayout.Slider("Thickness", _thickness, 0f, 2f);

        // 厚みがある場合のみ角処理を表示
        if (_thickness > 0.001f)
        {
            EditorGUILayout.Space(5);
            EditorGUILayout.LabelField("Edge (0=None, 1=Bevel, 2+=Round)", EditorStyles.miniBoldLabel);

            using (new EditorGUI.IndentLevelScope())
            {
                // 表面
                _segmentsFront = EditorGUILayout.IntSlider("Front Segments", _segmentsFront, 0, 16);
                if (_segmentsFront > 0)
                {
                    _edgeSizeFront = EditorGUILayout.Slider("  Size", _edgeSizeFront, 0.01f, 0.5f);
                }

                EditorGUILayout.Space(3);

                // 裏面
                _segmentsBack = EditorGUILayout.IntSlider("Back Segments", _segmentsBack, 0, 16);
                if (_segmentsBack > 0)
                {
                    _edgeSizeBack = EditorGUILayout.Slider("  Size", _edgeSizeBack, 0.01f, 0.5f);
                }

                // 角処理モード
                if (_segmentsFront > 0 || _segmentsBack > 0)
                {
                    EditorGUILayout.Space(3);
                    _edgeInward = EditorGUILayout.Toggle("Outward (Keep Face Size)", _edgeInward);
                }
            }
        }

        if (EditorGUI.EndChangeCheck())
        {
            UpdatePreviewMesh();
        }
    }

    private void DrawLoopList()
    {
        EditorGUILayout.LabelField("Loops", EditorStyles.boldLabel);

        // ループ追加・削除ボタン
        using (new EditorGUILayout.HorizontalScope())
        {
            if (GUILayout.Button("+ Add Loop"))
            {
                var newLoop = new Loop();
                newLoop.Points.Add(new Vector2(0f, 0f));
                newLoop.Points.Add(new Vector2(0.5f, 0f));
                newLoop.Points.Add(new Vector2(0.25f, 0.5f));
                newLoop.IsHole = _loops.Count > 0; // 2番目以降はデフォルトで穴
                _loops.Add(newLoop);
                _selectedLoopIndex = _loops.Count - 1;
                _undoHelper?.RecordImmediate("Add Loop");
                UpdatePreviewMesh();
            }

            GUI.enabled = _loops.Count > 1 && _selectedLoopIndex >= 0;
            if (GUILayout.Button("- Remove"))
            {
                _loops.RemoveAt(_selectedLoopIndex);
                _selectedLoopIndex = Mathf.Clamp(_selectedLoopIndex, 0, _loops.Count - 1);
                _selectedPointIndex = -1;
                _undoHelper?.RecordImmediate("Remove Loop");
                UpdatePreviewMesh();
            }
            GUI.enabled = true;
        }

        EditorGUILayout.Space(5);

        // ループ一覧
        for (int i = 0; i < _loops.Count; i++)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                bool selected = (i == _selectedLoopIndex);
                string label = _loops[i].IsHole ? $"Hole {i}" : $"Outer {i}";
                label += $" ({_loops[i].Points.Count} pts)";

                if (GUILayout.Toggle(selected, label, "Button", GUILayout.Width(150)))
                {
                    if (!selected)
                    {
                        _selectedLoopIndex = i;
                        _selectedPointIndex = -1;
                    }
                }

                EditorGUI.BeginChangeCheck();
                bool isHole = EditorGUILayout.Toggle(_loops[i].IsHole, GUILayout.Width(20));
                EditorGUILayout.LabelField("Hole", GUILayout.Width(35));
                if (EditorGUI.EndChangeCheck())
                {
                    _loops[i].IsHole = isHole;
                    _undoHelper?.RecordImmediate("Toggle Hole");
                    UpdatePreviewMesh();
                }
            }
        }
    }

    private void Draw2DEditor()
    {
        EditorGUILayout.LabelField("2D Editor (Click to select, Drag to move)", EditorStyles.boldLabel);

        // 選択中のループの点の編集
        if (_selectedLoopIndex >= 0 && _selectedLoopIndex < _loops.Count)
        {
            var loop = _loops[_selectedLoopIndex];

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("+ Add Point"))
                {
                    // 選択点の後ろに追加、または末尾に追加
                    Vector2 newPoint;
                    int insertIndex;

                    if (_selectedPointIndex >= 0 && _selectedPointIndex < loop.Points.Count)
                    {
                        int nextIndex = (_selectedPointIndex + 1) % loop.Points.Count;
                        newPoint = (loop.Points[_selectedPointIndex] + loop.Points[nextIndex]) * 0.5f;
                        insertIndex = _selectedPointIndex + 1;
                    }
                    else
                    {
                        newPoint = loop.Points.Count > 0 ? loop.Points[loop.Points.Count - 1] + new Vector2(0.2f, 0) : Vector2.zero;
                        insertIndex = loop.Points.Count;
                    }

                    loop.Points.Insert(insertIndex, newPoint);
                    _selectedPointIndex = insertIndex;
                    _undoHelper?.RecordImmediate("Add Point");
                    UpdatePreviewMesh();
                }

                GUI.enabled = _selectedPointIndex >= 0 && loop.Points.Count > 3;
                if (GUILayout.Button("- Remove Point"))
                {
                    loop.Points.RemoveAt(_selectedPointIndex);
                    _selectedPointIndex = Mathf.Clamp(_selectedPointIndex, 0, loop.Points.Count - 1);
                    _undoHelper?.RecordImmediate("Remove Point");
                    UpdatePreviewMesh();
                }
                GUI.enabled = true;
            }

            // 選択点の座標編集
            if (_selectedPointIndex >= 0 && _selectedPointIndex < loop.Points.Count)
            {
                EditorGUI.BeginChangeCheck();
                Vector2 newPos = EditorGUILayout.Vector2Field($"Point {_selectedPointIndex}", loop.Points[_selectedPointIndex]);
                if (EditorGUI.EndChangeCheck())
                {
                    loop.Points[_selectedPointIndex] = newPos;
                    _undoHelper?.RecordImmediate("Edit Point");
                    UpdatePreviewMesh();
                }
            }
        }

        EditorGUILayout.Space(5);

        // 2Dエディタ領域
        Rect rect = GUILayoutUtility.GetRect(330, 250);
        DrawEditorContent(rect);
        HandleEditorInput(rect);
    }

    private void DrawEditorContent(Rect rect)
    {
        if (Event.current.type != EventType.Repaint)
            return;

        // 背景
        EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.18f, 1f));

        if (_loops.Count == 0)
            return;

        Handles.BeginGUI();

        // グリッド
        DrawEditorGrid(rect);

        // ループを描画
        for (int li = 0; li < _loops.Count; li++)
        {
            var loop = _loops[li];
            if (loop.Points.Count < 2)
                continue;

            // 色: 選択=黄, 穴=赤, 外側=シアン
            Color lineColor;
            if (li == _selectedLoopIndex)
                lineColor = Color.yellow;
            else if (loop.IsHole)
                lineColor = new Color(1f, 0.3f, 0.3f);
            else
                lineColor = Color.cyan;

            Handles.color = lineColor;

            // 線を描画
            for (int i = 0; i < loop.Points.Count; i++)
            {
                Vector2 p0 = WorldToScreen(loop.Points[i], rect);
                Vector2 p1 = WorldToScreen(loop.Points[(i + 1) % loop.Points.Count], rect);
                Handles.DrawLine(new Vector3(p0.x, p0.y), new Vector3(p1.x, p1.y));
            }

            // 点を描画
            for (int i = 0; i < loop.Points.Count; i++)
            {
                Vector2 screenPos = WorldToScreen(loop.Points[i], rect);
                Color pointColor = (li == _selectedLoopIndex && i == _selectedPointIndex) ? Color.white : lineColor;
                Handles.color = pointColor;
                Handles.DrawSolidDisc(new Vector3(screenPos.x, screenPos.y, 0), Vector3.forward, 4f);

                // インデックス
                if (li == _selectedLoopIndex)
                {
                    GUI.Label(new Rect(screenPos.x + 6, screenPos.y - 8, 30, 20), i.ToString(), EditorStyles.miniLabel);
                }
            }
        }

        Handles.EndGUI();
    }

    private void DrawEditorGrid(Rect rect)
    {
        Handles.color = new Color(0.3f, 0.3f, 0.35f, 1f);

        // グリッド線
        for (float x = -5f; x <= 5f; x += 0.5f)
        {
            Vector2 p0 = WorldToScreen(new Vector2(x, -5f), rect);
            Vector2 p1 = WorldToScreen(new Vector2(x, 5f), rect);
            if (p0.x >= rect.xMin && p0.x <= rect.xMax)
                Handles.DrawLine(new Vector3(p0.x, Mathf.Max(p0.y, rect.yMin)), new Vector3(p1.x, Mathf.Min(p1.y, rect.yMax)));
        }

        for (float y = -5f; y <= 5f; y += 0.5f)
        {
            Vector2 p0 = WorldToScreen(new Vector2(-5f, y), rect);
            Vector2 p1 = WorldToScreen(new Vector2(5f, y), rect);
            if (p0.y >= rect.yMin && p0.y <= rect.yMax)
                Handles.DrawLine(new Vector3(Mathf.Max(p0.x, rect.xMin), p0.y), new Vector3(Mathf.Min(p1.x, rect.xMax), p1.y));
        }

        // 軸線
        Handles.color = new Color(0.5f, 0.5f, 0.55f, 1f);
        Vector2 axisX0 = WorldToScreen(new Vector2(-5f, 0f), rect);
        Vector2 axisX1 = WorldToScreen(new Vector2(5f, 0f), rect);
        Handles.DrawLine(new Vector3(axisX0.x, axisX0.y), new Vector3(axisX1.x, axisX1.y));

        Vector2 axisY0 = WorldToScreen(new Vector2(0f, -5f), rect);
        Vector2 axisY1 = WorldToScreen(new Vector2(0f, 5f), rect);
        Handles.DrawLine(new Vector3(axisY0.x, axisY0.y), new Vector3(axisY1.x, axisY1.y));
    }

    private void HandleEditorInput(Rect rect)
    {
        Event e = Event.current;

        if (!rect.Contains(e.mousePosition))
            return;

        Vector2 worldPos = ScreenToWorld(e.mousePosition, rect);

        switch (e.type)
        {
            case EventType.MouseDown:
                if (e.button == 0)
                {
                    // 最も近い点を探す
                    int closestLoop = -1;
                    int closestPoint = -1;
                    float closestDist = 15f;

                    for (int li = 0; li < _loops.Count; li++)
                    {
                        var loop = _loops[li];
                        for (int pi = 0; pi < loop.Points.Count; pi++)
                        {
                            Vector2 screenPt = WorldToScreen(loop.Points[pi], rect);
                            float dist = Vector2.Distance(e.mousePosition, screenPt);
                            if (dist < closestDist)
                            {
                                closestDist = dist;
                                closestLoop = li;
                                closestPoint = pi;
                            }
                        }
                    }

                    if (closestLoop >= 0)
                    {
                        _selectedLoopIndex = closestLoop;
                        _selectedPointIndex = closestPoint;
                        _dragPointIndex = closestPoint;
                        _isDragging = true;
                        _dragStartPos = _loops[closestLoop].Points[closestPoint];
                        e.Use();
                    }
                    else
                    {
                        _selectedPointIndex = -1;
                    }
                    Repaint();
                }
                break;

            case EventType.MouseDrag:
                if (_isDragging && _dragPointIndex >= 0 && e.button == 0)
                {
                    if (_selectedLoopIndex >= 0 && _selectedLoopIndex < _loops.Count)
                    {
                        _loops[_selectedLoopIndex].Points[_dragPointIndex] = worldPos;
                        UpdatePreviewMesh();
                        e.Use();
                    }
                }
                break;

            case EventType.MouseUp:
                if (_isDragging && e.button == 0)
                {
                    _undoHelper?.RecordImmediate("Move Profile Point");
                    _isDragging = false;
                    _dragPointIndex = -1;
                    e.Use();
                }
                break;

            case EventType.ScrollWheel:
                float zoomDelta = -e.delta.y * 0.05f;
                _editorZoom = Mathf.Clamp(_editorZoom + zoomDelta, 0.2f, 5f);
                e.Use();
                Repaint();
                break;
        }
    }

    private Vector2 WorldToScreen(Vector2 world, Rect rect)
    {
        float scale = Mathf.Min(rect.width, rect.height) * 0.4f * _editorZoom;
        Vector2 center = rect.center + _editorOffset;

        return new Vector2(
            center.x + world.x * scale,
            center.y - world.y * scale // Y反転
        );
    }

    private Vector2 ScreenToWorld(Vector2 screen, Rect rect)
    {
        float scale = Mathf.Min(rect.width, rect.height) * 0.4f * _editorZoom;
        Vector2 center = rect.center + _editorOffset;

        return new Vector2(
            (screen.x - center.x) / scale,
            -(screen.y - center.y) / scale // Y反転
        );
    }

    //======================================================================
    // 3D プレビュー
    //======================================================================

    private void Draw3DPreview()
    {
        EditorGUILayout.LabelField("3D Preview", EditorStyles.boldLabel);

        Rect rect = GUILayoutUtility.GetRect(300, 400, GUILayout.ExpandWidth(true), GUILayout.ExpandHeight(true));

        if (_preview == null || _previewMesh == null)
        {
            EditorGUI.DrawRect(rect, new Color(0.15f, 0.15f, 0.18f));
            return;
        }

        Event e = Event.current;
        if (rect.Contains(e.mousePosition))
        {
            if (e.type == EventType.MouseDrag && e.button == 1)
            {
                _rotationY += e.delta.x * 0.5f;
                _rotationX += e.delta.y * 0.5f;
                _rotationX = Mathf.Clamp(_rotationX, -89f, 89f);
                e.Use();
                Repaint();
            }
        }

        if (e.type != EventType.Repaint)
            return;

        _preview.BeginPreview(rect, GUIStyle.none);

        _preview.camera.clearFlags = CameraClearFlags.SolidColor;
        _preview.camera.backgroundColor = new Color(0.15f, 0.15f, 0.18f, 1f);

        Bounds bounds = _previewMesh.bounds;
        float maxExtent = Mathf.Max(bounds.extents.x, bounds.extents.y, bounds.extents.z);
        if (maxExtent < 0.001f) maxExtent = 1f;

        float fovRad = _preview.cameraFieldOfView * Mathf.Deg2Rad * 0.5f;
        float dist = maxExtent / Mathf.Tan(fovRad) * 2.0f;

        Quaternion rot = Quaternion.Euler(_rotationX, _rotationY, 0);
        Vector3 camPos = rot * new Vector3(0, 0, -dist) + bounds.center;

        _preview.camera.transform.position = camPos;
        _preview.camera.transform.LookAt(bounds.center);

        if (_previewMaterial != null)
        {
            _preview.DrawMesh(_previewMesh, Matrix4x4.identity, _previewMaterial, 0);
        }

        _preview.camera.Render();

        Texture result = _preview.EndPreview();
        GUI.DrawTexture(rect, result, ScaleMode.StretchToFill, false);
    }

    private void DrawButtons()
    {
        EditorGUILayout.BeginHorizontal();

        GUI.enabled = !string.IsNullOrEmpty(_csvPath);
        if (GUILayout.Button("Reload CSV", GUILayout.Height(30)))
        {
            LoadCsv(_csvPath);
            _undoHelper?.RecordImmediate("Reload CSV");
            UpdatePreviewMesh();
        }
        GUI.enabled = true;

        if (GUILayout.Button("Reset to Default", GUILayout.Height(30)))
        {
            InitializeDefaultLoops();
            _undoHelper?.RecordImmediate("Reset to Default");
            UpdatePreviewMesh();
        }

        GUI.enabled = _loops != null && _loops.Any(l => l.Points.Count >= 3 && !l.IsHole);
        if (GUILayout.Button("Create", GUILayout.Height(30)))
        {
            CreateMeshAndClose();
        }
        GUI.enabled = true;

        if (GUILayout.Button("Cancel", GUILayout.Height(30)))
        {
            Close();
        }

        EditorGUILayout.EndHorizontal();

        // 情報表示
        if (_previewMeshData != null)
        {
            EditorGUILayout.Space(5);
            int triCount = _previewMeshData.Faces.Count(f => f.IsTriangle);
            int quadCount = _previewMeshData.Faces.Count(f => f.IsQuad);
            EditorGUILayout.HelpBox(
                $"Vertices: {_previewMeshData.VertexCount}, Faces: {_previewMeshData.FaceCount} (Tri:{triCount}, Quad:{quadCount}), Loops: {_loops.Count}",
                MessageType.None);
        }
    }

    //======================================================================
    // CSV 読み込み
    //======================================================================

    private void LoadCsv(string path)
    {
        _loops.Clear();
        _selectedLoopIndex = 0;
        _selectedPointIndex = -1;

        if (string.IsNullOrEmpty(path) || !File.Exists(path))
        {
            Debug.LogWarning($"CSV file not found: {path}");
            return;
        }

        try
        {
            var lines = File.ReadAllLines(path);
            Loop current = null;

            foreach (var raw in lines)
            {
                var line = raw.Trim();
                if (string.IsNullOrEmpty(line))
                {
                    if (current != null && current.Points.Count >= 3)
                    {
                        _loops.Add(current);
                    }
                    current = null;
                    continue;
                }

                if (current == null)
                {
                    current = new Loop();
                }

                var tokens = line.Split(
                    new[] { ',', '\t', ';' },
                    StringSplitOptions.RemoveEmptyEntries);

                if (tokens.Length < 2)
                    continue;

                if (TryParseFloat(tokens[0], out float x) &&
                    TryParseFloat(tokens[1], out float y))
                {
                    current.Points.Add(new Vector2(x, y));
                }
            }

            if (current != null && current.Points.Count >= 3)
            {
                _loops.Add(current);
            }

            // 先頭と末尾が同じなら末尾を削る
            foreach (var loop in _loops)
            {
                if (loop.Points.Count >= 4)
                {
                    Vector2 first = loop.Points[0];
                    Vector2 last = loop.Points[loop.Points.Count - 1];
                    if (Vector2.Distance(first, last) < 1e-6f)
                    {
                        loop.Points.RemoveAt(loop.Points.Count - 1);
                    }
                }
            }

            // 最初のループは外側、2番目以降はデフォルトで穴
            for (int i = 1; i < _loops.Count; i++)
            {
                _loops[i].IsHole = true;
            }

            if (_loops.Count == 0)
            {
                Debug.LogWarning("No valid loops found in CSV.");
                InitializeDefaultLoops();
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"Failed to load CSV: {ex.Message}");
        }
    }

    private bool TryParseFloat(string s, out float value)
    {
        return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value) ||
               float.TryParse(s, NumberStyles.Float, CultureInfo.CurrentCulture, out value);
    }

    //======================================================================
    // Poly2Tri によるメッシュ生成
    //======================================================================

    private MeshData GenerateMeshData()
    {
        if (_loops == null || !_loops.Any(l => l.Points.Count >= 3 && !l.IsHole))
            return null;

        Loop outerLoop = _loops.FirstOrDefault(l => !l.IsHole && l.Points.Count >= 3);
        if (outerLoop == null)
            return null;

        try
        {
            // 変換済み座標を保持
            var transformedLoops = new List<List<Vector2>>();
            var isHoleFlags = new List<bool>();

            foreach (var loop in _loops)
            {
                if (loop.Points.Count < 3) continue;

                var transformed = new List<Vector2>();
                foreach (var pt in loop.Points)
                {
                    float x = pt.x * _scale + _offset.x;
                    float y = (_flipY ? -pt.y : pt.y) * _scale + _offset.y;
                    transformed.Add(new Vector2(x, y));
                }
                transformedLoops.Add(transformed);
                isHoleFlags.Add(loop.IsHole);
            }

            var md = new MeshData(_meshName);

            if (_thickness <= 0.001f)
            {
                // 厚みなし：平面のみ
                GenerateFlatFace(md, transformedLoops, isHoleFlags, 0f, Vector3.back, false);
            }
            else
            {
                float halfThick = _thickness * 0.5f;

                // 角処理適用した座標を計算（オフセットされた輪郭）
                float frontOffset = _segmentsFront > 0 ? _edgeSizeFront : 0f;
                float backOffset = _segmentsBack > 0 ? _edgeSizeBack : 0f;

                var offsetFrontLoops = ApplyEdgeOffset(transformedLoops, isHoleFlags, frontOffset);
                var offsetBackLoops = ApplyEdgeOffset(transformedLoops, isHoleFlags, backOffset);

                if (_edgeInward)
                {
                    // Outwardモード: 表裏両方縮小、角処理は両方とも小→大→小
                    // 表面（Z = -halfThick）- オフセットされた輪郭（縮小）
                    GenerateFlatFace(md, offsetFrontLoops, isHoleFlags, -halfThick, Vector3.back, false);
                    // 裏面（Z = +halfThick）- オフセットされた輪郭（縮小）
                    GenerateFlatFace(md, offsetBackLoops, isHoleFlags, halfThick, Vector3.forward, true);
                    // 側面を生成（表裏両方縮小）
                    GenerateSideFacesOutward(md, transformedLoops, offsetFrontLoops, offsetBackLoops, isHoleFlags, halfThick);
                }
                else
                {
                    // 通常モード（デフォルト）: 表裏両方縮小、凹カーブ
                    // 表面（Z = -halfThick）- オフセットされた輪郭（縮小）
                    GenerateFlatFace(md, offsetFrontLoops, isHoleFlags, -halfThick, Vector3.back, false);
                    // 裏面（Z = +halfThick）- オフセットされた輪郭（縮小）
                    GenerateFlatFace(md, offsetBackLoops, isHoleFlags, halfThick, Vector3.forward, true);
                    // 側面を生成（凹カーブ）
                    GenerateSideFacesNormal(md, transformedLoops, offsetFrontLoops, offsetBackLoops, isHoleFlags, halfThick);
                }
            }

            return md;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Poly2Tri triangulation failed: {ex.Message}");
            return null;
        }
    }

    // 角処理のオフセットを適用（外側は縮小、穴は拡大）
    private List<List<Vector2>> ApplyEdgeOffset(List<List<Vector2>> loops, List<bool> isHoleFlags, float offset)
    {
        if (offset <= 0.001f)
            return loops;

        var result = new List<List<Vector2>>();

        for (int li = 0; li < loops.Count; li++)
        {
            var loop = loops[li];
            bool isHole = isHoleFlags[li];
            var newLoop = new List<Vector2>();

            for (int i = 0; i < loop.Count; i++)
            {
                int prev = (i - 1 + loop.Count) % loop.Count;
                int next = (i + 1) % loop.Count;

                Vector2 p = loop[i];

                // エッジの方向ベクトル
                Vector2 edge1 = (p - loop[prev]).normalized;  // prev→p
                Vector2 edge2 = (loop[next] - p).normalized;  // p→next

                // 各エッジの法線（反時計回りループの場合、右手側が外側）
                Vector2 normal1 = new Vector2(edge1.y, -edge1.x);  // 右90度回転
                Vector2 normal2 = new Vector2(edge2.y, -edge2.x);

                // 法線の平均（内向き）
                Vector2 avgNormal = (normal1 + normal2).normalized;

                // avgNormalが0ベクトルの場合（直線）
                if (avgNormal.sqrMagnitude < 0.001f)
                {
                    avgNormal = normal1;
                }

                // 外側ループは内側へ縮小（負方向）、穴は外側へ拡大（正方向）
                float direction = isHole ? 1f : -1f;
                Vector2 offsetVec = avgNormal * offset * direction;

                newLoop.Add(p + offsetVec);
            }

            result.Add(newLoop);
        }

        return result;
    }

    // 平面を生成
    private void GenerateFlatFace(MeshData md, List<List<Vector2>> loops, List<bool> isHoleFlags,
                                   float z, Vector3 normal, bool flipWinding)
    {
        // 外側ループを探す
        int outerIdx = -1;
        for (int i = 0; i < isHoleFlags.Count; i++)
        {
            if (!isHoleFlags[i])
            {
                outerIdx = i;
                break;
            }
        }
        if (outerIdx < 0) return;

        var outerPoints = new List<PolygonPoint>();
        foreach (var pt in loops[outerIdx])
        {
            outerPoints.Add(new PolygonPoint(pt.x, pt.y));
        }

        var polygon = new Polygon(outerPoints);

        // 穴を追加
        for (int i = 0; i < loops.Count; i++)
        {
            if (!isHoleFlags[i]) continue;

            var holePoints = new List<PolygonPoint>();
            foreach (var pt in loops[i])
            {
                holePoints.Add(new PolygonPoint(pt.x, pt.y));
            }
            polygon.AddHole(new Polygon(holePoints));
        }

        P2T.Triangulate(polygon);

        var vertexMap = new Dictionary<TriangulationPoint, int>();

        foreach (var tri in polygon.Triangles)
        {
            int[] indices = new int[3];
            for (int i = 0; i < 3; i++)
            {
                TriangulationPoint p = tri.Points[i];
                if (!vertexMap.TryGetValue(p, out int idx))
                {
                    idx = md.VertexCount;
                    Vector3 pos = new Vector3((float)p.X, (float)p.Y, z);
                    Vector2 uv = new Vector2((float)p.X, (float)p.Y);
                    md.Vertices.Add(new Vertex(pos, uv, normal));
                    vertexMap[p] = idx;
                }
                indices[i] = idx;
            }

            if (flipWinding)
                md.AddTriangle(indices[0], indices[1], indices[2]);
            else
                md.AddTriangle(indices[0], indices[2], indices[1]);
        }
    }

    // 側面を生成（Outwardモード: 外→内へ削る）
    private void GenerateSideFacesOutward(MeshData md, List<List<Vector2>> baseLoops,
                                          List<List<Vector2>> offsetFrontLoops, List<List<Vector2>> offsetBackLoops,
                                          List<bool> isHoleFlags, float halfThick)
    {
        for (int li = 0; li < baseLoops.Count; li++)
        {
            var baseLoop = baseLoops[li];
            var offsetFront = offsetFrontLoops[li];
            var offsetBack = offsetBackLoops[li];
            bool isHole = isHoleFlags[li];

            int n = baseLoop.Count;

            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;

                // 法線計算（ベースループのエッジから）
                Vector2 edge = baseLoop[next] - baseLoop[i];
                Vector3 sideNormal = new Vector3(edge.y, -edge.x, 0).normalized;

                // 外側ループは外向き、穴は内向き
                if (isHole)
                    sideNormal = -sideNormal;

                // 表面の角処理部分（内→外へ広がる、凸）
                if (_segmentsFront > 0)
                {
                    // offset（面側、Z=-halfThick）からbase（側面側、Z=-halfThick+edgeSize）へ
                    GenerateEdgeFaces(md,
                        offsetFront[i], offsetFront[next],     // 内側（面、小さい）
                        baseLoop[i], baseLoop[next],           // 外側（側面、大きい）
                        -halfThick, -halfThick + _edgeSizeFront,
                        sideNormal, Vector3.back,
                        _segmentsFront, isHole, concave: false, isBackFace: false);
                }

                // メイン側面
                {
                    float frontZ = _segmentsFront > 0 ? -halfThick + _edgeSizeFront : -halfThick;
                    float backZ = _segmentsBack > 0 ? halfThick - _edgeSizeBack : halfThick;

                    Vector2 frontPt0 = _segmentsFront > 0 ? baseLoop[i] : offsetFront[i];
                    Vector2 frontPt1 = _segmentsFront > 0 ? baseLoop[next] : offsetFront[next];
                    Vector2 backPt0 = _segmentsBack > 0 ? baseLoop[i] : offsetBack[i];
                    Vector2 backPt1 = _segmentsBack > 0 ? baseLoop[next] : offsetBack[next];

                    Vector3 v0 = new Vector3(frontPt0.x, frontPt0.y, frontZ);
                    Vector3 v1 = new Vector3(frontPt1.x, frontPt1.y, frontZ);
                    Vector3 v2 = new Vector3(backPt1.x, backPt1.y, backZ);
                    Vector3 v3 = new Vector3(backPt0.x, backPt0.y, backZ);

                    int idx = md.VertexCount;
                    md.Vertices.Add(new Vertex(v0, new Vector2(0, 0), sideNormal));
                    md.Vertices.Add(new Vertex(v1, new Vector2(1, 0), sideNormal));
                    md.Vertices.Add(new Vertex(v2, new Vector2(1, 1), sideNormal));
                    md.Vertices.Add(new Vertex(v3, new Vector2(0, 1), sideNormal));

                    if (isHole)
                        md.AddQuad(idx, idx + 3, idx + 2, idx + 1);
                    else
                        md.AddQuad(idx, idx + 1, idx + 2, idx + 3);
                }

                // 裏面の角処理部分（凹、表面のチェックなしと同じ形状）
                if (_segmentsBack > 0)
                {
                    // base（側面側、Z=halfThick-edgeSize）からoffset（面側、Z=halfThick）へ
                    GenerateEdgeFaces(md,
                        baseLoop[i], baseLoop[next],           // 外側（側面、大きい）
                        offsetBack[i], offsetBack[next],       // 内側（面、小さい）
                        halfThick - _edgeSizeBack, halfThick,
                        sideNormal, Vector3.forward,
                        _segmentsBack, isHole, concave: true, isBackFace: true);
                }
            }
        }
    }

    // 側面を生成（通常モード: 内→外へ広がる）
    private void GenerateSideFacesNormal(MeshData md, List<List<Vector2>> baseLoops,
                                          List<List<Vector2>> offsetFrontLoops, List<List<Vector2>> offsetBackLoops,
                                          List<bool> isHoleFlags, float halfThick)
    {
        for (int li = 0; li < baseLoops.Count; li++)
        {
            var baseLoop = baseLoops[li];
            var offsetFront = offsetFrontLoops[li];
            var offsetBack = offsetBackLoops[li];
            bool isHole = isHoleFlags[li];

            int n = baseLoop.Count;

            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;

                // 法線計算（ベースループのエッジから）
                Vector2 edge = baseLoop[next] - baseLoop[i];
                Vector3 sideNormal = new Vector3(edge.y, -edge.x, 0).normalized;

                // 外側ループは外向き、穴は内向き
                if (isHole)
                    sideNormal = -sideNormal;

                // 表面の角処理部分（内→外へ広がる、凹）
                if (_segmentsFront > 0)
                {
                    // offset（面側、Z=-halfThick）からbase（側面側、Z=-halfThick+edgeSize）へ
                    GenerateEdgeFaces(md,
                        offsetFront[i], offsetFront[next],     // 内側（面）
                        baseLoop[i], baseLoop[next],           // 外側（側面）
                        -halfThick, -halfThick + _edgeSizeFront,
                        sideNormal, Vector3.back,
                        _segmentsFront, isHole, concave: true, isBackFace: false);
                }

                // メイン側面
                {
                    float frontZ = _segmentsFront > 0 ? -halfThick + _edgeSizeFront : -halfThick;
                    float backZ = _segmentsBack > 0 ? halfThick - _edgeSizeBack : halfThick;

                    Vector2 frontPt0 = _segmentsFront > 0 ? baseLoop[i] : offsetFront[i];
                    Vector2 frontPt1 = _segmentsFront > 0 ? baseLoop[next] : offsetFront[next];
                    Vector2 backPt0 = _segmentsBack > 0 ? baseLoop[i] : offsetBack[i];
                    Vector2 backPt1 = _segmentsBack > 0 ? baseLoop[next] : offsetBack[next];

                    Vector3 v0 = new Vector3(frontPt0.x, frontPt0.y, frontZ);
                    Vector3 v1 = new Vector3(frontPt1.x, frontPt1.y, frontZ);
                    Vector3 v2 = new Vector3(backPt1.x, backPt1.y, backZ);
                    Vector3 v3 = new Vector3(backPt0.x, backPt0.y, backZ);

                    int idx = md.VertexCount;
                    md.Vertices.Add(new Vertex(v0, new Vector2(0, 0), sideNormal));
                    md.Vertices.Add(new Vertex(v1, new Vector2(1, 0), sideNormal));
                    md.Vertices.Add(new Vertex(v2, new Vector2(1, 1), sideNormal));
                    md.Vertices.Add(new Vertex(v3, new Vector2(0, 1), sideNormal));

                    if (isHole)
                        md.AddQuad(idx, idx + 3, idx + 2, idx + 1);
                    else
                        md.AddQuad(idx, idx + 1, idx + 2, idx + 3);
                }

                // 裏面の角処理部分（凸、base → offset へ縮小）
                if (_segmentsBack > 0)
                {
                    // base（側面側）からoffset（面側）へ
                    GenerateEdgeFaces(md,
                        baseLoop[i], baseLoop[next],           // 外側（側面、大きい）
                        offsetBack[i], offsetBack[next],       // 内側（面、小さい）
                        halfThick - _edgeSizeBack, halfThick,
                        sideNormal, Vector3.forward,
                        _segmentsBack, isHole, concave: false, isBackFace: true);
                }
            }
        }
    }

    /// <summary>
    /// 角処理の面を生成
    /// セグメント数=1: ベベル（直線）
    /// セグメント数>=2: ラウンド（円弧）
    /// </summary>
    /// <param name="concave">true=凹（内側にへこむ）, false=凸（外側に膨らむ）</param>
    /// <param name="isBackFace">true=裏面の角処理（法線とwindingを反転）</param>
    private void GenerateEdgeFaces(MeshData md,
        Vector2 outer0, Vector2 outer1,  // 外側（面側）の2点
        Vector2 inner0, Vector2 inner1,  // 内側（側面側）の2点
        float outerZ, float innerZ,      // Z座標
        Vector3 sideNormal,              // 側面の法線
        Vector3 faceNormal,              // 面の法線（前面or背面）
        int segments,
        bool isHole,
        bool concave = false,
        bool isBackFace = false)
    {
        if (segments == 1)
        {
            // ベベル: 1枚の斜め面
            Vector3 v0 = new Vector3(outer0.x, outer0.y, outerZ);
            Vector3 v1 = new Vector3(outer1.x, outer1.y, outerZ);
            Vector3 v2 = new Vector3(inner1.x, inner1.y, innerZ);
            Vector3 v3 = new Vector3(inner0.x, inner0.y, innerZ);

            Vector3 bevelNormal = (sideNormal + faceNormal).normalized;

            int idx = md.VertexCount;
            md.Vertices.Add(new Vertex(v0, new Vector2(0, 0), bevelNormal));
            md.Vertices.Add(new Vertex(v1, new Vector2(1, 0), bevelNormal));
            md.Vertices.Add(new Vertex(v2, new Vector2(1, 1), bevelNormal));
            md.Vertices.Add(new Vertex(v3, new Vector2(0, 1), bevelNormal));

            // winding を調整
            bool flipWinding = isHole;
            if (flipWinding)
                md.AddQuad(idx, idx + 3, idx + 2, idx + 1);
            else
                md.AddQuad(idx, idx + 1, idx + 2, idx + 3);
        }
        else
        {
            // ラウンド: 円弧状に分割
            // outer→innerへ、法線がfaceNormal→sideNormalへ回転
            for (int s = 0; s < segments; s++)
            {
                float t0 = (float)s / segments;
                float t1 = (float)(s + 1) / segments;

                // 角度（0→π/2）
                float angle0 = t0 * Mathf.PI * 0.5f;
                float angle1 = t1 * Mathf.PI * 0.5f;

                float xyLerp0, xyLerp1, zLerp0, zLerp1;

                if (concave)
                {
                    // 凹: XYとZの補間を入れ替え
                    xyLerp0 = Mathf.Sin(angle0);
                    xyLerp1 = Mathf.Sin(angle1);
                    zLerp0 = 1f - Mathf.Cos(angle0);
                    zLerp1 = 1f - Mathf.Cos(angle1);
                }
                else
                {
                    // 凸（デフォルト）
                    xyLerp0 = 1f - Mathf.Cos(angle0);
                    xyLerp1 = 1f - Mathf.Cos(angle1);
                    zLerp0 = Mathf.Sin(angle0);
                    zLerp1 = Mathf.Sin(angle1);
                }

                // 2D位置を補間（円弧に沿って）
                Vector2 p0_0 = Vector2.Lerp(outer0, inner0, xyLerp0);
                Vector2 p0_1 = Vector2.Lerp(outer1, inner1, xyLerp0);
                Vector2 p1_0 = Vector2.Lerp(outer0, inner0, xyLerp1);
                Vector2 p1_1 = Vector2.Lerp(outer1, inner1, xyLerp1);

                // Z座標の補間（円弧に沿って）
                float z0 = Mathf.Lerp(outerZ, innerZ, zLerp0);
                float z1 = Mathf.Lerp(outerZ, innerZ, zLerp1);

                // 法線（表面: faceNormal→sideNormal、裏面: sideNormal→faceNormal）
                Vector3 n0, n1;
                if (isBackFace)
                {
                    n0 = Vector3.Slerp(sideNormal, faceNormal, t0).normalized;
                    n1 = Vector3.Slerp(sideNormal, faceNormal, t1).normalized;
                }
                else
                {
                    n0 = Vector3.Slerp(faceNormal, sideNormal, t0).normalized;
                    n1 = Vector3.Slerp(faceNormal, sideNormal, t1).normalized;
                }

                Vector3 v0 = new Vector3(p0_0.x, p0_0.y, z0);
                Vector3 v1 = new Vector3(p0_1.x, p0_1.y, z0);
                Vector3 v2 = new Vector3(p1_1.x, p1_1.y, z1);
                Vector3 v3 = new Vector3(p1_0.x, p1_0.y, z1);

                int idx = md.VertexCount;
                md.Vertices.Add(new Vertex(v0, new Vector2(0, t0), n0));
                md.Vertices.Add(new Vertex(v1, new Vector2(1, t0), n0));
                md.Vertices.Add(new Vertex(v2, new Vector2(1, t1), n1));
                md.Vertices.Add(new Vertex(v3, new Vector2(0, t1), n1));

                // winding を調整
                bool flipWinding = isHole;
                if (flipWinding)
                    md.AddQuad(idx, idx + 3, idx + 2, idx + 1);
                else
                    md.AddQuad(idx, idx + 1, idx + 2, idx + 3);
            }
        }
    }

    private void UpdatePreviewMesh()
    {
        if (_previewMesh != null)
        {
            DestroyImmediate(_previewMesh);
            _previewMesh = null;
        }

        _previewMeshData = GenerateMeshData();
        if (_previewMeshData != null)
        {
            _previewMesh = _previewMeshData.ToUnityMesh();
        }

        Repaint();
    }

    private void CreateMeshAndClose()
    {
        var meshData = GenerateMeshData();
        if (meshData == null)
        {
            EditorUtility.DisplayDialog(
                "2D Profile Extrude",
                "メッシュ生成に失敗しました。入力データを確認してください。",
                "OK");
            return;
        }

        _onMeshDataCreated?.Invoke(meshData, _meshName);
        Close();
    }
}