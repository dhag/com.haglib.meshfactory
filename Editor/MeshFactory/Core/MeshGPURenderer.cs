// Assets/Editor/MeshFactory/Rendering/MeshGPURenderer.cs
// GPU描画管理クラス
// Compute Shader + DrawProceduralでメッシュエディタの描画を高速化

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using MeshFactory.Data;

namespace MeshFactory.Rendering
{
    /// <summary>
    /// GPU描画管理クラス
    /// </summary>
    public class MeshGPURenderer : IDisposable
    {
        // ================================================================
        // Compute Shader
        // ================================================================

        private ComputeShader _computeShader;
        private int _kernelClear;
        private int _kernelScreenPos;
        private int _kernelFaceVisibility;
        private int _kernelLineVisibility;

        // ================================================================
        // 描画用マテリアル
        // ================================================================

        private Material _pointMaterial;
        private Material _lineMaterial;

        // ================================================================
        // GPU バッファ
        // ================================================================

        // 入力バッファ（メッシュ変更時に更新）
        private ComputeBuffer _positionBuffer;
        private ComputeBuffer _lineBuffer;
        private ComputeBuffer _faceVertexIndexBuffer;
        private ComputeBuffer _faceVertexOffsetBuffer;
        private ComputeBuffer _faceVertexCountBuffer;

        // 出力バッファ（毎フレーム更新）
        private ComputeBuffer _screenPositionBuffer;
        private ComputeBuffer _vertexVisibilityBuffer;
        private ComputeBuffer _faceVisibilityBuffer;
        private ComputeBuffer _lineVisibilityBuffer;

        // 選択状態バッファ
        private ComputeBuffer _selectionBuffer;
        private uint[] _selectionData;

        // ================================================================
        // 状態
        // ================================================================

        private bool _initialized;
        private int _vertexCount;
        private int _faceCount;
        private int _lineCount;
        private MeshData _cachedMeshData;

        // ================================================================
        // 公開プロパティ
        // ================================================================

        /// <summary>GPUレンダリングが利用可能か</summary>
        public bool IsAvailable => _initialized && SystemInfo.supportsComputeShaders;

        /// <summary>現在のカリングが有効か</summary>
        public bool CullingEnabled { get; set; } = true;

        // ================================================================
        // 初期化
        // ================================================================

        /// <summary>
        /// 初期化
        /// </summary>
        /// <param name="computeShader">Compute2D_GPU.compute</param>
        /// <param name="pointShader">MeshFactory/Point シェーダー</param>
        /// <param name="lineShader">MeshFactory/Line シェーダー</param>
        public bool Initialize(ComputeShader computeShader, Shader pointShader, Shader lineShader)
        {
            if (_initialized)
                return true;

            if (!SystemInfo.supportsComputeShaders)
            {
                Debug.LogWarning("MeshGPURenderer: Compute Shaders not supported");
                return false;
            }

            if (computeShader == null || pointShader == null || lineShader == null)
            {
                Debug.LogWarning("MeshGPURenderer: Missing shaders");
                return false;
            }

            _computeShader = computeShader;

            // カーネル取得
            _kernelClear = _computeShader.FindKernel("ClearBuffers");
            _kernelScreenPos = _computeShader.FindKernel("ComputeScreenPositions");
            _kernelFaceVisibility = _computeShader.FindKernel("ComputeFaceVisibility");
            _kernelLineVisibility = _computeShader.FindKernel("ComputeLineVisibility");

            // マテリアル作成
            _pointMaterial = new Material(pointShader);
            _pointMaterial.hideFlags = HideFlags.HideAndDontSave;

            _lineMaterial = new Material(lineShader);
            _lineMaterial.hideFlags = HideFlags.HideAndDontSave;

            _initialized = true;
            return true;
        }

        // ================================================================
        // バッファ更新（メッシュ変更時）
        // ================================================================

