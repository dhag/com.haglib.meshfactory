// Editor/MeshFactory/Core/MeshGPURenderer_3D.cs
// 3D描画拡張（ワイヤフレーム・頂点を深度テスト付きで描画）
// MeshGPURendererのpartial拡張

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEditor;
using MeshFactory.Data;

namespace MeshFactory.Rendering
{
    public partial class MeshGPURenderer
    {
        // ================================================================
        // 3D描画用フィールド
        // ================================================================
        
        private Material _wireframe3DMaterial;
        private Material _point3DMaterial;
        private Mesh _wireframeMesh;
        private Mesh _pointMesh;
        private bool _wireframeMeshDirty = true;
        private bool _pointMeshDirty = true;
        
        // キャッシュ用
        private MeshObject _cached3DMeshObject;
        private HashSet<int> _cachedSelectedVertices;
        private HashSet<int> _cachedSelectedLines;
        private int _cachedHoverVertex = -1;
        
        // ================================================================
        // 初期化
        // ================================================================
        
        /// <summary>
        /// 3D描画用シェーダーを初期化
        /// </summary>
        public bool Initialize3D(Shader wireframe3DShader, Shader point3DShader)
        {
            if (wireframe3DShader == null)
            {
                Debug.LogWarning("MeshGPURenderer: Wireframe3D shader is null");
                return false;
            }
            
            if (point3DShader == null)
            {
                Debug.LogWarning("MeshGPURenderer: Point3D shader is null");
                return false;
            }
            
            _wireframe3DMaterial = new Material(wireframe3DShader) { hideFlags = HideFlags.HideAndDontSave };
            _point3DMaterial = new Material(point3DShader) { hideFlags = HideFlags.HideAndDontSave };
            
            return true;
        }
        
