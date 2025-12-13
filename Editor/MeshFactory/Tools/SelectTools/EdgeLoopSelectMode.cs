// Assets/Editor/MeshFactory/Tools/Selection/Modes/EdgeLoopSelectMode.cs
// 連続エッジ選択モード

using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Selection;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 連続エッジ選択モード
    /// </summary>
    public class EdgeLoopSelectMode : IAdvancedSelectMode
    {
        public bool HandleClick(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;

            var edge = SelectionHelper.FindNearestEdgePair(toolCtx, mousePos);
            if (!edge.HasValue)
            {
                var legacyEdge = SelectionHelper.FindNearestEdgeLegacy(toolCtx, mousePos);
                if (legacyEdge.Item1 < 0) return false;
                edge = new VertexPair(legacyEdge.Item1, legacyEdge.Item2);
            }

            var loopEdges = GetEdgeLoopEdges(toolCtx.MeshData, edge.Value, ctx.EdgeLoopThreshold);

            if (selectMode.Has(MeshSelectMode.Vertex))
            {
                var verts = new HashSet<int>();
                foreach (var e in loopEdges) { verts.Add(e.V1); verts.Add(e.V2); }
                SelectionHelper.ApplyVertexSelection(toolCtx, verts.ToList(), ctx.AddToSelection);
            }

            if (selectMode.Has(MeshSelectMode.Edge))
                SelectionHelper.ApplyEdgeSelection(toolCtx, loopEdges, ctx.AddToSelection);

            if (selectMode.Has(MeshSelectMode.Face))
            {
                var faces = SelectionHelper.GetAdjacentFaces(toolCtx, loopEdges);
                SelectionHelper.ApplyFaceSelection(toolCtx, faces, ctx.AddToSelection);
            }

            return true;
        }

        public void UpdatePreview(AdvancedSelectContext ctx, Vector2 mousePos, MeshSelectMode selectMode)
        {
            var toolCtx = ctx.ToolCtx;

            ctx.HoveredEdgePair = SelectionHelper.FindNearestEdgePair(toolCtx, mousePos);
            if (!ctx.HoveredEdgePair.HasValue)
            {
                var legacyEdge = SelectionHelper.FindNearestEdgeLegacy(toolCtx, mousePos);
                if (legacyEdge.Item1 >= 0)
                    ctx.HoveredEdgePair = new VertexPair(legacyEdge.Item1, legacyEdge.Item2);
            }

            if (!ctx.HoveredEdgePair.HasValue) return;

            var loopEdges = GetEdgeLoopEdges(toolCtx.MeshData, ctx.HoveredEdgePair.Value, ctx.EdgeLoopThreshold);

            if (selectMode.Has(MeshSelectMode.Vertex))
            {
                var verts = new HashSet<int>();
                foreach (var e in loopEdges) { verts.Add(e.V1); verts.Add(e.V2); }
                ctx.PreviewVertices.AddRange(verts);
            }
            if (selectMode.Has(MeshSelectMode.Edge))
                ctx.PreviewEdges.AddRange(loopEdges);
            if (selectMode.Has(MeshSelectMode.Face))
                ctx.PreviewFaces.AddRange(SelectionHelper.GetAdjacentFaces(toolCtx, loopEdges));
        }

        public void Reset() { }

        public void DrawModeSettingsUI()
        {
            EditorGUILayout.HelpBox(
                "Click edge to select edge loop.\n" +
                "• Vertex: loop vertices\n" +
                "• Edge: loop edges\n" +
                "• Face: adjacent faces",
                MessageType.Info);
        }

        // ================================================================
        // アルゴリズム
        // ================================================================

        private List<VertexPair> GetEdgeLoopEdges(MeshData meshData, VertexPair startEdge, float threshold)
        {
            var result = new HashSet<VertexPair>();
            var visitedEdges = new HashSet<VertexPair>();

            Vector3 edgeDir = (meshData.Vertices[startEdge.V2].Position -
                              meshData.Vertices[startEdge.V1].Position).normalized;

            var adjacency = SelectionHelper.BuildVertexAdjacency(meshData);

            TraverseEdgeLoopEdges(meshData, startEdge.V1, startEdge.V2, edgeDir, adjacency, visitedEdges, result, threshold);
            TraverseEdgeLoopEdges(meshData, startEdge.V2, startEdge.V1, -edgeDir, adjacency, visitedEdges, result, threshold);

            return result.ToList();
        }

        private void TraverseEdgeLoopEdges(MeshData meshData, int fromV, int toV, Vector3 direction,
            Dictionary<int, HashSet<int>> adjacency, HashSet<VertexPair> visitedEdges, HashSet<VertexPair> result, float threshold)
        {
            int current = toV;
            int prev = fromV;
            Vector3 currentDir = direction;

            while (true)
            {
                var edge = new VertexPair(prev, current);
                if (visitedEdges.Contains(edge)) break;
                visitedEdges.Add(edge);
                result.Add(edge);

                if (!adjacency.TryGetValue(current, out var neighbors)) break;

                int bestNext = -1;
                float bestDot = threshold;

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
    }
}