        /// <summary>
        /// メッシュデータからGPUバッファを構築
        /// </summary>
        public void UpdateBuffers(MeshData meshData, MeshEdgeCache edgeCache)
        {
            if (!_initialized || meshData == null || edgeCache == null)
                return;

            // 同じメッシュなら更新不要
            if (_cachedMeshData == meshData && _vertexCount == meshData.VertexCount)
                return;

            _cachedMeshData = meshData;

            // 既存バッファを解放
            ReleaseBuffers();

            _vertexCount = meshData.VertexCount;
            _faceCount = meshData.FaceCount;

            if (_vertexCount == 0)
                return;

            // エッジキャッシュを更新
            edgeCache.Update(meshData, force: true);
            _lineCount = edgeCache.LineCount;

            // === 頂点バッファ ===
            var positions = new Vector3[_vertexCount];
            for (int i = 0; i < _vertexCount; i++)
            {
                positions[i] = meshData.Vertices[i].Position;
            }
            _positionBuffer = new ComputeBuffer(_vertexCount, sizeof(float) * 3);
            _positionBuffer.SetData(positions);

            // === 線分バッファ ===
            if (_lineCount > 0)
            {
                _lineBuffer = new ComputeBuffer(_lineCount, LineData.SizeInBytes);
                _lineBuffer.SetData(edgeCache.Lines);
            }

            // === 面データバッファ ===
            BuildFaceBuffers(meshData);

            // === 出力バッファ ===
            _screenPositionBuffer = new ComputeBuffer(_vertexCount, sizeof(float) * 4);
            _vertexVisibilityBuffer = new ComputeBuffer(_vertexCount, sizeof(float));

            if (_faceCount > 0)
            {
                _faceVisibilityBuffer = new ComputeBuffer(_faceCount, sizeof(float));
            }

            if (_lineCount > 0)
            {
                _lineVisibilityBuffer = new ComputeBuffer(_lineCount, sizeof(float));
            }

            // === 選択バッファ ===
            _selectionBuffer = new ComputeBuffer(_vertexCount, sizeof(uint));
            _selectionData = new uint[_vertexCount];
        }

        private void BuildFaceBuffers(MeshData meshData)
        {
            if (_faceCount == 0)
                return;

            var indices = new List<int>();
            var offsets = new int[_faceCount];
            var counts = new int[_faceCount];

            int offset = 0;
            for (int f = 0; f < _faceCount; f++)
            {
                var face = meshData.Faces[f];
                offsets[f] = offset;
                counts[f] = face.VertexCount;

                foreach (int vIdx in face.VertexIndices)
                {
                    indices.Add(vIdx);
                }
                offset += face.VertexCount;
            }

            if (indices.Count > 0)
            {
                _faceVertexIndexBuffer = new ComputeBuffer(indices.Count, sizeof(int));
                _faceVertexIndexBuffer.SetData(indices.ToArray());
            }

            _faceVertexOffsetBuffer = new ComputeBuffer(_faceCount, sizeof(int));
            _faceVertexOffsetBuffer.SetData(offsets);

            _faceVertexCountBuffer = new ComputeBuffer(_faceCount, sizeof(int));
            _faceVertexCountBuffer.SetData(counts);
        }

        // ================================================================
        // 選択状態更新
        // ================================================================

        /// <summary>
        /// 選択頂点を更新
        /// </summary>
        public void UpdateSelection(HashSet<int> selectedVertices)
        {
            if (!_initialized || _selectionBuffer == null || _selectionData == null)
                return;

            // クリア
            Array.Clear(_selectionData, 0, _selectionData.Length);

            // 選択頂点をマーク
            if (selectedVertices != null)
            {
                foreach (int idx in selectedVertices)
                {
                    if (idx >= 0 && idx < _selectionData.Length)
                    {
                        _selectionData[idx] = 1;
                    }
                }
            }

            _selectionBuffer.SetData(_selectionData);
        }

