// Assets/Editor/UndoSystem/MeshEditor/Context/MeshEditContext.cs
// メッシュ編集コンテキスト
// Undo/Redo操作の対象となるデータを保持

using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using MeshFactory.Data;
using MeshFactory.Tools;
using MeshFactory.Selection;

namespace MeshFactory.UndoSystem
{
    /// <summary>
    /// メッシュ編集コンテキスト
    /// Undo/Redo操作の対象となるデータを保持
    /// MeshDataベースの新構造
    /// </summary>
    public class MeshEditContext
    {
        // === メッシュデータ（新構造） ===

        /// <summary>メッシュデータ本体</summary>
        public MeshData MeshData;

        // === 選択状態 ===

        /// <summary>選択中の頂点インデックス</summary>
        public HashSet<int> SelectedVertices;

        /// <summary>選択中の面インデックス</summary>
        public HashSet<int> SelectedFaces;

        // === 元データ参照（リセット用） ===

        /// <summary>元の頂点位置（リセット用）</summary>
        public Vector3[] OriginalPositions;

        // === Unity UnityMesh 参照 ===

        /// <summary>実際のMeshオブジェクト</summary>
        public Mesh TargetMesh;

        // === WorkPlane参照（選択連動Undo用） ===

        /// <summary>WorkPlane参照（選択と連動してUndo/Redoするため）</summary>
        public WorkPlane WorkPlane;

        // === 拡張選択システム用 ===

        /// <summary>Undo/Redo時に復元すべきSelectionSnapshot</summary>
        public SelectionSnapshot CurrentSelectionSnapshot;

        // === マテリアル（マルチマテリアル対応） ===

        /// <summary>マテリアルリスト（MeshContext.Materialsと同期）</summary>
        public List<Material> Materials = new List<Material> { null };

        /// <summary>現在選択中のマテリアルインデックス</summary>
        public int CurrentMaterialIndex = 0;

        // === デフォルトマテリアル（グローバル設定） ===

        /// <summary>新規メッシュ作成時に適用されるデフォルトマテリアルリスト</summary>
        public List<Material> DefaultMaterials = new List<Material> { null };

        /// <summary>新規メッシュ作成時に適用されるデフォルトカレントマテリアルインデックス</summary>
        public int DefaultCurrentMaterialIndex = 0;

        /// <summary>マテリアル変更時に自動でデフォルトに設定するか</summary>
        public bool AutoSetDefaultMaterials = true;

        // === 後方互換プロパティ ===

        /// <summary>頂点位置リスト（後方互換）</summary>
        public List<Vector3> Vertices
        {
            get => MeshData?.Vertices.Select(v => v.Position).ToList() ?? new List<Vector3>();
            set
            {
                if (MeshData == null) return;
                for (int i = 0; i < value.Count && i < MeshData.Vertices.Count; i++)
                {
                    MeshData.Vertices[i].Position = value[i];
                }
            }
        }

        /// <summary>元の頂点位置（後方互換）</summary>
        public Vector3[] OriginalVertices
        {
            get => OriginalPositions;
            set => OriginalPositions = value;
        }

        // === コンストラクタ ===

        public MeshEditContext()
        {
            MeshData = new MeshData();
            SelectedVertices = new HashSet<int>();
            SelectedFaces = new HashSet<int>();
            Materials = new List<Material> { null };
            CurrentMaterialIndex = 0;
            DefaultMaterials = new List<Material> { null };
            DefaultCurrentMaterialIndex = 0;
            AutoSetDefaultMaterials = true;
        }

        // === メッシュ読み込み/適用 ===

        /// <summary>
        /// Meshからデータを読み込む
        /// </summary>
        public void LoadFromMesh(Mesh mesh, bool mergeVertices = true)
        {
            if (mesh == null) return;

            TargetMesh = mesh;
            MeshData = new MeshData();
            MeshData.FromUnityMesh(mesh, mergeVertices);

            // 元の位置を保存
            OriginalPositions = MeshData.Vertices.Select(v => v.Position).ToArray();

            // 選択クリア
            SelectedVertices.Clear();
            SelectedFaces.Clear();
        }

        /// <summary>
        /// データをMeshに適用
        /// </summary>
        public void ApplyToMesh()
        {
            if (TargetMesh == null || MeshData == null) return;

            // MeshDataをUnity Meshに変換して適用
            var newMesh = MeshData.ToUnityMesh();

            TargetMesh.Clear();
            TargetMesh.vertices = newMesh.vertices;
            TargetMesh.triangles = newMesh.triangles;
            TargetMesh.uv = newMesh.uv;
            TargetMesh.normals = newMesh.normals;
            TargetMesh.RecalculateBounds();

            // 一時メッシュを破棄
            Object.DestroyImmediate(newMesh);
        }

        /// <summary>
        /// 頂点位置のみをMeshに適用（高速）
        /// </summary>
        public void ApplyVertexPositionsToMesh()
        {
            if (TargetMesh == null || MeshData == null) return;

            // Unity Meshに変換して頂点位置を更新
            var newMesh = MeshData.ToUnityMesh();
            TargetMesh.vertices = newMesh.vertices;
            TargetMesh.RecalculateNormals();
            TargetMesh.RecalculateBounds();

            Object.DestroyImmediate(newMesh);
        }

        // === 頂点操作ヘルパー ===

        /// <summary>
        /// 頂点位置を取得
        /// </summary>
        public Vector3 GetVertexPosition(int index)
        {
            if (MeshData == null || index < 0 || index >= MeshData.VertexCount)
                return Vector3.zero;
            return MeshData.Vertices[index].Position;
        }

        /// <summary>
        /// 頂点位置を設定
        /// </summary>
        public void SetVertexPosition(int index, Vector3 position)
        {
            if (MeshData == null || index < 0 || index >= MeshData.VertexCount)
                return;
            MeshData.Vertices[index].Position = position;
        }

        /// <summary>
        /// 全頂点位置を配列で取得
        /// </summary>
        public Vector3[] GetAllPositions()
        {
            if (MeshData == null) return new Vector3[0];
            return MeshData.Vertices.Select(v => v.Position).ToArray();
        }

        /// <summary>
        /// 全頂点位置を配列で設定
        /// </summary>
        public void SetAllPositions(Vector3[] positions)
        {
            if (MeshData == null) return;
            for (int i = 0; i < positions.Length && i < MeshData.VertexCount; i++)
            {
                MeshData.Vertices[i].Position = positions[i];
            }
        }

        /// <summary>
        /// 頂点数を取得
        /// </summary>
        public int VertexCount => MeshData?.VertexCount ?? 0;

        /// <summary>
        /// 面数を取得
        /// </summary>
        public int FaceCount => MeshData?.FaceCount ?? 0;
    }
}