        /// <summary>
        /// 3Dリソースをクリーンアップ
        /// </summary>
        private void Cleanup3D()
        {
            if (_wireframe3DMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_wireframe3DMaterial);
                _wireframe3DMaterial = null;
            }
            if (_point3DMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_point3DMaterial);
                _point3DMaterial = null;
            }
            if (_wireframeMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_wireframeMesh);
                _wireframeMesh = null;
            }
            if (_pointMesh != null)
            {
                UnityEngine.Object.DestroyImmediate(_pointMesh);
                _pointMesh = null;
            }
        }
        
        // ================================================================
        // 3D描画メッシュ生成
        // ================================================================
        
        /// <summary>
        /// ワイヤフレーム用3Dメッシュを生成
        /// </summary>
        public void UpdateWireframeMesh3D(
            MeshObject meshObject, 
            MeshEdgeCache edgeCache,
            HashSet<int> selectedLines = null,
            float alpha = 1f,
            Matrix4x4? modelMatrix = null)
        {
            if (meshObject == null || edgeCache == null)
            {
                if (_wireframeMesh != null) _wireframeMesh.Clear();
                return;
            }
            
            Matrix4x4 matrix = modelMatrix ?? Matrix4x4.identity;
            
            // 可視性データを取得
            float[] lineVisibility = GetLineVisibility();
            
            // メッシュ生成
            if (_wireframeMesh == null)
            {
                _wireframeMesh = new Mesh();
                _wireframeMesh.name = "WireframeMesh3D";
                _wireframeMesh.hideFlags = HideFlags.HideAndDontSave;
            }
            else
            {
                _wireframeMesh.Clear();
            }
            
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var indices = new List<int>();
            
            var lines = edgeCache.Lines;
            
            Color normalColor = new Color(0f, 1f, 0.5f, 0.9f * alpha);
            Color selectedColor = new Color(0f, 1f, 1f, 1f * alpha);
            Color auxColor = new Color(1f, 0.3f, 1f, 0.9f * alpha);
            
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                
                // 可視性チェック
                if (lineVisibility != null && i < lineVisibility.Length && lineVisibility[i] < 0.5f)
                    continue;
                
                // 頂点インデックスチェック
                if (line.V1 < 0 || line.V1 >= meshObject.VertexCount ||
                    line.V2 < 0 || line.V2 >= meshObject.VertexCount)
                    continue;
                
                // モデル行列で変換
                Vector3 p1 = matrix.MultiplyPoint3x4(meshObject.Vertices[line.V1].Position);
                Vector3 p2 = matrix.MultiplyPoint3x4(meshObject.Vertices[line.V2].Position);
                
                // 色決定
                Color lineColor;
                if (selectedLines != null && selectedLines.Contains(i))
                {
                    lineColor = selectedColor;
                }
                else if (line.LineType == 1) // 補助線
                {
                    lineColor = auxColor;
                }
                else
                {
                    lineColor = normalColor;
                }
                
                int baseIdx = vertices.Count;
                vertices.Add(p1);
                vertices.Add(p2);
                colors.Add(lineColor);
                colors.Add(lineColor);
                indices.Add(baseIdx);
                indices.Add(baseIdx + 1);
            }
            
            _wireframeMesh.SetVertices(vertices);
            _wireframeMesh.SetColors(colors);
            _wireframeMesh.SetIndices(indices, MeshTopology.Lines, 0);
        }
        
        /// <summary>
        /// 頂点用3Dメッシュを生成（ビルボードクワッド）
        /// </summary>
        public void UpdatePointMesh3D(
            MeshObject meshObject,
            Camera camera,
            HashSet<int> selectedVertices = null,
            int hoverVertex = -1,
            float pointSize = 0.02f,
            float alpha = 1f,
            Matrix4x4? modelMatrix = null)
        {
            if (meshObject == null || camera == null)
            {
                if (_pointMesh != null) _pointMesh.Clear();
                return;
            }
            
            Matrix4x4 matrix = modelMatrix ?? Matrix4x4.identity;
            
            // 可視性データを取得
            float[] vertexVisibility = GetVertexVisibility();
            
            // メッシュ生成
            if (_pointMesh == null)
            {
                _pointMesh = new Mesh();
                _pointMesh.name = "PointMesh3D";
                _pointMesh.hideFlags = HideFlags.HideAndDontSave;
            }
            else
            {
                _pointMesh.Clear();
            }
            
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var uvs = new List<Vector2>();
            var indices = new List<int>();
            
            // カメラの右/上ベクトル（ビルボード用）
            Vector3 camRight = camera.transform.right;
            Vector3 camUp = camera.transform.up;
            
            float halfSize = pointSize * 0.5f;
            
            for (int i = 0; i < meshObject.VertexCount; i++)
            {
                // ホバー頂点はカリングをスキップ
                bool isHover = (i == hoverVertex);
                
                // 可視性チェック（ホバー頂点以外）
                if (!isHover && vertexVisibility != null && i < vertexVisibility.Length && vertexVisibility[i] < 0.5f)
                    continue;
                
                // モデル行列で変換
                Vector3 center = matrix.MultiplyPoint3x4(meshObject.Vertices[i].Position);
                
                // 選択状態をcolor.aにエンコード
                // 1.0 = selected, 0.5 = normal, 0.0 = hover
                float selectState;
                if (i == hoverVertex)
                {
                    selectState = 0f; // hover
                }
                else if (selectedVertices != null && selectedVertices.Contains(i))
                {
                    selectState = 1f; // selected
                }
                else
                {
                    selectState = 0.5f; // normal
                }
                
                Color col = new Color(1, 1, 1, selectState);
                
                // クワッドの4頂点
                Vector3 offset1 = (-camRight - camUp) * halfSize;
                Vector3 offset2 = ( camRight - camUp) * halfSize;
                Vector3 offset3 = (-camRight + camUp) * halfSize;
                Vector3 offset4 = ( camRight + camUp) * halfSize;
                
                int baseIdx = vertices.Count;
                
                vertices.Add(center + offset1);
                vertices.Add(center + offset2);
                vertices.Add(center + offset3);
                vertices.Add(center + offset4);
                
                colors.Add(col);
                colors.Add(col);
                colors.Add(col);
                colors.Add(col);
                
                uvs.Add(new Vector2(0, 0));
                uvs.Add(new Vector2(1, 0));
                uvs.Add(new Vector2(0, 1));
                uvs.Add(new Vector2(1, 1));
                
                // 2つの三角形
                indices.Add(baseIdx);
                indices.Add(baseIdx + 1);
                indices.Add(baseIdx + 2);
                indices.Add(baseIdx + 2);
                indices.Add(baseIdx + 1);
                indices.Add(baseIdx + 3);
            }
            
            _pointMesh.SetVertices(vertices);
            _pointMesh.SetColors(colors);
            _pointMesh.SetUVs(0, uvs);
            _pointMesh.SetTriangles(indices, 0);
        }
        
        // ================================================================
        // 3D描画
        // ================================================================
        
        // 描画用の一時メッシュリスト（フレーム後にクリア）
        private List<Mesh> _pendingMeshes = new List<Mesh>();
        private List<Material> _pendingMaterials = new List<Material>();
        
        /// <summary>
        /// ワイヤフレームを描画キューに追加
        /// </summary>
        public void QueueWireframe3D()
        {
            // 通常のワイヤフレーム
            if (_wireframeMesh != null && _wireframe3DMaterial != null && _wireframeMesh.vertexCount > 0)
            {
                var meshCopy = UnityEngine.Object.Instantiate(_wireframeMesh);
                meshCopy.hideFlags = HideFlags.HideAndDontSave;
                _pendingMeshes.Add(meshCopy);
                _pendingMaterials.Add(_wireframe3DMaterial);
            }
        }
        
        /// <summary>
        /// 頂点を描画キューに追加
        /// </summary>
        public void QueuePoints3D()
        {
            if (_pointMesh == null || _point3DMaterial == null)
                return;
            
            if (_pointMesh.vertexCount == 0)
                return;
            
            // メッシュをコピー
            var meshCopy = UnityEngine.Object.Instantiate(_pointMesh);
            meshCopy.hideFlags = HideFlags.HideAndDontSave;
            _pendingMeshes.Add(meshCopy);
            _pendingMaterials.Add(_point3DMaterial);
        }
        
        /// <summary>
        /// キューに入っているメッシュをPreviewRenderUtilityで描画
        /// </summary>
        public void DrawQueued3D(PreviewRenderUtility preview)
        {
            if (preview == null) return;
            
            for (int i = 0; i < _pendingMeshes.Count; i++)
            {
                var mesh = _pendingMeshes[i];
                var material = _pendingMaterials[i];
                if (mesh != null && material != null)
                {
                    preview.DrawMesh(mesh, Matrix4x4.identity, material, 0);
                }
            }
        }
        
        /// <summary>
        /// 3D描画後のクリーンアップ
        /// </summary>
        public void CleanupQueued3D()
        {
            // 一時メッシュを破棄
            foreach (var mesh in _pendingMeshes)
            {
                if (mesh != null)
                {
                    UnityEngine.Object.DestroyImmediate(mesh);
                }
            }
            _pendingMeshes.Clear();
            _pendingMaterials.Clear();
        }
        
        // 旧API互換用（削除予定）
        public void DrawWireframe3D(Camera camera) => QueueWireframe3D();
        public void DrawPoints3D(Camera camera) => QueuePoints3D();
        public void CleanupAfterRender(Camera camera) => CleanupQueued3D();
        
        // ================================================================
        // ミラーワイヤフレーム3D描画
        // ================================================================
        
        private Mesh _mirrorWireframeMesh;
        
        /// <summary>
        /// ミラーワイヤフレーム用3Dメッシュを生成
        /// 事前にDispatchCompute(modelMatrix: combinedMatrix, isMirrored: true)を呼んでおくこと
        /// </summary>
        /// <param name="displayMatrix">表示用トランスフォーム行列（nullならidentity）</param>
        public void UpdateMirrorWireframeMesh3D(
            MeshObject meshObject, 
            MeshEdgeCache edgeCache,
            Matrix4x4 mirrorMatrix,
            float alpha = 1f,
            bool useCulling = true,
            Matrix4x4? displayMatrix = null)
        {
            if (meshObject == null || edgeCache == null)
            {
                if (_mirrorWireframeMesh != null) _mirrorWireframeMesh.Clear();
                return;
            }
            
            // 合成行列: displayMatrix * mirrorMatrix
            Matrix4x4 combinedMatrix = (displayMatrix ?? Matrix4x4.identity) * mirrorMatrix;
            
            // 可視性データを取得（DispatchComputeで計算済み）
            float[] lineVisibility = useCulling ? GetLineVisibility() : null;
            
            // メッシュ生成
            if (_mirrorWireframeMesh == null)
            {
                _mirrorWireframeMesh = new Mesh();
                _mirrorWireframeMesh.name = "MirrorWireframeMesh3D";
                _mirrorWireframeMesh.hideFlags = HideFlags.HideAndDontSave;
            }
            else
            {
                _mirrorWireframeMesh.Clear();
            }
            
            var vertices = new List<Vector3>();
            var colors = new List<Color>();
            var indices = new List<int>();
            
            var lines = edgeCache.Lines;
            Color normalColor = new Color(0f, 0.8f, 0.8f, 0.7f * alpha);  // シアン
            Color auxColor = new Color(1f, 0.5f, 0.8f, 0.7f * alpha);     // ピンク
            
            for (int i = 0; i < lines.Count; i++)
            {
                var line = lines[i];
                
                // 可視性チェック（カリング有効時）
                if (lineVisibility != null && i < lineVisibility.Length && lineVisibility[i] < 0.5f)
                    continue;
                
                // 頂点インデックスチェック
                if (line.V1 < 0 || line.V1 >= meshObject.VertexCount ||
                    line.V2 < 0 || line.V2 >= meshObject.VertexCount)
                    continue;
                
                // 合成行列で変換（ミラー + 表示トランスフォーム）
                Vector3 p1 = combinedMatrix.MultiplyPoint3x4(meshObject.Vertices[line.V1].Position);
                Vector3 p2 = combinedMatrix.MultiplyPoint3x4(meshObject.Vertices[line.V2].Position);
                
                // 色決定
                Color lineColor = (line.LineType == 1) ? auxColor : normalColor;
                
                int baseIdx = vertices.Count;
                vertices.Add(p1);
                vertices.Add(p2);
                colors.Add(lineColor);
                colors.Add(lineColor);
                indices.Add(baseIdx);
                indices.Add(baseIdx + 1);
            }
            
            _mirrorWireframeMesh.SetVertices(vertices);
            _mirrorWireframeMesh.SetColors(colors);
            _mirrorWireframeMesh.SetIndices(indices, MeshTopology.Lines, 0);
        }
        
        /// <summary>
        /// ミラーワイヤフレームを描画キューに追加
        /// </summary>
        public void QueueMirrorWireframe3D()
        {
            if (_mirrorWireframeMesh == null || _wireframe3DMaterial == null)
                return;
            
            if (_mirrorWireframeMesh.vertexCount == 0)
                return;
            
            // メッシュをコピー
            var meshCopy = UnityEngine.Object.Instantiate(_mirrorWireframeMesh);
            meshCopy.hideFlags = HideFlags.HideAndDontSave;
            _pendingMeshes.Add(meshCopy);
            _pendingMaterials.Add(_wireframe3DMaterial);
        }
        
        // ================================================================
        // ユーティリティ
        // ================================================================
        
        /// <summary>
        /// 3Dメッシュを無効化（トポロジー変更時）
        /// </summary>
        public void Invalidate3DMeshes()
        {
            _wireframeMeshDirty = true;
            _pointMeshDirty = true;
        }
        
        /// <summary>
        /// 3D描画が利用可能か
        /// </summary>
        public bool Is3DAvailable => _wireframe3DMaterial != null && _point3DMaterial != null;
    }
}