        // ================================================================
        // Compute Shader 実行
        // ================================================================

        /// <summary>
        /// Compute Shader を実行（カリング計算）
        /// </summary>
        public void DispatchCompute(Matrix4x4 mvp, Rect previewRect)
        {
            if (!_initialized || _vertexCount == 0)
                return;

            // 共通パラメータ
            _computeShader.SetMatrix("_MATRIX_MVP", mvp);
            _computeShader.SetVector("_ScreenParams",
                new Vector4(previewRect.width, previewRect.height, 0, 0));
            _computeShader.SetInt("_VertexCount", _vertexCount);
            _computeShader.SetInt("_FaceCount", _faceCount);
            _computeShader.SetInt("_LineCount", _lineCount);

            int threadGroups64(int count) => Mathf.CeilToInt(count / 64f);

            // Pass 0: クリア
            SetBufferSafe(_kernelClear, "_VertexVisibilityBuffer", _vertexVisibilityBuffer);
            SetBufferSafe(_kernelClear, "_ScreenPositionBuffer", _screenPositionBuffer);
            SetBufferSafe(_kernelClear, "_FaceVisibilityBuffer", _faceVisibilityBuffer);
            SetBufferSafe(_kernelClear, "_LineVisibilityBuffer", _lineVisibilityBuffer);

            int maxCount = Mathf.Max(_vertexCount, Mathf.Max(_faceCount, _lineCount));
            _computeShader.Dispatch(_kernelClear, threadGroups64(maxCount), 1, 1);

            // Pass 1: スクリーン座標計算
            SetBufferSafe(_kernelScreenPos, "_PositionBuffer", _positionBuffer);
            SetBufferSafe(_kernelScreenPos, "_ScreenPositionBuffer", _screenPositionBuffer);
            _computeShader.Dispatch(_kernelScreenPos, threadGroups64(_vertexCount), 1, 1);

            if (CullingEnabled && _faceCount > 0)
            {
                // Pass 2: 面の表裏判定
                SetBufferSafe(_kernelFaceVisibility, "_ScreenPositionBuffer", _screenPositionBuffer);
                SetBufferSafe(_kernelFaceVisibility, "_FaceVertexIndexBuffer", _faceVertexIndexBuffer);
                SetBufferSafe(_kernelFaceVisibility, "_FaceVertexOffsetBuffer", _faceVertexOffsetBuffer);
                SetBufferSafe(_kernelFaceVisibility, "_FaceVertexCountBuffer", _faceVertexCountBuffer);
                SetBufferSafe(_kernelFaceVisibility, "_VertexVisibilityBuffer", _vertexVisibilityBuffer);
                SetBufferSafe(_kernelFaceVisibility, "_FaceVisibilityBuffer", _faceVisibilityBuffer);
                _computeShader.Dispatch(_kernelFaceVisibility, threadGroups64(_faceCount), 1, 1);

                // Pass 3: 線分の可視性
                if (_lineCount > 0)
                {
                    SetBufferSafe(_kernelLineVisibility, "_LineBuffer", _lineBuffer);
                    SetBufferSafe(_kernelLineVisibility, "_FaceVisibilityBuffer", _faceVisibilityBuffer);
                    SetBufferSafe(_kernelLineVisibility, "_LineVisibilityBuffer", _lineVisibilityBuffer);
                    _computeShader.Dispatch(_kernelLineVisibility, threadGroups64(_lineCount), 1, 1);
                }
            }
            else
            {
                // カリング無効時は全て表示
                // VertexVisibilityBufferを全て1にする処理が必要だが、
                // ここでは簡略化してフラグメントシェーダーで対応
            }
        }

        private void SetBufferSafe(int kernel, string name, ComputeBuffer buffer)
        {
            if (buffer != null)
            {
                _computeShader.SetBuffer(kernel, name, buffer);
            }
        }

