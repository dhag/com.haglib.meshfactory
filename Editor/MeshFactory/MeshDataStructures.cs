// Assets/Editor/MeshData/MeshDataStructures.cs
// 頂点ベースのメッシュデータ構造
// - Vertex: 位置 + 複数UV + 複数法線
// - Face: N角形対応（三角形、四角形、Nゴン）
// - MeshData: Unity Mesh との相互変換

using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace MeshFactory.Data
{
    // ============================================================
    // Vertex クラス
    // ============================================================

    /// <summary>
    /// 頂点データ
    /// 位置と、複数のUV/法線を保持（シーム・ハードエッジ対応）
    /// </summary>
    [Serializable]
    public class Vertex
    {
        /// <summary>頂点位置</summary>
        public Vector3 Position;

        /// <summary>UV座標リスト（面から UVIndices で参照）</summary>
        public List<Vector2> UVs = new List<Vector2>();

        /// <summary>法線リスト（面から NormalIndices で参照）</summary>
        public List<Vector3> Normals = new List<Vector3>();

        // === コンストラクタ ===

        public Vertex()
        {
            Position = Vector3.zero;
        }

        public Vertex(Vector3 position)
        {
            Position = position;
        }

        public Vertex(Vector3 position, Vector2 uv)
        {
            Position = position;
            UVs.Add(uv);
        }

        public Vertex(Vector3 position, Vector2 uv, Vector3 normal)
        {
            Position = position;
            UVs.Add(uv);
            Normals.Add(normal);
        }

        // === ユーティリティ ===

        /// <summary>
        /// UVを追加し、インデックスを返す
        /// </summary>
        public int AddUV(Vector2 uv)
        {
            UVs.Add(uv);
            return UVs.Count - 1;
        }

        /// <summary>
        /// 法線を追加し、インデックスを返す
        /// </summary>
        public int AddNormal(Vector3 normal)
        {
            Normals.Add(normal);
            return Normals.Count - 1;
        }

        /// <summary>
        /// 同一UVが既にあればそのインデックス、なければ追加
        /// </summary>
        public int GetOrAddUV(Vector2 uv, float tolerance = 0.0001f)
        {
            for (int i = 0; i < UVs.Count; i++)
            {
                if (Vector2.Distance(UVs[i], uv) < tolerance)
                    return i;
            }
            return AddUV(uv);
        }

        /// <summary>
        /// 同一法線が既にあればそのインデックス、なければ追加
        /// </summary>
        public int GetOrAddNormal(Vector3 normal, float tolerance = 0.0001f)
        {
            for (int i = 0; i < Normals.Count; i++)
            {
                if (Vector3.Distance(Normals[i], normal) < tolerance)
                    return i;
            }
            return AddNormal(normal);
        }

        /// <summary>
        /// ディープコピー
        /// </summary>
        public Vertex Clone()
        {
            return new Vertex
            {
                Position = Position,
                UVs = new List<Vector2>(UVs),
                Normals = new List<Vector3>(Normals)
            };
        }
    }

    // ============================================================
    // Face クラス
    // ============================================================

    /// <summary>
    /// 面データ（N角形対応）
    /// 頂点インデックスと、各頂点のUV/法線サブインデックスを保持
    /// </summary>
    [Serializable]
    public class Face
    {
        /// <summary>頂点インデックスリスト（Vertex配列への参照）</summary>
        public List<int> VertexIndices = new List<int>();

        /// <summary>各頂点のUVサブインデックス（Vertex.UVs[n]への参照）</summary>
        public List<int> UVIndices = new List<int>();

        /// <summary>各頂点の法線サブインデックス（Vertex.Normals[n]への参照）</summary>
        public List<int> NormalIndices = new List<int>();

        // === プロパティ ===

        /// <summary>頂点数</summary>
        public int VertexCount => VertexIndices.Count;

        /// <summary>三角形か</summary>
        public bool IsTriangle => VertexCount == 3;

        /// <summary>四角形か</summary>
        public bool IsQuad => VertexCount == 4;

        /// <summary>有効な面か（3頂点以上）</summary>
        public bool IsValid => VertexCount >= 3;

        // === コンストラクタ ===

        public Face() { }

        /// <summary>
        /// 三角形を作成（UV/法線インデックスは全て0）
        /// </summary>
        public Face(int v0, int v1, int v2)
        {
            VertexIndices.AddRange(new[] { v0, v1, v2 });
            UVIndices.AddRange(new[] { 0, 0, 0 });
            NormalIndices.AddRange(new[] { 0, 0, 0 });
        }

        /// <summary>
        /// 四角形を作成（UV/法線インデックスは全て0）
        /// </summary>
        public Face(int v0, int v1, int v2, int v3)
        {
            VertexIndices.AddRange(new[] { v0, v1, v2, v3 });
            UVIndices.AddRange(new[] { 0, 0, 0, 0 });
            NormalIndices.AddRange(new[] { 0, 0, 0, 0 });
        }

        /// <summary>
        /// 完全指定で三角形を作成
        /// </summary>
        public static Face CreateTriangle(
            int v0, int v1, int v2,
            int uv0, int uv1, int uv2,
            int n0, int n1, int n2)
        {
            return new Face
            {
                VertexIndices = new List<int> { v0, v1, v2 },
                UVIndices = new List<int> { uv0, uv1, uv2 },
                NormalIndices = new List<int> { n0, n1, n2 }
            };
        }

        /// <summary>
        /// 完全指定で四角形を作成
        /// </summary>
        public static Face CreateQuad(
            int v0, int v1, int v2, int v3,
            int uv0, int uv1, int uv2, int uv3,
            int n0, int n1, int n2, int n3)
        {
            return new Face
            {
                VertexIndices = new List<int> { v0, v1, v2, v3 },
                UVIndices = new List<int> { uv0, uv1, uv2, uv3 },
                NormalIndices = new List<int> { n0, n1, n2, n3 }
            };
        }

        // === 三角形分解 ===

        /// <summary>
        /// 三角形インデックスに分解（扇形分割）
        /// </summary>
        /// <returns>三角形数 × 3 のインデックス配列</returns>
        public int[] ToTriangleIndices()
        {
            if (VertexCount < 3)
                return Array.Empty<int>();

            if (IsTriangle)
                return VertexIndices.ToArray();

            // 扇形分割: v0 を中心に (v0, v1, v2), (v0, v2, v3), ... 
            var result = new List<int>();
            for (int i = 1; i < VertexCount - 1; i++)
            {
                result.Add(VertexIndices[0]);
                result.Add(VertexIndices[i]);
                result.Add(VertexIndices[i + 1]);
            }
            return result.ToArray();
        }

        /// <summary>
        /// 三角形に分解してFaceリストを返す
        /// </summary>
        public List<Face> Triangulate()
        {
            var result = new List<Face>();

            if (VertexCount < 3)
                return result;

            if (IsTriangle)
            {
                result.Add(Clone());
                return result;
            }

            // 扇形分割
            for (int i = 1; i < VertexCount - 1; i++)
            {
                result.Add(Face.CreateTriangle(
                    VertexIndices[0], VertexIndices[i], VertexIndices[i + 1],
                    UVIndices[0], UVIndices[i], UVIndices[i + 1],
                    NormalIndices[0], NormalIndices[i], NormalIndices[i + 1]
                ));
            }

            return result;
        }

        /// <summary>
        /// 三角形数を取得
        /// </summary>
        public int TriangleCount => VertexCount >= 3 ? VertexCount - 2 : 0;

        // === ユーティリティ ===

        /// <summary>
        /// 面を反転（頂点順序を逆に）
        /// </summary>
        public void Flip()
        {
            VertexIndices.Reverse();
            UVIndices.Reverse();
            NormalIndices.Reverse();
        }

        /// <summary>
        /// ディープコピー
        /// </summary>
        public Face Clone()
        {
            return new Face
            {
                VertexIndices = new List<int>(VertexIndices),
                UVIndices = new List<int>(UVIndices),
                NormalIndices = new List<int>(NormalIndices)
            };
        }
    }

    // ============================================================
    // MeshData クラス
    // ============================================================

    /// <summary>
    /// メッシュデータ（頂点 + 面）
    /// Unity Mesh との相互変換機能付き
    /// </summary>
    [Serializable]
    public class MeshData
    {
        /// <summary>頂点リスト</summary>
        public List<Vertex> Vertices = new List<Vertex>();

        /// <summary>面リスト</summary>
        public List<Face> Faces = new List<Face>();

        /// <summary>メッシュ名</summary>
        public string Name = "Mesh";

        // === プロパティ ===

        /// <summary>頂点数</summary>
        public int VertexCount => Vertices.Count;

        /// <summary>面数</summary>
        public int FaceCount => Faces.Count;

        /// <summary>三角形数（全ての面を三角形化した場合）</summary>
        public int TriangleCount => Faces.Sum(f => f.TriangleCount);

        // === コンストラクタ ===

        public MeshData() { }

        public MeshData(string name)
        {
            Name = name;
        }

        // === 頂点操作 ===

        /// <summary>
        /// 頂点を追加
        /// </summary>
        public int AddVertex(Vector3 position)
        {
            Vertices.Add(new Vertex(position));
            return Vertices.Count - 1;
        }

        /// <summary>
        /// 頂点を追加（UV付き）
        /// </summary>
        public int AddVertex(Vector3 position, Vector2 uv)
        {
            Vertices.Add(new Vertex(position, uv));
            return Vertices.Count - 1;
        }

        /// <summary>
        /// 頂点を追加（UV + 法線付き）
        /// </summary>
        public int AddVertex(Vector3 position, Vector2 uv, Vector3 normal)
        {
            Vertices.Add(new Vertex(position, uv, normal));
            return Vertices.Count - 1;
        }

        // === 面操作 ===

        /// <summary>
        /// 三角形を追加
        /// </summary>
        public void AddTriangle(int v0, int v1, int v2)
        {
            Faces.Add(new Face(v0, v1, v2));
        }

        /// <summary>
        /// 四角形を追加
        /// </summary>
        public void AddQuad(int v0, int v1, int v2, int v3)
        {
            Faces.Add(new Face(v0, v1, v2, v3));
        }

        /// <summary>
        /// 面を追加
        /// </summary>
        public void AddFace(Face face)
        {
            Faces.Add(face);
        }

        // === Unity Mesh 変換 ===

        /// <summary>
        /// Unity Mesh に変換
        /// 全ての面を三角形に展開し、頂点を複製してUnity形式に変換
        /// </summary>
        public Mesh ToUnityMesh()
        {
            var mesh = new Mesh { name = Name };

            // Unity Mesh 用のリスト（展開後）
            var unityVertices = new List<Vector3>();
            var unityNormals = new List<Vector3>();
            var unityUVs = new List<Vector2>();
            var unityTriangles = new List<int>();

            foreach (var face in Faces)
            {
                if (!face.IsValid)
                    continue;

                // 三角形に分解
                var triangles = face.Triangulate();

                foreach (var tri in triangles)
                {
                    int startIdx = unityVertices.Count;

                    for (int i = 0; i < 3; i++)
                    {
                        int vIdx = tri.VertexIndices[i];
                        int uvIdx = tri.UVIndices[i];
                        int nIdx = tri.NormalIndices[i];

                        var vertex = Vertices[vIdx];

                        unityVertices.Add(vertex.Position);

                        // UV取得（範囲チェック）
                        if (uvIdx >= 0 && uvIdx < vertex.UVs.Count)
                            unityUVs.Add(vertex.UVs[uvIdx]);
                        else if (vertex.UVs.Count > 0)
                            unityUVs.Add(vertex.UVs[0]);
                        else
                            unityUVs.Add(Vector2.zero);

                        // 法線取得（範囲チェック）
                        if (nIdx >= 0 && nIdx < vertex.Normals.Count)
                            unityNormals.Add(vertex.Normals[nIdx]);
                        else if (vertex.Normals.Count > 0)
                            unityNormals.Add(vertex.Normals[0]);
                        else
                            unityNormals.Add(Vector3.zero);

                        unityTriangles.Add(startIdx + i);
                    }
                }
            }

            mesh.vertices = unityVertices.ToArray();
            mesh.uv = unityUVs.ToArray();
            mesh.triangles = unityTriangles.ToArray();

            // 法線: 有効なデータがあれば設定、なければ自動計算
            if (unityNormals.Any(n => n != Vector3.zero))
                mesh.normals = unityNormals.ToArray();
            else
                mesh.RecalculateNormals();

            mesh.RecalculateBounds();

            return mesh;
        }

        /// <summary>
        /// Unity Mesh から読み込み
        /// 三角形ベースで読み込み、同一位置の頂点をマージ
        /// </summary>
        public void FromUnityMesh(Mesh mesh, bool mergeVertices = true)
        {
            if (mesh == null)
                return;

            Clear();
            Name = mesh.name;

            var srcVertices = mesh.vertices;
            var srcUVs = mesh.uv;
            var srcNormals = mesh.normals;
            var srcTriangles = mesh.triangles;

            if (mergeVertices)
            {
                FromUnityMeshMerged(srcVertices, srcUVs, srcNormals, srcTriangles);
            }
            else
            {
                FromUnityMeshDirect(srcVertices, srcUVs, srcNormals, srcTriangles);
            }
        }

        /// <summary>
        /// 頂点マージしてインポート
        /// </summary>
        private void FromUnityMeshMerged(
            Vector3[] srcVertices, Vector2[] srcUVs, Vector3[] srcNormals, int[] srcTriangles)
        {
            // 位置でグループ化して頂点をマージ
            var positionToVertex = new Dictionary<Vector3, int>(new Vector3Comparer());
            var unityToLocalIndex = new int[srcVertices.Length];

            for (int i = 0; i < srcVertices.Length; i++)
            {
                Vector3 pos = srcVertices[i];

                if (!positionToVertex.TryGetValue(pos, out int localIdx))
                {
                    localIdx = Vertices.Count;
                    Vertices.Add(new Vertex(pos));
                    positionToVertex[pos] = localIdx;
                }

                unityToLocalIndex[i] = localIdx;

                // UV追加
                var vertex = Vertices[localIdx];
                Vector2 uv = (srcUVs != null && i < srcUVs.Length) ? srcUVs[i] : Vector2.zero;
                vertex.GetOrAddUV(uv);

                // 法線追加
                Vector3 normal = (srcNormals != null && i < srcNormals.Length) ? srcNormals[i] : Vector3.zero;
                if (normal != Vector3.zero)
                    vertex.GetOrAddNormal(normal);
            }

            // 三角形を面に変換
            for (int i = 0; i < srcTriangles.Length; i += 3)
            {
                int ui0 = srcTriangles[i];
                int ui1 = srcTriangles[i + 1];
                int ui2 = srcTriangles[i + 2];

                int v0 = unityToLocalIndex[ui0];
                int v1 = unityToLocalIndex[ui1];
                int v2 = unityToLocalIndex[ui2];

                // UV/法線インデックスを検索
                Vector2 uv0 = (srcUVs != null && ui0 < srcUVs.Length) ? srcUVs[ui0] : Vector2.zero;
                Vector2 uv1 = (srcUVs != null && ui1 < srcUVs.Length) ? srcUVs[ui1] : Vector2.zero;
                Vector2 uv2 = (srcUVs != null && ui2 < srcUVs.Length) ? srcUVs[ui2] : Vector2.zero;

                int uvIdx0 = FindUVIndex(Vertices[v0], uv0);
                int uvIdx1 = FindUVIndex(Vertices[v1], uv1);
                int uvIdx2 = FindUVIndex(Vertices[v2], uv2);

                Vector3 n0 = (srcNormals != null && ui0 < srcNormals.Length) ? srcNormals[ui0] : Vector3.zero;
                Vector3 n1 = (srcNormals != null && ui1 < srcNormals.Length) ? srcNormals[ui1] : Vector3.zero;
                Vector3 n2 = (srcNormals != null && ui2 < srcNormals.Length) ? srcNormals[ui2] : Vector3.zero;

                int nIdx0 = FindNormalIndex(Vertices[v0], n0);
                int nIdx1 = FindNormalIndex(Vertices[v1], n1);
                int nIdx2 = FindNormalIndex(Vertices[v2], n2);

                Faces.Add(Face.CreateTriangle(v0, v1, v2, uvIdx0, uvIdx1, uvIdx2, nIdx0, nIdx1, nIdx2));
            }
        }

        /// <summary>
        /// そのままインポート（頂点マージなし）
        /// </summary>
        private void FromUnityMeshDirect(
            Vector3[] srcVertices, Vector2[] srcUVs, Vector3[] srcNormals, int[] srcTriangles)
        {
            for (int i = 0; i < srcVertices.Length; i++)
            {
                var vertex = new Vertex(srcVertices[i]);

                if (srcUVs != null && i < srcUVs.Length)
                    vertex.UVs.Add(srcUVs[i]);
                else
                    vertex.UVs.Add(Vector2.zero);

                if (srcNormals != null && i < srcNormals.Length)
                    vertex.Normals.Add(srcNormals[i]);

                Vertices.Add(vertex);
            }

            for (int i = 0; i < srcTriangles.Length; i += 3)
            {
                int v0 = srcTriangles[i];
                int v1 = srcTriangles[i + 1];
                int v2 = srcTriangles[i + 2];

                Faces.Add(Face.CreateTriangle(v0, v1, v2, 0, 0, 0, 0, 0, 0));
            }
        }

        private int FindUVIndex(Vertex vertex, Vector2 uv, float tolerance = 0.0001f)
        {
            for (int i = 0; i < vertex.UVs.Count; i++)
            {
                if (Vector2.Distance(vertex.UVs[i], uv) < tolerance)
                    return i;
            }
            return 0;
        }

        private int FindNormalIndex(Vertex vertex, Vector3 normal, float tolerance = 0.0001f)
        {
            for (int i = 0; i < vertex.Normals.Count; i++)
            {
                if (Vector3.Distance(vertex.Normals[i], normal) < tolerance)
                    return i;
            }
            return 0;
        }

        // === ユーティリティ ===

        /// <summary>
        /// データをクリア
        /// </summary>
        public void Clear()
        {
            Vertices.Clear();
            Faces.Clear();
        }

        /// <summary>
        /// 全ての面の法線を自動計算
        /// </summary>
        public void RecalculateNormals()
        {
            // 各頂点の法線をクリア
            foreach (var vertex in Vertices)
            {
                vertex.Normals.Clear();
            }

            // 各面ごとに法線を計算
            foreach (var face in Faces)
            {
                if (face.VertexCount < 3)
                    continue;

                // 面法線を計算
                Vector3 v0 = Vertices[face.VertexIndices[0]].Position;
                Vector3 v1 = Vertices[face.VertexIndices[1]].Position;
                Vector3 v2 = Vertices[face.VertexIndices[2]].Position;
                Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                // 各頂点に法線を追加
                face.NormalIndices.Clear();
                for (int i = 0; i < face.VertexCount; i++)
                {
                    var vertex = Vertices[face.VertexIndices[i]];
                    int nIdx = vertex.GetOrAddNormal(faceNormal);
                    face.NormalIndices.Add(nIdx);
                }
            }
        }

        /// <summary>
        /// スムーズ法線を計算（同一頂点の法線を平均化）
        /// </summary>
        public void RecalculateSmoothNormals()
        {
            // まず面法線を計算
            var faceNormals = new Dictionary<int, List<Vector3>>();

            foreach (var face in Faces)
            {
                if (face.VertexCount < 3)
                    continue;

                Vector3 v0 = Vertices[face.VertexIndices[0]].Position;
                Vector3 v1 = Vertices[face.VertexIndices[1]].Position;
                Vector3 v2 = Vertices[face.VertexIndices[2]].Position;
                Vector3 faceNormal = Vector3.Cross(v1 - v0, v2 - v0).normalized;

                foreach (int vIdx in face.VertexIndices)
                {
                    if (!faceNormals.ContainsKey(vIdx))
                        faceNormals[vIdx] = new List<Vector3>();
                    faceNormals[vIdx].Add(faceNormal);
                }
            }

            // 各頂点に平均法線を設定
            foreach (var vertex in Vertices)
            {
                vertex.Normals.Clear();
            }

            foreach (var kvp in faceNormals)
            {
                var vertex = Vertices[kvp.Key];
                Vector3 avgNormal = Vector3.zero;
                foreach (var n in kvp.Value)
                    avgNormal += n;
                avgNormal = avgNormal.normalized;
                vertex.AddNormal(avgNormal);
            }

            // 面の法線インデックスを更新
            foreach (var face in Faces)
            {
                face.NormalIndices.Clear();
                for (int i = 0; i < face.VertexCount; i++)
                {
                    face.NormalIndices.Add(0);
                }
            }
        }

        /// <summary>
        /// ディープコピー
        /// </summary>
        public MeshData Clone()
        {
            var copy = new MeshData(Name);
            copy.Vertices = Vertices.Select(v => v.Clone()).ToList();
            copy.Faces = Faces.Select(f => f.Clone()).ToList();
            return copy;
        }

        /// <summary>
        /// バウンディングボックスを計算
        /// </summary>
        public Bounds CalculateBounds()
        {
            if (Vertices.Count == 0)
                return new Bounds(Vector3.zero, Vector3.zero);

            Vector3 min = Vertices[0].Position;
            Vector3 max = Vertices[0].Position;

            foreach (var vertex in Vertices)
            {
                min = Vector3.Min(min, vertex.Position);
                max = Vector3.Max(max, vertex.Position);
            }

            return new Bounds((min + max) * 0.5f, max - min);
        }

        /// <summary>
        /// デバッグ情報
        /// </summary>
        public string GetDebugInfo()
        {
            int triCount = Faces.Where(f => f.IsTriangle).Count();
            int quadCount = Faces.Where(f => f.IsQuad).Count();
            int nGonCount = Faces.Count - triCount - quadCount;

            return $"[{Name}] Vertices: {VertexCount}, Faces: {FaceCount} " +
                   $"(Tri: {triCount}, Quad: {quadCount}, NGon: {nGonCount})";
        }
    }

    // ============================================================
    // ヘルパークラス
    // ============================================================

    /// <summary>
    /// Vector3 比較用（Dictionary キー用）
    /// </summary>
    internal class Vector3Comparer : IEqualityComparer<Vector3>
    {
        private const float Tolerance = 0.00001f;

        public bool Equals(Vector3 a, Vector3 b)
        {
            return Vector3.Distance(a, b) < Tolerance;
        }

        public int GetHashCode(Vector3 v)
        {
            // 精度を落としてハッシュ化（近い値が同じハッシュになるように）
            int x = Mathf.RoundToInt(v.x * 10000);
            int y = Mathf.RoundToInt(v.y * 10000);
            int z = Mathf.RoundToInt(v.z * 10000);
            return x.GetHashCode() ^ (y.GetHashCode() << 2) ^ (z.GetHashCode() >> 2);
        }
    }
}
