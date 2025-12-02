// Tools/AdvancedSelectTool.cs
// 特殊選択ツール - 接続領域、ベルト、連続エッジ、最短ルート

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.UndoSystem;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 特殊選択ツールのモード
    /// </summary>
    public enum AdvancedSelectMode
    {
        /// <summary>接続領域選択</summary>
        Connected,
        /// <summary>ベルト選択</summary>
        Belt,
        /// <summary>連続エッジ選択</summary>
        EdgeLoop,
        /// <summary>最短ルート選択</summary>
        ShortestPath
    }

    /// <summary>
    /// 特殊選択ツール
    /// </summary>
    public class AdvancedSelectTool : IEditTool
    {
        public string Name => "Sel+";

        // === 設定 ===
        private AdvancedSelectMode _mode = AdvancedSelectMode.Connected;
        private float _edgeLoopThreshold = 0.7f;  // 連続エッジの内積しきい値
        private bool _addToSelection = true;      // true: 追加, false: 削除

        // === 状態 ===
        private int _firstVertex = -1;            // ShortestPath用: 1つ目の頂点
        private int _hoveredVertex = -1;          // ホバー中の頂点
        private int _hoveredFace = -1;            // ホバー中の面
        private (int, int) _hoveredEdge = (-1, -1); // ホバー中の辺

        // === プレビュー ===
        private List<int> _previewVertices = new List<int>();
        private List<int> _previewPath = new List<int>();

        // === 定数 ===
        private const float VERTEX_CLICK_THRESHOLD = 15f;
        private const float EDGE_CLICK_THRESHOLD = 10f;

        // === モード選択用 ===
        private static readonly string[] ModeNames = { "Connected", "Belt", "EdgeLoop", "Shortest" };
        private static readonly AdvancedSelectMode[] ModeValues = { 
            AdvancedSelectMode.Connected, 
            AdvancedSelectMode.Belt, 
            AdvancedSelectMode.EdgeLoop,
            AdvancedSelectMode.ShortestPath
        };

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshData == null) return false;

            switch (_mode)
            {
                case AdvancedSelectMode.Connected:
                    return HandleConnectedClick(ctx, mousePos);

                case AdvancedSelectMode.Belt:
                    return HandleBeltClick(ctx, mousePos);

                case AdvancedSelectMode.EdgeLoop:
                    return HandleEdgeLoopClick(ctx, mousePos);

                case AdvancedSelectMode.ShortestPath:
                    return HandleShortestPathClick(ctx, mousePos);
            }

            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            // MouseMoveイベント（delta == 0）の場合もプレビュー更新
            if (ctx.MeshData == null) return false;

            _previewVertices.Clear();
            _previewPath.Clear();

            switch (_mode)
            {
                case AdvancedSelectMode.Connected:
                    _hoveredVertex = FindNearestVertex(ctx, mousePos);
                    if (_hoveredVertex >= 0)
                    {
                        _previewVertices = GetConnectedVertices(ctx.MeshData, _hoveredVertex);
                    }
                    break;

                case AdvancedSelectMode.Belt:
                    _hoveredEdge = FindNearestEdge(ctx, mousePos);
                    if (_hoveredEdge.Item1 >= 0)
                    {
                        _previewVertices = GetBeltVertices(ctx.MeshData, _hoveredEdge.Item1, _hoveredEdge.Item2);
                    }
                    break;

                case AdvancedSelectMode.EdgeLoop:
                    _hoveredEdge = FindNearestEdge(ctx, mousePos);
                    if (_hoveredEdge.Item1 >= 0)
                    {
                        _previewVertices = GetEdgeLoopVertices(ctx.MeshData, _hoveredEdge.Item1, _hoveredEdge.Item2);
                    }
                    break;

                case AdvancedSelectMode.ShortestPath:
                    _hoveredVertex = FindNearestVertex(ctx, mousePos);
                    if (_firstVertex >= 0 && _hoveredVertex >= 0 && _firstVertex != _hoveredVertex)
                    {
                        _previewPath = GetShortestPath(ctx.MeshData, _firstVertex, _hoveredVertex);
                    }
                    break;
            }

            ctx.Repaint?.Invoke();
            return false;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            return false;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            if (ctx.MeshData == null) return;

            Handles.BeginGUI();

            // プレビュー頂点を描画
            Color previewColor = _addToSelection ? new Color(0, 1, 0, 0.7f) : new Color(1, 0, 0, 0.7f);
            GUI.color = previewColor;

            foreach (int vIdx in _previewVertices)
            {
                if (vIdx < 0 || vIdx >= ctx.MeshData.VertexCount) continue;
                Vector2 sp = ctx.WorldToScreenPos(ctx.MeshData.Vertices[vIdx].Position, 
                    ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                float size = 8f;
                GUI.DrawTexture(new Rect(sp.x - size/2, sp.y - size/2, size, size), EditorGUIUtility.whiteTexture);
            }

            // 最短パスのプレビュー
            if (_mode == AdvancedSelectMode.ShortestPath && _previewPath.Count > 1)
            {
                Handles.color = previewColor;
                for (int i = 0; i < _previewPath.Count - 1; i++)
                {
                    int v1 = _previewPath[i];
                    int v2 = _previewPath[i + 1];
                    if (v1 < 0 || v1 >= ctx.MeshData.VertexCount) continue;
                    if (v2 < 0 || v2 >= ctx.MeshData.VertexCount) continue;

                    Vector2 sp1 = ctx.WorldToScreenPos(ctx.MeshData.Vertices[v1].Position,
                        ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    Vector2 sp2 = ctx.WorldToScreenPos(ctx.MeshData.Vertices[v2].Position,
                        ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    Handles.DrawAAPolyLine(3f, sp1, sp2);
                }

                // パス上の頂点
                foreach (int vIdx in _previewPath)
                {
                    if (vIdx < 0 || vIdx >= ctx.MeshData.VertexCount) continue;
                    Vector2 sp = ctx.WorldToScreenPos(ctx.MeshData.Vertices[vIdx].Position,
                        ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    float size = 8f;
                    GUI.DrawTexture(new Rect(sp.x - size/2, sp.y - size/2, size, size), EditorGUIUtility.whiteTexture);
                }
            }

            // ShortestPath: 1つ目の頂点をハイライト
            if (_mode == AdvancedSelectMode.ShortestPath && _firstVertex >= 0 && _firstVertex < ctx.MeshData.VertexCount)
            {
                GUI.color = Color.yellow;
                Vector2 sp = ctx.WorldToScreenPos(ctx.MeshData.Vertices[_firstVertex].Position,
                    ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                float size = 12f;
                GUI.DrawTexture(new Rect(sp.x - size/2, sp.y - size/2, size, size), EditorGUIUtility.whiteTexture);
            }

            // ホバー中の辺をハイライト
            if ((_mode == AdvancedSelectMode.Belt || _mode == AdvancedSelectMode.EdgeLoop) && 
                _hoveredEdge.Item1 >= 0 && _hoveredEdge.Item2 >= 0)
            {
                Handles.color = Color.cyan;
                Vector2 sp1 = ctx.WorldToScreenPos(ctx.MeshData.Vertices[_hoveredEdge.Item1].Position,
                    ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Vector2 sp2 = ctx.WorldToScreenPos(ctx.MeshData.Vertices[_hoveredEdge.Item2].Position,
                    ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Handles.DrawAAPolyLine(4f, sp1, sp2);
            }

            GUI.color = Color.white;
            Handles.EndGUI();
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Advanced Select Tool", EditorStyles.boldLabel);

            // モード選択
            int currentIndex = Array.IndexOf(ModeValues, _mode);
            int newIndex = GUILayout.SelectionGrid(currentIndex, ModeNames, 2);
            if (newIndex != currentIndex && newIndex >= 0 && newIndex < ModeValues.Length)
            {
                _mode = ModeValues[newIndex];
                Reset();
            }

            EditorGUILayout.Space(3);

            // 追加/削除切り替え
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Mode:", GUILayout.Width(40));
                if (GUILayout.Toggle(_addToSelection, "Add", "Button"))
                    _addToSelection = true;
                if (GUILayout.Toggle(!_addToSelection, "Remove", "Button"))
                    _addToSelection = false;
            }

            // EdgeLoop用しきい値
            if (_mode == AdvancedSelectMode.EdgeLoop)
            {
                EditorGUILayout.Space(3);
                _edgeLoopThreshold = EditorGUILayout.Slider("Threshold", _edgeLoopThreshold, 0f, 1f);
            }

            EditorGUILayout.Space(3);

            // モード説明
            switch (_mode)
            {
                case AdvancedSelectMode.Connected:
                    EditorGUILayout.HelpBox("頂点をクリックして接続領域を選択", MessageType.Info);
                    break;
                case AdvancedSelectMode.Belt:
                    EditorGUILayout.HelpBox("辺をクリックしてベルト状に選択", MessageType.Info);
                    break;
                case AdvancedSelectMode.EdgeLoop:
                    EditorGUILayout.HelpBox("辺をクリックして連続エッジを選択\n（内積がしきい値以上の辺を辿る）", MessageType.Info);
                    break;
                case AdvancedSelectMode.ShortestPath:
                    EditorGUILayout.HelpBox("2つの頂点をクリックして最短経路を選択", MessageType.Info);
                    if (_firstVertex >= 0)
                    {
                        EditorGUILayout.LabelField($"始点: 頂点 {_firstVertex}", EditorStyles.miniLabel);
                        if (GUILayout.Button("リセット", GUILayout.Width(60)))
                        {
                            _firstVertex = -1;
                        }
                    }
                    break;
            }
        }

        public void OnActivate(ToolContext ctx)
        {
            Reset();
        }

        public void OnDeactivate(ToolContext ctx)
        {
            Reset();
        }

        public void Reset()
        {
            _firstVertex = -1;
            _hoveredVertex = -1;
            _hoveredFace = -1;
            _hoveredEdge = (-1, -1);
            _previewVertices.Clear();
            _previewPath.Clear();
        }

        // ================================================================
        // クリックハンドラ
        // ================================================================

        private bool HandleConnectedClick(ToolContext ctx, Vector2 mousePos)
        {
            int vertex = FindNearestVertex(ctx, mousePos);
            if (vertex < 0) return false;

            var connected = GetConnectedVertices(ctx.MeshData, vertex);
            ApplySelection(ctx, connected);
            return true;
        }

        private bool HandleBeltClick(ToolContext ctx, Vector2 mousePos)
        {
            var edge = FindNearestEdge(ctx, mousePos);
            if (edge.Item1 < 0) return false;

            var belt = GetBeltVertices(ctx.MeshData, edge.Item1, edge.Item2);
            ApplySelection(ctx, belt);
            return true;
        }

        private bool HandleEdgeLoopClick(ToolContext ctx, Vector2 mousePos)
        {
            var edge = FindNearestEdge(ctx, mousePos);
            if (edge.Item1 < 0) return false;

            var loop = GetEdgeLoopVertices(ctx.MeshData, edge.Item1, edge.Item2);
            ApplySelection(ctx, loop);
            return true;
        }

        private bool HandleShortestPathClick(ToolContext ctx, Vector2 mousePos)
        {
            int vertex = FindNearestVertex(ctx, mousePos);
            if (vertex < 0) return false;

            if (_firstVertex < 0)
            {
                _firstVertex = vertex;
                ctx.Repaint?.Invoke();
                return true;
            }

            if (_firstVertex == vertex)
            {
                _firstVertex = -1;
                ctx.Repaint?.Invoke();
                return true;
            }

            var path = GetShortestPath(ctx.MeshData, _firstVertex, vertex);
            ApplySelection(ctx, path);
            _firstVertex = -1;
            return true;
        }

        private void ApplySelection(ToolContext ctx, List<int> vertices)
        {
            if (vertices.Count == 0) return;

            var oldSelection = new HashSet<int>(ctx.SelectedVertices);

            if (_addToSelection)
            {
                foreach (int v in vertices)
                    ctx.SelectedVertices.Add(v);
            }
            else
            {
                foreach (int v in vertices)
                    ctx.SelectedVertices.Remove(v);
            }

            ctx.RecordSelectionChange?.Invoke(oldSelection, ctx.SelectedVertices);
            ctx.Repaint?.Invoke();
        }

        // ================================================================
        // 頂点・辺検出
        // ================================================================

        private int FindNearestVertex(ToolContext ctx, Vector2 mousePos)
        {
            float minDist = VERTEX_CLICK_THRESHOLD;
            int result = -1;

            for (int i = 0; i < ctx.MeshData.VertexCount; i++)
            {
                Vector2 sp = ctx.WorldToScreenPos(ctx.MeshData.Vertices[i].Position,
                    ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                float dist = Vector2.Distance(mousePos, sp);
                if (dist < minDist)
                {
                    minDist = dist;
                    result = i;
                }
            }

            return result;
        }

        private (int, int) FindNearestEdge(ToolContext ctx, Vector2 mousePos)
        {
            float minDist = EDGE_CLICK_THRESHOLD;
            (int, int) result = (-1, -1);

            foreach (var face in ctx.MeshData.Faces)
            {
                int n = face.VertexIndices.Count;
                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    Vector2 sp1 = ctx.WorldToScreenPos(ctx.MeshData.Vertices[v1].Position,
                        ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    Vector2 sp2 = ctx.WorldToScreenPos(ctx.MeshData.Vertices[v2].Position,
                        ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                    float dist = DistanceToLineSegment(mousePos, sp1, sp2);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        result = (v1, v2);
                    }
                }
            }

            return result;
        }

        private float DistanceToLineSegment(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
        {
            Vector2 line = lineEnd - lineStart;
            float len = line.magnitude;
            if (len < 0.001f) return Vector2.Distance(point, lineStart);

            float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / (len * len));
            Vector2 projection = lineStart + t * line;
            return Vector2.Distance(point, projection);
        }

        // ================================================================
        // Connected: 接続領域
        // ================================================================

        private List<int> GetConnectedVertices(MeshData meshData, int startVertex)
        {
            var result = new HashSet<int>();
            var queue = new Queue<int>();
            queue.Enqueue(startVertex);
            result.Add(startVertex);

            // 頂点→隣接頂点のマップを構築
            var adjacency = BuildVertexAdjacency(meshData);

            while (queue.Count > 0)
            {
                int current = queue.Dequeue();
                if (adjacency.TryGetValue(current, out var neighbors))
                {
                    foreach (int neighbor in neighbors)
                    {
                        if (result.Add(neighbor))
                        {
                            queue.Enqueue(neighbor);
                        }
                    }
                }
            }

            return result.ToList();
        }

        private Dictionary<int, HashSet<int>> BuildVertexAdjacency(MeshData meshData)
        {
            var adjacency = new Dictionary<int, HashSet<int>>();

            foreach (var face in meshData.Faces)
            {
                int n = face.VertexIndices.Count;
                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];

                    if (!adjacency.ContainsKey(v1)) adjacency[v1] = new HashSet<int>();
                    if (!adjacency.ContainsKey(v2)) adjacency[v2] = new HashSet<int>();

                    adjacency[v1].Add(v2);
                    adjacency[v2].Add(v1);
                }
            }

            return adjacency;
        }

        // ================================================================
        // Belt: ベルト選択
        // ================================================================

        private List<int> GetBeltVertices(MeshData meshData, int v1, int v2)
        {
            var result = new HashSet<int>();
            var edgeToFaces = BuildEdgeToFacesMap(meshData);
            var visitedEdges = new HashSet<(int, int)>();

            // 両方向に探索
            TraverseBelt(meshData, v1, v2, edgeToFaces, visitedEdges, result, 1);
            TraverseBelt(meshData, v1, v2, edgeToFaces, visitedEdges, result, -1);

            return result.ToList();
        }

        private void TraverseBelt(MeshData meshData, int v1, int v2, 
            Dictionary<(int, int), List<int>> edgeToFaces,
            HashSet<(int, int)> visitedEdges, HashSet<int> result, int direction)
        {
            var currentEdge = NormalizeEdge(v1, v2);

            while (true)
            {
                if (visitedEdges.Contains(currentEdge)) break;
                visitedEdges.Add(currentEdge);

                result.Add(currentEdge.Item1);
                result.Add(currentEdge.Item2);

                // この辺を持つ四角形を探す
                if (!edgeToFaces.TryGetValue(currentEdge, out var faces)) break;

                (int, int)? nextEdge = null;

                foreach (int faceIdx in faces)
                {
                    var face = meshData.Faces[faceIdx];
                    if (face.VertexIndices.Count != 4) continue;

                    // 対向辺を見つける
                    var opposite = FindOppositeEdge(face, currentEdge.Item1, currentEdge.Item2);
                    if (opposite.HasValue)
                    {
                        var normalizedOpposite = NormalizeEdge(opposite.Value.Item1, opposite.Value.Item2);
                        if (!visitedEdges.Contains(normalizedOpposite))
                        {
                            nextEdge = normalizedOpposite;
                            break;
                        }
                    }
                }

                if (!nextEdge.HasValue) break;
                currentEdge = nextEdge.Value;
            }
        }

        private (int, int)? FindOppositeEdge(Face face, int v1, int v2)
        {
            var verts = face.VertexIndices;
            int n = verts.Count;
            if (n != 4) return null;

            for (int i = 0; i < n; i++)
            {
                if ((verts[i] == v1 && verts[(i + 1) % n] == v2) ||
                    (verts[i] == v2 && verts[(i + 1) % n] == v1))
                {
                    // 対向辺は +2 の位置
                    int oppStart = (i + 2) % n;
                    int oppEnd = (i + 3) % n;
                    return (verts[oppStart], verts[oppEnd]);
                }
            }

            return null;
        }

        // ================================================================
        // EdgeLoop: 連続エッジ
        // ================================================================

        private List<int> GetEdgeLoopVertices(MeshData meshData, int v1, int v2)
        {
            var result = new HashSet<int>();
            var visitedEdges = new HashSet<(int, int)>();

            // エッジの方向ベクトル
            Vector3 edgeDir = (meshData.Vertices[v2].Position - meshData.Vertices[v1].Position).normalized;

            // 両方向に探索
            TraverseEdgeLoop(meshData, v1, v2, edgeDir, visitedEdges, result);
            TraverseEdgeLoop(meshData, v2, v1, -edgeDir, visitedEdges, result);

            return result.ToList();
        }

        private void TraverseEdgeLoop(MeshData meshData, int fromV, int toV, Vector3 direction,
            HashSet<(int, int)> visitedEdges, HashSet<int> result)
        {
            int current = toV;
            int prev = fromV;
            Vector3 currentDir = direction;

            while (true)
            {
                var edge = NormalizeEdge(prev, current);
                if (visitedEdges.Contains(edge)) break;
                visitedEdges.Add(edge);

                result.Add(prev);
                result.Add(current);

                // currentから出る辺の中で、最も同じ方向に近いものを探す
                var adjacency = BuildVertexAdjacency(meshData);
                if (!adjacency.TryGetValue(current, out var neighbors)) break;

                int bestNext = -1;
                float bestDot = _edgeLoopThreshold;

                foreach (int next in neighbors)
                {
                    if (next == prev) continue;

                    Vector3 nextDir = (meshData.Vertices[next].Position - meshData.Vertices[current].Position).normalized;
                    float dot = Vector3.Dot(currentDir, nextDir);

                    if (dot > bestDot)
                    {
                        bestDot = dot;
                        bestNext = next;
                    }
                }

                if (bestNext < 0) break;

                currentDir = (meshData.Vertices[bestNext].Position - meshData.Vertices[current].Position).normalized;
                prev = current;
                current = bestNext;
            }
        }

        // ================================================================
        // ShortestPath: 最短経路
        // ================================================================

        private List<int> GetShortestPath(MeshData meshData, int start, int end)
        {
            var adjacency = BuildVertexAdjacency(meshData);
            var distances = new Dictionary<int, float>();
            var previous = new Dictionary<int, int>();
            var unvisited = new HashSet<int>();

            // 初期化
            for (int i = 0; i < meshData.VertexCount; i++)
            {
                distances[i] = float.MaxValue;
                unvisited.Add(i);
            }
            distances[start] = 0;

            while (unvisited.Count > 0)
            {
                // 最小距離の頂点を取得
                int current = -1;
                float minDist = float.MaxValue;
                foreach (int v in unvisited)
                {
                    if (distances[v] < minDist)
                    {
                        minDist = distances[v];
                        current = v;
                    }
                }

                if (current < 0 || current == end) break;
                unvisited.Remove(current);

                if (!adjacency.TryGetValue(current, out var neighbors)) continue;

                foreach (int neighbor in neighbors)
                {
                    if (!unvisited.Contains(neighbor)) continue;

                    float edgeLength = Vector3.Distance(
                        meshData.Vertices[current].Position,
                        meshData.Vertices[neighbor].Position);

                    float alt = distances[current] + edgeLength;
                    if (alt < distances[neighbor])
                    {
                        distances[neighbor] = alt;
                        previous[neighbor] = current;
                    }
                }
            }

            // パスを構築
            var path = new List<int>();
            int node = end;
            while (previous.ContainsKey(node))
            {
                path.Add(node);
                node = previous[node];
            }
            path.Add(start);
            path.Reverse();

            return path;
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        private (int, int) NormalizeEdge(int v1, int v2)
        {
            return v1 < v2 ? (v1, v2) : (v2, v1);
        }

        private Dictionary<(int, int), List<int>> BuildEdgeToFacesMap(MeshData meshData)
        {
            var map = new Dictionary<(int, int), List<int>>();

            for (int faceIdx = 0; faceIdx < meshData.FaceCount; faceIdx++)
            {
                var face = meshData.Faces[faceIdx];
                int n = face.VertexIndices.Count;

                for (int i = 0; i < n; i++)
                {
                    int v1 = face.VertexIndices[i];
                    int v2 = face.VertexIndices[(i + 1) % n];
                    var key = NormalizeEdge(v1, v2);

                    if (!map.ContainsKey(key))
                        map[key] = new List<int>();

                    map[key].Add(faceIdx);
                }
            }

            return map;
        }
    }
}
