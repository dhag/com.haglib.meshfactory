// Tools/KnifeTool.cs
// ナイフツール - 面を切断する

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
    /// ナイフツールのモード
    /// </summary>
    public enum KnifeMode
    {
        /// <summary>単一面切断</summary>
        SingleCut,
        /// <summary>接続面を連続切断</summary>
        ChainCut,
        /// <summary>接続辺を連続消去</summary>
        ChainErase,
        /// <summary>頂点ナイフ</summary>
        VertexKnife,
        /// <summary>連続頂点ナイフ</summary>
        ChainVertexKnife,
        /// <summary>ラダー選択ナイフ</summary>
        LadderKnife
    }

    /// <summary>
    /// ナイフツール
    /// </summary>
    public class KnifeTool : IEditTool
    {
        public string Name => "Knife";

        // === 設定 ===
        private KnifeMode _mode = KnifeMode.SingleCut;

        // === ドラッグ状態 ===
        private bool _isDragging;
        private Vector2 _startScreenPos;
        private Vector2 _currentScreenPos;
        private bool _isShiftHeld;

        // === 検出結果 ===
        private int _targetFaceIndex = -1;
        private List<EdgeIntersection> _intersections = new List<EdgeIntersection>();

        // === 定数 ===
        private const float SNAP_ANGLE_THRESHOLD = 15f; // 水平/垂直スナップの角度閾値

        // === モード選択用 ===
        private static readonly string[] ModeNames = { "Single", "Chain", "Erase", "Vertex", "ChainVtx", "Ladder" };
        private static readonly KnifeMode[] ModeValues = {
            KnifeMode.SingleCut,
            KnifeMode.ChainCut,
            KnifeMode.ChainErase,
            KnifeMode.VertexKnife,
            KnifeMode.ChainVertexKnife,
            KnifeMode.LadderKnife
        };

        /// <summary>
        /// 辺と交点の情報
        /// </summary>
        private struct EdgeIntersection
        {
            public int EdgeStartIndex;  // 辺の開始頂点（面内でのローカルインデックス）
            public int EdgeEndIndex;    // 辺の終了頂点（面内でのローカルインデックス）
            public float T;             // 辺上での位置 (0-1)
            public Vector3 WorldPos;    // ワールド座標での交点位置
            public Vector2 ScreenPos;   // スクリーン座標での交点位置
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
            _isShiftHeld = Event.current.shift;
            _targetFaceIndex = -1;
            _intersections.Clear();

            // 最前面の面を検出
            _targetFaceIndex = FindFrontmostFaceAtPosition(ctx, mousePos);

            ctx.Repaint?.Invoke();
            return true;
        }

        public bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta)
        {
            if (!_isDragging || ctx.MeshData == null) return false;

            _currentScreenPos = mousePos;
            _isShiftHeld = Event.current.shift;

            // Shiftキーで水平/垂直スナップ
            if (_isShiftHeld)
            {
                _currentScreenPos = SnapToAxis(_startScreenPos, mousePos);
            }

            // 交差点を再計算
            UpdateIntersections(ctx);

            ctx.Repaint?.Invoke();
            return true;
        }

        public bool OnMouseUp(ToolContext ctx, Vector2 mousePos)
        {
            if (!_isDragging || ctx.MeshData == null)
            {
                _isDragging = false;
                return false;
            }

            _isDragging = false;

            // Shiftキーで水平/垂直スナップ
            if (_isShiftHeld)
            {
                _currentScreenPos = SnapToAxis(_startScreenPos, mousePos);
            }
            else
            {
                _currentScreenPos = mousePos;
            }

            // 最終的な交差点を計算
            UpdateIntersections(ctx);

            // 2つの交点があれば切断実行
            if (_intersections.Count >= 2)
            {
                ExecuteCut(ctx);
            }

            // リセット
            _targetFaceIndex = -1;
            _intersections.Clear();

            ctx.Repaint?.Invoke();
            return true;
        }

        public void DrawGizmo(ToolContext ctx)
        {
            if (ctx.MeshData == null) return;

            // ワイヤーを描画
            if (_isDragging)
            {
                DrawWire(ctx);
            }

            // 交差点を描画
            DrawIntersections(ctx);

            // ターゲット面をハイライト
            if (_targetFaceIndex >= 0)
            {
                DrawTargetFaceHighlight(ctx);
            }
        }

        public void DrawSettingsUI()
        {
            EditorGUILayout.LabelField("Knife Tool", EditorStyles.boldLabel);

            // モード選択（SelectionGrid）
            int currentIndex = System.Array.IndexOf(ModeValues, _mode);
            int newIndex = GUILayout.SelectionGrid(currentIndex, ModeNames, 3);
            if (newIndex != currentIndex && newIndex >= 0 && newIndex < ModeValues.Length)
            {
                _mode = ModeValues[newIndex];
            }

            EditorGUILayout.Space(5);

            // ヘルプテキスト
            switch (_mode)
            {
                case KnifeMode.SingleCut:
                    EditorGUILayout.HelpBox(
                        "Drag across a face to cut it.\n" +
                        "Shift: Snap to horizontal/vertical",
                        MessageType.Info);
                    break;
                case KnifeMode.ChainCut:
                    EditorGUILayout.HelpBox(
                        "Drag across opposite edges to chain cut.\n" +
                        "Shift: Cut through edge centers",
                        MessageType.Info);
                    break;
                default:
                    EditorGUILayout.HelpBox("Not implemented yet", MessageType.Warning);
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
            _isDragging = false;
            _targetFaceIndex = -1;
            _intersections.Clear();
        }

        // ================================================================
        // 面検出
        // ================================================================

        /// <summary>
        /// 指定位置で最前面（カメラに最も近い）の面を検出
        /// </summary>
        private int FindFrontmostFaceAtPosition(ToolContext ctx, Vector2 screenPos)
        {
            int bestFaceIndex = -1;
            float bestDepth = float.MaxValue;

            for (int faceIdx = 0; faceIdx < ctx.MeshData.FaceCount; faceIdx++)
            {
                var face = ctx.MeshData.Faces[faceIdx];
                if (face.VertexCount < 3) continue;

                // 面のスクリーン座標を取得
                var screenPoints = new List<Vector2>();
                float avgDepth = 0;

                foreach (var vIdx in face.VertexIndices)
                {
                    var worldPos = ctx.MeshData.Vertices[vIdx].Position;
                    var sp = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                    screenPoints.Add(sp);

                    // 深度を計算（カメラからの距離）
                    avgDepth += Vector3.Distance(worldPos, ctx.CameraPosition);
                }
                avgDepth /= face.VertexCount;

                // 点が面の内部にあるかチェック
                if (IsPointInPolygon(screenPos, screenPoints))
                {
                    if (avgDepth < bestDepth)
                    {
                        bestDepth = avgDepth;
                        bestFaceIndex = faceIdx;
                    }
                }
            }

            return bestFaceIndex;
        }

        /// <summary>
        /// ドラッグライン上で最前面の面を検出（開始点付近を優先）
        /// </summary>
        private int FindFrontmostFaceOnLine(ToolContext ctx)
        {
            // まず開始点で検出
            int faceIdx = FindFrontmostFaceAtPosition(ctx, _startScreenPos);
            if (faceIdx >= 0) return faceIdx;

            // 開始点で見つからなければライン上でサンプリング
            int samples = 10;
            for (int i = 1; i <= samples; i++)
            {
                float t = i / (float)samples;
                var samplePos = Vector2.Lerp(_startScreenPos, _currentScreenPos, t);
                faceIdx = FindFrontmostFaceAtPosition(ctx, samplePos);
                if (faceIdx >= 0) return faceIdx;
            }

            return -1;
        }

        // ================================================================
        // 交差点計算
        // ================================================================

        /// <summary>
        /// 交差点を更新
        /// </summary>
        private void UpdateIntersections(ToolContext ctx)
        {
            _intersections.Clear();

            if (ctx.MeshData == null || ctx.MeshData.FaceCount == 0) return;

            // 全ての面について交差点を計算し、2つの交差点を持つ面を探す
            int bestFaceIndex = -1;
            List<EdgeIntersection> bestIntersections = null;
            float bestMinDistance = float.MaxValue; // ワイヤー開始点に最も近い交差点を持つ面を優先

            for (int faceIdx = 0; faceIdx < ctx.MeshData.FaceCount; faceIdx++)
            {
                var face = ctx.MeshData.Faces[faceIdx];
                if (face.VertexCount < 3) continue;

                var intersections = FindIntersectionsForFace(ctx, face);

                if (intersections.Count >= 2)
                {
                    // ワイヤー開始点に最も近い交差点の距離
                    float minDist = intersections.Min(x => Vector2.Distance(_startScreenPos, x.ScreenPos));

                    if (minDist < bestMinDistance)
                    {
                        bestMinDistance = minDist;
                        bestFaceIndex = faceIdx;
                        bestIntersections = intersections;
                    }
                }
            }

            if (bestFaceIndex >= 0 && bestIntersections != null)
            {
                _targetFaceIndex = bestFaceIndex;

                // ワイヤー上での位置でソート（開始点に近い順）、最初の2つを使用
                _intersections = bestIntersections
                    .OrderBy(x => Vector2.Distance(_startScreenPos, x.ScreenPos))
                    .Take(2)
                    .ToList();
            }
            else
            {
                _targetFaceIndex = -1;
            }
        }

        /// <summary>
        /// 指定した面とワイヤーの交差点を計算
        /// </summary>
        private List<EdgeIntersection> FindIntersectionsForFace(ToolContext ctx, Face face)
        {
            var intersections = new List<EdgeIntersection>();

            for (int i = 0; i < face.VertexCount; i++)
            {
                int nextI = (i + 1) % face.VertexCount;

                int vIdx0 = face.VertexIndices[i];
                int vIdx1 = face.VertexIndices[nextI];

                var p0 = ctx.MeshData.Vertices[vIdx0].Position;
                var p1 = ctx.MeshData.Vertices[vIdx1].Position;

                var sp0 = ctx.WorldToScreenPos(p0, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                var sp1 = ctx.WorldToScreenPos(p1, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);

                // 2D線分交差判定
                if (LineSegmentIntersection(
                    _startScreenPos, _currentScreenPos,
                    sp0, sp1,
                    out float t1, out float t2))
                {
                    // 辺上の位置 t2 が端点でなければ
                    if (t2 > 0.0001f && t2 < 0.9999f)
                    {
                        var worldPos = Vector3.Lerp(p0, p1, t2);
                        var screenPos = Vector2.Lerp(sp0, sp1, t2);

                        intersections.Add(new EdgeIntersection
                        {
                            EdgeStartIndex = i,
                            EdgeEndIndex = nextI,
                            T = t2,
                            WorldPos = worldPos,
                            ScreenPos = screenPos
                        });
                    }
                }
            }

            return intersections;
        }

        // ================================================================
        // 切断実行
        // ================================================================

        /// <summary>
        /// 切断を実行
        /// </summary>
        private void ExecuteCut(ToolContext ctx)
        {
            if (_intersections.Count < 2) return;
            if (_targetFaceIndex < 0 || _targetFaceIndex >= ctx.MeshData.FaceCount) return;

            var face = ctx.MeshData.Faces[_targetFaceIndex];
            var inter0 = _intersections[0];
            var inter1 = _intersections[1];

            // 同じ辺上の交点は無視
            if (inter0.EdgeStartIndex == inter1.EdgeStartIndex) return;

            // Undo記録の準備
            var originalFace = face.Clone();
            var addedVertices = new List<(int Index, Vertex Vertex)>();

            // 新しい頂点を追加
            int newVertexIdx0 = ctx.MeshData.VertexCount;
            var newVertex0 = new Vertex(inter0.WorldPos);
            // UV補間
            InterpolateVertexAttributes(ctx.MeshData, face, inter0.EdgeStartIndex, inter0.EdgeEndIndex, inter0.T, newVertex0);
            ctx.MeshData.Vertices.Add(newVertex0);
            addedVertices.Add((newVertexIdx0, newVertex0.Clone()));

            int newVertexIdx1 = ctx.MeshData.VertexCount;
            var newVertex1 = new Vertex(inter1.WorldPos);
            InterpolateVertexAttributes(ctx.MeshData, face, inter1.EdgeStartIndex, inter1.EdgeEndIndex, inter1.T, newVertex1);
            ctx.MeshData.Vertices.Add(newVertex1);
            addedVertices.Add((newVertexIdx1, newVertex1.Clone()));

            // 面を分割
            var (face1, face2) = SplitFace(face, inter0, inter1, newVertexIdx0, newVertexIdx1);

            // 元の面を置き換え
            ctx.MeshData.Faces[_targetFaceIndex] = face1;
            int newFaceIdx = ctx.MeshData.FaceCount;
            ctx.MeshData.Faces.Add(face2);

            // Undo記録
            ctx.UndoController?.RecordKnifeCut(
                _targetFaceIndex,
                originalFace,
                face1.Clone(),
                newFaceIdx,
                face2.Clone(),
                addedVertices
            );

            // メッシュを更新
            ctx.SyncMesh?.Invoke();
        }

        /// <summary>
        /// 面を2つに分割
        /// </summary>
        private (Face face1, Face face2) SplitFace(
            Face originalFace,
            EdgeIntersection inter0,
            EdgeIntersection inter1,
            int newVertexIdx0,
            int newVertexIdx1)
        {
            var verts = originalFace.VertexIndices;
            var uvs = originalFace.UVIndices;
            var normals = originalFace.NormalIndices;
            int n = verts.Count;

            // 交差辺のインデックスを取得（小さい方が先）
            int edge0Start = inter0.EdgeStartIndex;
            int edge1Start = inter1.EdgeStartIndex;

            if (edge0Start > edge1Start)
            {
                // swap
                (edge0Start, edge1Start) = (edge1Start, edge0Start);
                (newVertexIdx0, newVertexIdx1) = (newVertexIdx1, newVertexIdx0);
                (inter0, inter1) = (inter1, inter0);
            }

            // Face1: edge0Start+1 から edge1Start までの頂点 + newVertex1 + newVertex0
            var face1Verts = new List<int>();
            var face1UVs = new List<int>();
            var face1Normals = new List<int>();

            face1Verts.Add(newVertexIdx0);
            face1UVs.Add(0);
            face1Normals.Add(0);

            for (int i = edge0Start + 1; i <= edge1Start; i++)
            {
                face1Verts.Add(verts[i]);
                face1UVs.Add(uvs.Count > i ? uvs[i] : 0);
                face1Normals.Add(normals.Count > i ? normals[i] : 0);
            }

            face1Verts.Add(newVertexIdx1);
            face1UVs.Add(0);
            face1Normals.Add(0);

            // Face2: edge1Start+1 から最後まで + 最初から edge0Start まで + newVertex0 + newVertex1
            var face2Verts = new List<int>();
            var face2UVs = new List<int>();
            var face2Normals = new List<int>();

            face2Verts.Add(newVertexIdx1);
            face2UVs.Add(0);
            face2Normals.Add(0);

            for (int i = edge1Start + 1; i < n; i++)
            {
                face2Verts.Add(verts[i]);
                face2UVs.Add(uvs.Count > i ? uvs[i] : 0);
                face2Normals.Add(normals.Count > i ? normals[i] : 0);
            }
            for (int i = 0; i <= edge0Start; i++)
            {
                face2Verts.Add(verts[i]);
                face2UVs.Add(uvs.Count > i ? uvs[i] : 0);
                face2Normals.Add(normals.Count > i ? normals[i] : 0);
            }

            face2Verts.Add(newVertexIdx0);
            face2UVs.Add(0);
            face2Normals.Add(0);

            var face1 = new Face
            {
                VertexIndices = face1Verts,
                UVIndices = face1UVs,
                NormalIndices = face1Normals
            };

            var face2 = new Face
            {
                VertexIndices = face2Verts,
                UVIndices = face2UVs,
                NormalIndices = face2Normals
            };

            return (face1, face2);
        }

        /// <summary>
        /// 頂点属性を補間
        /// </summary>
        private void InterpolateVertexAttributes(
            MeshData meshData,
            Face face,
            int localIdx0,
            int localIdx1,
            float t,
            Vertex targetVertex)
        {
            int vIdx0 = face.VertexIndices[localIdx0];
            int vIdx1 = face.VertexIndices[localIdx1];

            var v0 = meshData.Vertices[vIdx0];
            var v1 = meshData.Vertices[vIdx1];

            // UV補間
            if (v0.UVs.Count > 0 && v1.UVs.Count > 0)
            {
                int uvIdx0 = face.UVIndices.Count > localIdx0 ? face.UVIndices[localIdx0] : 0;
                int uvIdx1 = face.UVIndices.Count > localIdx1 ? face.UVIndices[localIdx1] : 0;

                if (uvIdx0 < v0.UVs.Count && uvIdx1 < v1.UVs.Count)
                {
                    var uv = Vector2.Lerp(v0.UVs[uvIdx0], v1.UVs[uvIdx1], t);
                    targetVertex.UVs.Add(uv);
                }
            }

            // 法線補間
            if (v0.Normals.Count > 0 && v1.Normals.Count > 0)
            {
                int nIdx0 = face.NormalIndices.Count > localIdx0 ? face.NormalIndices[localIdx0] : 0;
                int nIdx1 = face.NormalIndices.Count > localIdx1 ? face.NormalIndices[localIdx1] : 0;

                if (nIdx0 < v0.Normals.Count && nIdx1 < v1.Normals.Count)
                {
                    var normal = Vector3.Lerp(v0.Normals[nIdx0], v1.Normals[nIdx1], t).normalized;
                    targetVertex.Normals.Add(normal);
                }
            }
        }

        // ================================================================
        // 描画
        // ================================================================

        /// <summary>
        /// ワイヤーを描画
        /// </summary>
        private void DrawWire(ToolContext ctx)
        {
            Handles.color = new Color(1f, 0.5f, 0f, 0.8f); // オレンジ

            // スクリーン座標からGUI座標に変換
            var start = new Vector3(_startScreenPos.x, _startScreenPos.y, 0);
            var end = new Vector3(_currentScreenPos.x, _currentScreenPos.y, 0);

            Handles.DrawLine(start, end, 2f);
        }

        /// <summary>
        /// 交差点を描画
        /// </summary>
        private void DrawIntersections(ToolContext ctx)
        {
            if (_intersections.Count == 0) return;

            // 1つ目の交差点は黄色
            if (_intersections.Count >= 1)
            {
                Handles.color = Color.yellow;
                var pos0 = new Vector3(_intersections[0].ScreenPos.x, _intersections[0].ScreenPos.y, 0);
                Handles.DrawSolidDisc(pos0, Vector3.forward, 6f);
            }

            // 2つ目の交差点（対向）はオレンジ
            if (_intersections.Count >= 2)
            {
                Handles.color = new Color(1f, 0.5f, 0f); // オレンジ
                var pos1 = new Vector3(_intersections[1].ScreenPos.x, _intersections[1].ScreenPos.y, 0);
                Handles.DrawSolidDisc(pos1, Vector3.forward, 6f);

                // 緑で接続線を描画
                Handles.color = Color.green;
                var p0 = new Vector3(_intersections[0].ScreenPos.x, _intersections[0].ScreenPos.y, 0);
                var p1 = new Vector3(_intersections[1].ScreenPos.x, _intersections[1].ScreenPos.y, 0);
                Handles.DrawLine(p0, p1, 3f);
            }
        }

        /// <summary>
        /// ターゲット面をハイライト
        /// </summary>
        private void DrawTargetFaceHighlight(ToolContext ctx)
        {
            if (_targetFaceIndex < 0 || _targetFaceIndex >= ctx.MeshData.FaceCount) return;

            var face = ctx.MeshData.Faces[_targetFaceIndex];
            if (face.VertexCount < 3) return;

            Handles.color = new Color(0f, 1f, 1f, 0.3f); // シアン半透明

            // 面の輪郭を描画
            var points = new Vector3[face.VertexCount + 1];
            for (int i = 0; i < face.VertexCount; i++)
            {
                var worldPos = ctx.MeshData.Vertices[face.VertexIndices[i]].Position;
                var screenPos = ctx.WorldToScreenPos(worldPos, ctx.PreviewRect, ctx.CameraPosition, ctx.CameraTarget);
                points[i] = new Vector3(screenPos.x, screenPos.y, 0);
            }
            points[face.VertexCount] = points[0]; // 閉じる

            Handles.DrawAAPolyLine(3f, points);
        }

        // ================================================================
        // ユーティリティ
        // ================================================================

        /// <summary>
        /// 水平/垂直方向にスナップ
        /// </summary>
        private Vector2 SnapToAxis(Vector2 start, Vector2 current)
        {
            var delta = current - start;
            float angle = Mathf.Atan2(Mathf.Abs(delta.y), Mathf.Abs(delta.x)) * Mathf.Rad2Deg;

            float length = delta.magnitude;

            if (angle < SNAP_ANGLE_THRESHOLD)
            {
                // 水平
                return start + new Vector2(Mathf.Sign(delta.x) * length, 0);
            }
            else if (angle > 90 - SNAP_ANGLE_THRESHOLD)
            {
                // 垂直
                return start + new Vector2(0, Mathf.Sign(delta.y) * length);
            }
            else
            {
                // 45度
                float diag = length / Mathf.Sqrt(2);
                return start + new Vector2(Mathf.Sign(delta.x) * diag, Mathf.Sign(delta.y) * diag);
            }
        }

        /// <summary>
        /// 点が多角形の内部にあるか判定（2D）
        /// </summary>
        private bool IsPointInPolygon(Vector2 point, List<Vector2> polygon)
        {
            if (polygon.Count < 3) return false;

            bool inside = false;
            int j = polygon.Count - 1;

            for (int i = 0; i < polygon.Count; i++)
            {
                if ((polygon[i].y < point.y && polygon[j].y >= point.y ||
                     polygon[j].y < point.y && polygon[i].y >= point.y) &&
                    (polygon[i].x + (point.y - polygon[i].y) / (polygon[j].y - polygon[i].y) * (polygon[j].x - polygon[i].x) < point.x))
                {
                    inside = !inside;
                }
                j = i;
            }

            return inside;
        }

        /// <summary>
        /// 2つの線分の交差判定
        /// </summary>
        /// <returns>交差する場合true、t1はline1上の位置、t2はline2上の位置</returns>
        private bool LineSegmentIntersection(
            Vector2 p1, Vector2 p2, // line1
            Vector2 p3, Vector2 p4, // line2
            out float t1, out float t2)
        {
            t1 = 0;
            t2 = 0;

            float d = (p4.y - p3.y) * (p2.x - p1.x) - (p4.x - p3.x) * (p2.y - p1.y);

            if (Mathf.Abs(d) < 0.0001f)
                return false; // 平行

            float ua = ((p4.x - p3.x) * (p1.y - p3.y) - (p4.y - p3.y) * (p1.x - p3.x)) / d;
            float ub = ((p2.x - p1.x) * (p1.y - p3.y) - (p2.y - p1.y) * (p1.x - p3.x)) / d;

            t1 = ua;
            t2 = ub;

            // 両方の線分上にあるか
            return ua >= 0 && ua <= 1 && ub >= 0 && ub <= 1;
        }

        /// <summary>
        /// 直線（無限）と線分の交差判定
        /// </summary>
        /// <param name="lineP1">直線上の点1</param>
        /// <param name="lineP2">直線上の点2</param>
        /// <param name="segP1">線分の始点</param>
        /// <param name="segP2">線分の終点</param>
        /// <param name="t">線分上の交点位置（0-1）</param>
        /// <returns>交差する場合true</returns>
        private bool LineIntersectsSegment(
            Vector2 lineP1, Vector2 lineP2,
            Vector2 segP1, Vector2 segP2,
            out float t)
        {
            t = 0;

            float d = (segP2.y - segP1.y) * (lineP2.x - lineP1.x) - (segP2.x - segP1.x) * (lineP2.y - lineP1.y);

            if (Mathf.Abs(d) < 0.0001f)
                return false; // 平行

            // 線分上の交点位置を計算
            float ub = ((lineP2.x - lineP1.x) * (lineP1.y - segP1.y) - (lineP2.y - lineP1.y) * (lineP1.x - segP1.x)) / d;

            t = ub;

            // 線分上にあるか（直線は無限なので t1 のチェック不要）
            return ub >= 0 && ub <= 1;
        }

        // ================================================================
        // プロパティ
        // ================================================================

        /// <summary>
        /// 現在のモード
        /// </summary>
        public KnifeMode Mode
        {
            get => _mode;
            set => _mode = value;
        }
    }
}