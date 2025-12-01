// Assets/Editor/MeshCreators/PatchCreatorWindow.cs
// CSV で読み込んだ 2D 閉曲線群から
// Poly2Tri を用いて三角パッチメッシュを生成するサブウインドウ
// 穴あきポリゴンにも対応、手動編集機能付き

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshEditor.Data;
using Poly2Tri;

public class PatchCreatorWindow : EditorWindow
{
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
    [SerializeField] private string _meshName = "CsvPatch";
    [SerializeField] private string _csvPath = "";
    [SerializeField] private float _scale = 1.0f;
    [SerializeField] private Vector2 _offset = Vector2.zero;
    [SerializeField] private bool _flipY = false;
    [SerializeField] private float _thickness = 0f; // 厚み（0なら平面）
    [SerializeField] private float _bevelFront = 0f; // 表面の角落とし
    [SerializeField] private float _bevelBack = 0f;  // 裏面の角落とし

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

    //======================================================================
    // エントリポイント
    //======================================================================

    public static PatchCreatorWindow Open(Action<MeshData, string> onMeshDataCreated)
    {
        var window = GetWindow<PatchCreatorWindow>(
            utility: true,
            title: "CSV 2D Patch (Poly2Tri)",
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
        if (_loops.Count == 0)
        {
            InitializeDefaultLoops();
        }
        UpdatePreviewMesh();
    }

    private void OnDisable()
    {
        CleanupPreview();
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
        EditorGUILayout.LabelField("CSV Patch Parameters", EditorStyles.boldLabel);
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
                    UpdatePreviewMesh();
                }
            }
        }

        EditorGUILayout.Space(5);

        _scale = EditorGUILayout.Slider("Scale", _scale, 0.01f, 10f);
        _offset = EditorGUILayout.Vector2Field("Offset", _offset);
        _flipY = EditorGUILayout.Toggle("Flip Y", _flipY);
        _thickness = EditorGUILayout.Slider("Thickness", _thickness, 0f, 2f);

        if (_thickness > 0.001f)
        {
            using (new EditorGUI.IndentLevelScope())
            {
                _bevelFront = EditorGUILayout.Slider("Bevel Front", _bevelFront, 0f, 0.5f);
                _bevelBack = EditorGUILayout.Slider("Bevel Back", _bevelBack, 0f, 0.5f);
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
                UpdatePreviewMesh();
            }

            GUI.enabled = _loops.Count > 1 && _selectedLoopIndex >= 0;
            if (GUILayout.Button("- Remove"))
            {
                _loops.RemoveAt(_selectedLoopIndex);
                _selectedLoopIndex = Mathf.Clamp(_selectedLoopIndex, 0, _loops.Count - 1);
                _selectedPointIndex = -1;
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
                    UpdatePreviewMesh();
                }

                GUI.enabled = _selectedPointIndex >= 0 && loop.Points.Count > 3;
                if (GUILayout.Button("- Remove Point"))
                {
                    loop.Points.RemoveAt(_selectedPointIndex);
                    _selectedPointIndex = Mathf.Clamp(_selectedPointIndex, 0, loop.Points.Count - 1);
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
            if (e.type == EventType.MouseDrag && e.button == 0)
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
            UpdatePreviewMesh();
        }
        GUI.enabled = true;

        if (GUILayout.Button("Reset to Default", GUILayout.Height(30)))
        {
            InitializeDefaultLoops();
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
            EditorGUILayout.HelpBox(
                $"Vertices: {_previewMeshData.VertexCount}, Triangles: {triCount}, Loops: {_loops.Count}",
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

                // 角落とし適用した座標を計算
                var frontLoops = ApplyBevel(transformedLoops, isHoleFlags, _bevelFront);
                var backLoops = ApplyBevel(transformedLoops, isHoleFlags, _bevelBack);

                // 表面（Z = -halfThick）
                GenerateFlatFace(md, frontLoops, isHoleFlags, -halfThick, Vector3.back, false);

                // 裏面（Z = +halfThick）
                GenerateFlatFace(md, backLoops, isHoleFlags, halfThick, Vector3.forward, true);

                // 側面を生成
                GenerateSideFaces(md, transformedLoops, frontLoops, backLoops, isHoleFlags, halfThick);
            }

            return md;
        }
        catch (Exception ex)
        {
            Debug.LogError($"Poly2Tri triangulation failed: {ex.Message}");
            return null;
        }
    }

    // 角落としを適用（外側は縮小、穴は拡大）
    private List<List<Vector2>> ApplyBevel(List<List<Vector2>> loops, List<bool> isHoleFlags, float bevel)
    {
        if (bevel <= 0.001f)
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
                Vector2 toPrev = (loop[prev] - p).normalized;
                Vector2 toNext = (loop[next] - p).normalized;

                // 内向き法線（2つのエッジの角の二等分線方向）
                Vector2 bisector = (toPrev + toNext).normalized;

                // bisectorが0ベクトルの場合（直線）
                if (bisector.sqrMagnitude < 0.001f)
                {
                    // エッジに垂直な方向
                    Vector2 edge = toNext;
                    bisector = new Vector2(-edge.y, edge.x);
                }

                // 外側ループは内側へ、穴は外側へ
                float direction = isHole ? -1f : 1f;
                Vector2 offset = bisector * bevel * direction;

                newLoop.Add(p + offset);
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

    // 側面を生成
    private void GenerateSideFaces(MeshData md, List<List<Vector2>> baseLoops,
                                    List<List<Vector2>> frontLoops, List<List<Vector2>> backLoops,
                                    List<bool> isHoleFlags, float halfThick)
    {
        for (int li = 0; li < baseLoops.Count; li++)
        {
            var baseLoop = baseLoops[li];
            var frontLoop = frontLoops[li];
            var backLoop = backLoops[li];
            bool isHole = isHoleFlags[li];

            int n = baseLoop.Count;

            for (int i = 0; i < n; i++)
            {
                int next = (i + 1) % n;

                // 角落としがある場合は3段階
                // 表面エッジ → ベース → 裏面エッジ
                bool hasFrontBevel = _bevelFront > 0.001f;
                bool hasBackBevel = _bevelBack > 0.001f;

                // 法線計算（ベースループのエッジから）
                Vector2 edge = baseLoop[next] - baseLoop[i];
                Vector3 normal = new Vector3(edge.y, -edge.x, 0).normalized;

                // 外側ループは外向き、穴は内向き
                if (isHole)
                    normal = -normal;

                // 表面の角落とし部分
                if (hasFrontBevel)
                {
                    Vector3 v0 = new Vector3(frontLoop[i].x, frontLoop[i].y, -halfThick);
                    Vector3 v1 = new Vector3(frontLoop[next].x, frontLoop[next].y, -halfThick);
                    Vector3 v2 = new Vector3(baseLoop[next].x, baseLoop[next].y, -halfThick);
                    Vector3 v3 = new Vector3(baseLoop[i].x, baseLoop[i].y, -halfThick);

                    // 斜め法線
                    Vector3 bevelNormal = (normal + Vector3.back).normalized;

                    int idx = md.VertexCount;
                    md.Vertices.Add(new Vertex(v0, new Vector2(0, 0), bevelNormal));
                    md.Vertices.Add(new Vertex(v1, new Vector2(1, 0), bevelNormal));
                    md.Vertices.Add(new Vertex(v2, new Vector2(1, 1), bevelNormal));
                    md.Vertices.Add(new Vertex(v3, new Vector2(0, 1), bevelNormal));

                    if (isHole)
                        md.AddQuad(idx, idx + 3, idx + 2, idx + 1);
                    else
                        md.AddQuad(idx, idx + 1, idx + 2, idx + 3);
                }

                // メイン側面
                {
                    float frontZ = -halfThick;
                    float backZ = halfThick;

                    Vector2 frontPt0 = hasFrontBevel ? baseLoop[i] : frontLoop[i];
                    Vector2 frontPt1 = hasFrontBevel ? baseLoop[next] : frontLoop[next];
                    Vector2 backPt0 = hasBackBevel ? baseLoop[i] : backLoop[i];
                    Vector2 backPt1 = hasBackBevel ? baseLoop[next] : backLoop[next];

                    Vector3 v0 = new Vector3(frontPt0.x, frontPt0.y, frontZ);
                    Vector3 v1 = new Vector3(frontPt1.x, frontPt1.y, frontZ);
                    Vector3 v2 = new Vector3(backPt1.x, backPt1.y, backZ);
                    Vector3 v3 = new Vector3(backPt0.x, backPt0.y, backZ);

                    int idx = md.VertexCount;
                    md.Vertices.Add(new Vertex(v0, new Vector2(0, 0), normal));
                    md.Vertices.Add(new Vertex(v1, new Vector2(1, 0), normal));
                    md.Vertices.Add(new Vertex(v2, new Vector2(1, 1), normal));
                    md.Vertices.Add(new Vertex(v3, new Vector2(0, 1), normal));

                    if (isHole)
                        md.AddQuad(idx, idx + 3, idx + 2, idx + 1);
                    else
                        md.AddQuad(idx, idx + 1, idx + 2, idx + 3);
                }

                // 裏面の角落とし部分
                if (hasBackBevel)
                {
                    Vector3 v0 = new Vector3(baseLoop[i].x, baseLoop[i].y, halfThick);
                    Vector3 v1 = new Vector3(baseLoop[next].x, baseLoop[next].y, halfThick);
                    Vector3 v2 = new Vector3(backLoop[next].x, backLoop[next].y, halfThick);
                    Vector3 v3 = new Vector3(backLoop[i].x, backLoop[i].y, halfThick);

                    // 斜め法線
                    Vector3 bevelNormal = (normal + Vector3.forward).normalized;

                    int idx = md.VertexCount;
                    md.Vertices.Add(new Vertex(v0, new Vector2(0, 0), bevelNormal));
                    md.Vertices.Add(new Vertex(v1, new Vector2(1, 0), bevelNormal));
                    md.Vertices.Add(new Vertex(v2, new Vector2(1, 1), bevelNormal));
                    md.Vertices.Add(new Vertex(v3, new Vector2(0, 1), bevelNormal));

                    if (isHole)
                        md.AddQuad(idx, idx + 3, idx + 2, idx + 1);
                    else
                        md.AddQuad(idx, idx + 1, idx + 2, idx + 3);
                }
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
                "CSV Patch",
                "メッシュ生成に失敗しました。入力データを確認してください。",
                "OK");
            return;
        }

        _onMeshDataCreated?.Invoke(meshData, _meshName);
        Close();
    }
}