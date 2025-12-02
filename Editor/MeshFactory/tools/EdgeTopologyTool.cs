// Tools/EdgeTopologyTool.cs
// 辺トポロジツール - 辺の編集（入れ替え、分割、結合）

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
    /// 辺トポロジツールのモード
    /// </summary>
    public enum EdgeTopoMode
    {
        /// <summary>辺の入れ替え（2三角形の対角線切り替え）</summary>
        Flip,
        /// <summary>四角形を対角線で分割</summary>
        Split,
        /// <summary>辺の消去（2面を結合）</summary>
        Dissolve
    }

    /// <summary>
    /// 辺トポロジツール - 辺のトポロジ編集
    /// </summary>
    public class EdgeTopologyTool : IEditTool
    {
        public string Name => "EdgeTopo";

        // === 設定 ===
        private EdgeTopoMode _mode = EdgeTopoMode.Flip;

        // === ドラッグ状態 ===
        private bool _isDragging;
        private Vector2 _startScreenPos;
        private Vector2 _currentScreenPos;

        // === 検出結果 ===
        private EdgeInfo _hoveredEdge;
        private int _hoveredFaceIndex = -1;
        private int _startVertexIndex = -1;  // Split用
        private int _endVertexIndex = -1;    // Split用

        // === 定数 ===
        private const float EDGE_CLICK_THRESHOLD = 10f;  // 辺クリック判定の距離（ピクセル）
        private const float VERTEX_CLICK_THRESHOLD = 15f; // 頂点クリック判定の距離（ピクセル）

        // === モード選択用 ===
        private static readonly string[] ModeNames = { "Flip", "Split", "Dissolve" };
        private static readonly EdgeTopoMode[] ModeValues = { EdgeTopoMode.Flip, EdgeTopoMode.Split, EdgeTopoMode.Dissolve };

        /// <summary>
        /// 辺の情報
        /// </summary>
        private struct EdgeInfo
        {
            public int FaceIndex1;      // 辺を持つ面1のインデックス
            public int FaceIndex2;      // 辺を持つ面2のインデックス（-1 = 境界辺）
            public int VertexIndex1;    // 辺の頂点1（グローバルインデックス）
            public int VertexIndex2;    // 辺の頂点2（グローバルインデックス）
            public Vector2 ScreenPos1;  // スクリーン座標1
            public Vector2 ScreenPos2;  // スクリーン座標2
            public bool IsShared => FaceIndex2 >= 0;
        }

        // ================================================================
        // IEditTool 実装
        // ================================================================

        public bool OnMouseDown(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshData == null) return false;

            _isDragging = true;
            _startScreenPos = mousePos;
            _currentScreenPos = mousePos;

            switch (_mode)
            {
                case EdgeTopoMode.Flip:
                case EdgeTopoMode.Dissolve:
                    // 辺をクリックで即時実行
                    var edge = FindNearestEdge(ctx, mousePos);
                    if (edge.HasValue && edge.Value.IsShared)
                    {
                        if (_mode == EdgeTopoMode.Flip)
                            ExecuteFlip(ctx, edge.Value);
                        else
                            ExecuteDissolve(ctx, edge.Value);
                    }
                    _isDragging = false;
                    return true;

                case EdgeTopoMode.Split:
                    // 四角形面の頂点をクリック
                    _startVertexIndex = FindNearestVertexInQuad(ctx, mousePos, out _hoveredFaceIndex);
                    if (_startVertexIndex < 0)
                    {
                        _isDragging = false;
                    }
                    return true;
            }

            return false;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            if (!_isDragging) return false;

            _currentScreenPos = mousePos;

            if (_mode == EdgeTopoMode.Split && _startVertexIndex >= 0)
            {
                // 対角頂点を検出
                _endVertexIndex = FindOppositeVertex(ctx, _hoveredFaceIndex, _startVertexIndex, mousePos);
            }

            ctx.Repaint?.Invoke();
            return true;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            if (!_isDragging) return false;

            _isDragging = false;

            if (_mode == EdgeTopoMode.Split && _startVertexIndex >= 0 && _endVertexIndex >= 0)
            {
                ExecuteSplit(ctx, _hoveredFaceIndex, _startVertexIndex, _endVertexIndex);
            }

            _startVertexIndex = -1;
            _endVertexIndex = -1;
            _hoveredFaceIndex = -1;

            ctx.Repaint?.Invoke();
            return true;
        }

        public void OnMouseMove(ToolContext ctx, Vector2 mousePos)
        {
            if (ctx.MeshData == null) return;

            switch (_mode)
            {
                case EdgeTopoMode.Flip:
                case EdgeTopoMode.Dissolve:
                    var edge = FindNearestEdge(ctx, mousePos);
                    _hoveredEdge = edge ?? default;
                    break;

                case EdgeTopoMode.Split:
                    if (!_isDragging)
                    {
                        // 四角形面をホバー検出
                        FindNearestVertexInQuad(ctx, mousePos, out _hoveredFaceIndex);
                    }
                    break;
            }

            ctx.Repaint?.Invoke();
        }

        public void DrawOverlay(ToolContext ctx, Rect previewRect)
        {
            if (ctx.MeshData == null) return;

            switch (_mode)
            {
                case EdgeTopoMode.Flip:
                case EdgeTopoMode.Dissolve:
                    DrawEdgeHighlight(ctx);
                    break;

                case EdgeTopoMode.Split:
                    DrawSplitPreview(ctx);
                    break;
            }
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Edge Topology Tool", EditorStyles.boldLabel);

            // モード選択
            int currentIndex = Array.IndexOf(ModeValues, _mode);
            int newIndex = GUILayout.SelectionGrid(currentIndex, ModeNames, 3);
            if (newIndex != currentIndex && newIndex >= 0 && newIndex < ModeValues.Length)
            {
                _mode = ModeValues[newIndex];
            }

            EditorGUILayout.Space(3);

            // モード説明
            switch (_mode)
            {
                case EdgeTopoMode.Flip:
                    EditorGUILayout.HelpBox("共有辺をクリックして対角線を入れ替え\n（2つの三角形が必要）", MessageType.Info);
                    break;
                case EdgeTopoMode.Split:
                    EditorGUILayout.HelpBox("四角形の対角頂点をドラッグして分割", MessageType.Info);
                    break;
                case EdgeTopoMode.Dissolve:
                    EditorGUILayout.HelpBox("共有辺をクリックして2つの面を結合", MessageType.Info);
                    break;
            }
        }

        public void Reset()
        {
            _isDragging = false;
            _startVertexIndex = -1;
            _endVertexIndex = -1;
            _hoveredFaceIndex = -1;
            _hoveredEdge = default;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            // DrawOverlayで描画しているため、ここでは何もしない
        }

        public void OnActivate(ToolContext ctx)
        {
            Reset();
        }

        public void OnDeactivate(ToolContext ctx)
        {
            Reset();
        }

        // ================================================================
        // 辺検出
        // ================================================================

        /// <summary>
        /// マウス位置に最も近い辺を検索
        /// </summary>
        private EdgeInfo? FindNearestEdge(ToolContext ctx, Vector2 mousePos)
        {
            float minDist = EDGE_CLICK_THRESHOLD;
            EdgeInfo? result = null;

            // 全ての辺を走査
            var edgeToFaces = BuildEdgeToFacesMap(ctx.MeshData);

            foreach (var kvp in edgeToFaces)
            {
                var (v1, v2) = kvp.Key;
                var faces = kvp.Value;

                // スクリーン座標を計算
                Vector3 worldPos1 = ctx.MeshData.Vertices[v1].Position;
                Vector3 worldPos2 = ctx.MeshData.Vertices[v2].Position;
                Vector2 screenPos1 = ctx.WorldToScreenPos(worldPos1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Vector2 screenPos2 = ctx.WorldToScreenPos(worldPos2, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                // マウスと辺の距離
                float dist = DistanceToLineSegment(mousePos, screenPos1, screenPos2);

                if (dist < minDist)
                {
                    minDist = dist;
                    result = new EdgeInfo
                    {
                        FaceIndex1 = faces[0],
                        FaceIndex2 = faces.Count > 1 ? faces[1] : -1,
                        VertexIndex1 = v1,
                        VertexIndex2 = v2,
                        ScreenPos1 = screenPos1,
                        ScreenPos2 = screenPos2
                    };
                }
            }

            return result;
        }

        /// <summary>
        /// 辺→面のマップを構築
        /// </summary>
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

                    // 正規化（小さい方を先に）
                    var key = v1 < v2 ? (v1, v2) : (v2, v1);

                    if (!map.ContainsKey(key))
                        map[key] = new List<int>();

                    map[key].Add(faceIdx);
                }
            }

            return map;
        }

        /// <summary>
        /// 点と線分の距離
        /// </summary>
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
        // Split用: 四角形頂点検出
        // ================================================================

        /// <summary>
        /// 四角形面内で最も近い頂点を検索
        /// </summary>
        private int FindNearestVertexInQuad(ToolContext ctx, Vector2 mousePos, out int faceIndex)
        {
            faceIndex = -1;
            float minDist = VERTEX_CLICK_THRESHOLD;
            int resultVertex = -1;

            for (int f = 0; f < ctx.MeshData.FaceCount; f++)
            {
                var face = ctx.MeshData.Faces[f];
                if (face.VertexIndices.Count != 4) continue; // 四角形のみ

                for (int i = 0; i < 4; i++)
                {
                    int vIdx = face.VertexIndices[i];
                    Vector3 worldPos = ctx.MeshData.Vertices[vIdx].Position;
                    Vector2 screenPos = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                    float dist = Vector2.Distance(mousePos, screenPos);
                    if (dist < minDist)
                    {
                        minDist = dist;
                        resultVertex = vIdx;
                        faceIndex = f;
                    }
                }
            }

            return resultVertex;
        }

        /// <summary>
        /// 対角頂点を検索
        /// </summary>
        private int FindOppositeVertex(ToolContext ctx, int faceIndex, int startVertex, Vector2 mousePos)
        {
            if (faceIndex < 0 || faceIndex >= ctx.MeshData.FaceCount) return -1;

            var face = ctx.MeshData.Faces[faceIndex];
            if (face.VertexIndices.Count != 4) return -1;

            // 開始頂点の位置を探す
            int startIdx = face.VertexIndices.IndexOf(startVertex);
            if (startIdx < 0) return -1;

            // 対角は+2の位置
            int oppositeIdx = (startIdx + 2) % 4;
            int oppositeVertex = face.VertexIndices[oppositeIdx];

            // マウスが対角頂点の近くにあるか確認
            Vector3 worldPos = ctx.MeshData.Vertices[oppositeVertex].Position;
            Vector2 screenPos = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

            if (Vector2.Distance(mousePos, screenPos) < VERTEX_CLICK_THRESHOLD * 2)
            {
                return oppositeVertex;
            }

            return -1;
        }

        // ================================================================
        // 操作実行
        // ================================================================

        /// <summary>
        /// Edge Flip実行
        /// </summary>
        private void ExecuteFlip(ToolContext ctx, EdgeInfo edge)
        {
            if (!edge.IsShared) return;

            var face1 = ctx.MeshData.Faces[edge.FaceIndex1];
            var face2 = ctx.MeshData.Faces[edge.FaceIndex2];

            // 両方とも三角形でなければ不可
            if (face1.VertexIndices.Count != 3 || face2.VertexIndices.Count != 3)
            {
                Debug.LogWarning("Edge Flip requires two triangles");
                return;
            }

            // スナップショット（操作前）
            var before = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);

            // 共有辺の頂点
            int v1 = edge.VertexIndex1;
            int v2 = edge.VertexIndex2;

            // 共有辺以外の頂点を見つける
            int opposite1 = face1.VertexIndices.First(v => v != v1 && v != v2);
            int opposite2 = face2.VertexIndices.First(v => v != v1 && v != v2);

            // 巻き順を維持するため、元の面の頂点リストで共有辺の頂点を対角頂点に置き換える
            // face1: v2 → opposite2 に置き換え
            // face2: v1 → opposite1 に置き換え
            var newVerts1 = new List<int>(face1.VertexIndices);
            var newVerts2 = new List<int>(face2.VertexIndices);

            int idx1 = newVerts1.IndexOf(v2);
            int idx2 = newVerts2.IndexOf(v1);

            if (idx1 >= 0) newVerts1[idx1] = opposite2;
            if (idx2 >= 0) newVerts2[idx2] = opposite1;

            face1.VertexIndices = newVerts1;
            face2.VertexIndices = newVerts2;

            // UV/Normalインデックスも更新（簡易版: リセット）
            face1.UVIndices = new List<int> { 0, 0, 0 };
            face1.NormalIndices = new List<int> { 0, 0, 0 };
            face2.UVIndices = new List<int> { 0, 0, 0 };
            face2.NormalIndices = new List<int> { 0, 0, 0 };

            // メッシュ更新
            ctx.SyncMesh?.Invoke();

            // スナップショット（操作後）& Undo記録
            var after = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
            ctx.UndoController?.RecordTopologyChange(before, after, "Edge Flip");
        }

        /// <summary>
        /// Quad Split実行
        /// </summary>
        private void ExecuteSplit(ToolContext ctx, int faceIndex, int startVertex, int endVertex)
        {
            if (faceIndex < 0 || faceIndex >= ctx.MeshData.FaceCount) return;

            var face = ctx.MeshData.Faces[faceIndex];
            if (face.VertexIndices.Count != 4) return;

            // スナップショット（操作前）
            var before = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);

            // 頂点の位置を特定
            int startIdx = face.VertexIndices.IndexOf(startVertex);
            int endIdx = face.VertexIndices.IndexOf(endVertex);
            if (startIdx < 0 || endIdx < 0) return;

            // 対角かどうか確認
            if (Math.Abs(startIdx - endIdx) != 2) return;

            // 四角形の頂点（順序通り）
            int v0 = face.VertexIndices[0];
            int v1 = face.VertexIndices[1];
            int v2 = face.VertexIndices[2];
            int v3 = face.VertexIndices[3];

            Face newFace1, newFace2;

            if ((startIdx == 0 && endIdx == 2) || (startIdx == 2 && endIdx == 0))
            {
                // 0-2対角線で分割
                newFace1 = new Face(v0, v1, v2);
                newFace2 = new Face(v0, v2, v3);
            }
            else
            {
                // 1-3対角線で分割
                newFace1 = new Face(v0, v1, v3);
                newFace2 = new Face(v1, v2, v3);
            }

            // UV/Normalインデックス設定
            newFace1.UVIndices = new List<int> { 0, 0, 0 };
            newFace1.NormalIndices = new List<int> { 0, 0, 0 };
            newFace2.UVIndices = new List<int> { 0, 0, 0 };
            newFace2.NormalIndices = new List<int> { 0, 0, 0 };

            // 元の面を置換、新しい面を追加
            ctx.MeshData.Faces[faceIndex] = newFace1;
            ctx.MeshData.Faces.Add(newFace2);

            // メッシュ更新
            ctx.SyncMesh?.Invoke();

            // スナップショット（操作後）& Undo記録
            var after = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
            ctx.UndoController?.RecordTopologyChange(before, after, "Quad Split");
        }

        /// <summary>
        /// Edge Dissolve実行
        /// </summary>
        private void ExecuteDissolve(ToolContext ctx, EdgeInfo edge)
        {
            if (!edge.IsShared) return;

            var face1 = ctx.MeshData.Faces[edge.FaceIndex1];
            var face2 = ctx.MeshData.Faces[edge.FaceIndex2];

            // スナップショット（操作前）
            var before = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);

            // 2つの面を結合した多角形を作成
            var mergedVertices = MergeFaceVertices(face1, face2, edge.VertexIndex1, edge.VertexIndex2);
            if (mergedVertices == null || mergedVertices.Count < 3)
            {
                Debug.LogWarning("Cannot dissolve edge");
                return;
            }

            // 新しい面を作成
            var newFace = new Face
            {
                VertexIndices = mergedVertices,
                UVIndices = Enumerable.Repeat(0, mergedVertices.Count).ToList(),
                NormalIndices = Enumerable.Repeat(0, mergedVertices.Count).ToList()
            };

            // 面を削除（大きいインデックスから）
            int removeFirst = Math.Max(edge.FaceIndex1, edge.FaceIndex2);
            int removeSecond = Math.Min(edge.FaceIndex1, edge.FaceIndex2);

            ctx.MeshData.Faces.RemoveAt(removeFirst);
            ctx.MeshData.Faces.RemoveAt(removeSecond);

            // 新しい面を追加
            ctx.MeshData.Faces.Add(newFace);

            // メッシュ更新
            ctx.SyncMesh?.Invoke();

            // スナップショット（操作後）& Undo記録
            var after = MeshDataSnapshot.Capture(ctx.UndoController.MeshContext);
            ctx.UndoController?.RecordTopologyChange(before, after, "Edge Dissolve");
        }

        /// <summary>
        /// 2つの面の頂点を結合（共有辺を除去）
        /// </summary>
        private List<int> MergeFaceVertices(Face face1, Face face2, int sharedV1, int sharedV2)
        {
            var verts1 = face1.VertexIndices;
            var verts2 = face2.VertexIndices;

            // face1から共有辺を除いた頂点列を取得
            // sharedV1 -> sharedV2の辺を見つけ、その間の頂点を除外
            int idx1Start = verts1.IndexOf(sharedV1);
            int idx1End = verts1.IndexOf(sharedV2);

            if (idx1Start < 0 || idx1End < 0) return null;

            // face2でsharedV2 -> sharedV1の順を見つける
            int idx2Start = verts2.IndexOf(sharedV2);
            int idx2End = verts2.IndexOf(sharedV1);

            if (idx2Start < 0 || idx2End < 0) return null;

            var result = new List<int>();

            // face1をsharedV2からsharedV1まで（共有辺を除く）
            int n1 = verts1.Count;
            int current = idx1End;
            while (current != idx1Start)
            {
                result.Add(verts1[current]);
                current = (current + 1) % n1;
            }
            // sharedV1は追加しない（face2で追加される）

            // face2をsharedV1からsharedV2まで（共有辺を除く）
            int n2 = verts2.Count;
            current = idx2End;
            while (current != idx2Start)
            {
                result.Add(verts2[current]);
                current = (current + 1) % n2;
            }
            // sharedV2は追加しない（face1で既に追加済み）

            return result;
        }

        // ================================================================
        // 描画
        // ================================================================

        /// <summary>
        /// 辺ハイライト描画
        /// </summary>
        private void DrawEdgeHighlight(ToolContext ctx)
        {
            if (_hoveredEdge.ScreenPos1 == Vector2.zero && _hoveredEdge.ScreenPos2 == Vector2.zero)
                return;

            // 共有辺かどうかで色を変える
            Color edgeColor = _hoveredEdge.IsShared ? Color.yellow : Color.gray;

            Handles.BeginGUI();

            // 辺を描画
            Handles.color = edgeColor;
            Handles.DrawAAPolyLine(4f, _hoveredEdge.ScreenPos1, _hoveredEdge.ScreenPos2);

            // 端点を描画
            GUI.color = edgeColor;
            float size = 6f;
            GUI.DrawTexture(new Rect(_hoveredEdge.ScreenPos1.x - size / 2, _hoveredEdge.ScreenPos1.y - size / 2, size, size),
                EditorGUIUtility.whiteTexture);
            GUI.DrawTexture(new Rect(_hoveredEdge.ScreenPos2.x - size / 2, _hoveredEdge.ScreenPos2.y - size / 2, size, size),
                EditorGUIUtility.whiteTexture);

            GUI.color = Color.white;
            Handles.EndGUI();
        }

        /// <summary>
        /// Split プレビュー描画
        /// </summary>
        private void DrawSplitPreview(ToolContext ctx)
        {
            if (_hoveredFaceIndex < 0) return;

            var face = ctx.MeshData.Faces[_hoveredFaceIndex];
            if (face.VertexIndices.Count != 4) return;

            Handles.BeginGUI();

            // 四角形の辺を描画
            Color quadColor = new Color(0f, 1f, 1f, 0.5f);
            Handles.color = quadColor;

            for (int i = 0; i < 4; i++)
            {
                int v1 = face.VertexIndices[i];
                int v2 = face.VertexIndices[(i + 1) % 4];
                Vector2 sp1 = ctx.WorldToScreenPos(ctx.MeshData.Vertices[v1].Position, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Vector2 sp2 = ctx.WorldToScreenPos(ctx.MeshData.Vertices[v2].Position, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                Handles.DrawAAPolyLine(3f, sp1, sp2);
            }

            // ドラッグ中なら対角線プレビュー
            if (_isDragging && _startVertexIndex >= 0)
            {
                Vector2 startScreen = ctx.WorldToScreenPos(ctx.MeshData.Vertices[_startVertexIndex].Position, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                if (_endVertexIndex >= 0)
                {
                    // 対角線（確定）
                    Vector2 endScreen = ctx.WorldToScreenPos(ctx.MeshData.Vertices[_endVertexIndex].Position, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    Handles.color = Color.green;
                    Handles.DrawAAPolyLine(4f, startScreen, endScreen);
                }
                else
                {
                    // マウスまでの線
                    Handles.color = Color.yellow;
                    Handles.DrawAAPolyLine(2f, startScreen, _currentScreenPos);
                }

                // 開始点を描画
                GUI.color = Color.yellow;
                float size = 8f;
                GUI.DrawTexture(new Rect(startScreen.x - size / 2, startScreen.y - size / 2, size, size),
                    EditorGUIUtility.whiteTexture);
            }

            GUI.color = Color.white;
            Handles.EndGUI();
        }
    }
}