        // ================================================================
        // 描画
        // ================================================================

        /// <summary>
        /// 頂点を描画
        /// </summary>
        public void DrawPoints(Rect previewRect, float pointSize = 8f)
        {
            if (!_initialized || _vertexCount == 0 || _pointMaterial == null)
                return;

            _pointMaterial.SetBuffer("_ScreenPositionBuffer", _screenPositionBuffer);
            _pointMaterial.SetBuffer("_VertexVisibilityBuffer", _vertexVisibilityBuffer);
            _pointMaterial.SetBuffer("_SelectionBuffer", _selectionBuffer);
            _pointMaterial.SetVector("_MeshFactoryScreenSize", new Vector2(previewRect.width, previewRect.height));
            _pointMaterial.SetFloat("_PointSize", pointSize);

            // 1頂点 = 1インスタンス、6頂点/インスタンス (2 triangles)
            Graphics.DrawProcedural(
                _pointMaterial,
                new Bounds(Vector3.zero, Vector3.one * 10000),
                MeshTopology.Triangles,
                6,              // 頂点数/インスタンス
                _vertexCount    // インスタンス数
            );
        }

        /// <summary>
        /// 線分を描画
        /// </summary>
        public void DrawLines(Rect previewRect, float lineWidth = 2f)
        {
            if (!_initialized || _lineCount == 0 || _lineMaterial == null)
                return;

            _lineMaterial.SetBuffer("_ScreenPositionBuffer", _screenPositionBuffer);
            _lineMaterial.SetBuffer("_LineBuffer", _lineBuffer);
            _lineMaterial.SetBuffer("_LineVisibilityBuffer", _lineVisibilityBuffer);
            _lineMaterial.SetVector("_MeshFactoryScreenSize", new Vector2(previewRect.width, previewRect.height));
            _lineMaterial.SetFloat("_LineWidth", lineWidth);

            // 1線分 = 1インスタンス、6頂点/インスタンス (2 triangles)
            Graphics.DrawProcedural(
                _lineMaterial,
                new Bounds(Vector3.zero, Vector3.one * 10000),
                MeshTopology.Triangles,
                6,              // 頂点数/インスタンス
                _lineCount      // インスタンス数
            );
        }

        // ================================================================
        // クリーンアップ
        // ================================================================

        private void ReleaseBuffers()
        {
            _positionBuffer?.Release();
            _lineBuffer?.Release();
            _faceVertexIndexBuffer?.Release();
            _faceVertexOffsetBuffer?.Release();
            _faceVertexCountBuffer?.Release();
            _screenPositionBuffer?.Release();
            _vertexVisibilityBuffer?.Release();
            _faceVisibilityBuffer?.Release();
            _lineVisibilityBuffer?.Release();
            _selectionBuffer?.Release();

            _positionBuffer = null;
            _lineBuffer = null;
            _faceVertexIndexBuffer = null;
            _faceVertexOffsetBuffer = null;
            _faceVertexCountBuffer = null;
            _screenPositionBuffer = null;
            _vertexVisibilityBuffer = null;
            _faceVisibilityBuffer = null;
            _lineVisibilityBuffer = null;
            _selectionBuffer = null;

            _vertexCount = 0;
            _faceCount = 0;
            _lineCount = 0;
            _cachedMeshData = null;
        }

        /// <summary>
        /// バッファを無効化（メッシュ変更時）
        /// </summary>
        public void InvalidateBuffers()
        {
            _cachedMeshData = null;
        }

        /// <summary>
        /// リソースを解放
        /// </summary>
        public void Dispose()
        {
            ReleaseBuffers();

            if (_pointMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_pointMaterial);
                _pointMaterial = null;
            }

            if (_lineMaterial != null)
            {
                UnityEngine.Object.DestroyImmediate(_lineMaterial);
                _lineMaterial = null;
            }

            _initialized = false;
        }
    }
}
