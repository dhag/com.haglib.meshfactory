// PMXToMeshContext.cs - PMXモデルをMeshContextに変換
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXモデルをMeshContextに変換
    /// </summary>
    public static class PMXToMeshContext
    {
        /// <summary>
        /// PMXファイル（バイナリ）を読み込んでMeshContextリストに変換
        /// </summary>
        /// <param name="filePath">PMXファイルパス</param>
        /// <param name="splitByMaterial">マテリアルごとに分割するか</param>
        /// <returns>MeshContextのリスト</returns>
        public static List<MeshContext> LoadBinary(string filePath, bool splitByMaterial = false)
        {
            var doc = PMXReader.Load(filePath);
            return Convert(doc, splitByMaterial);
        }

        /// <summary>
        /// PMXDocumentをMeshContextリストに変換
        /// </summary>
        public static List<MeshContext> Convert(PMXDocument doc, bool splitByMaterial = false)
        {
            var results = new List<MeshContext>();

            if (splitByMaterial && doc.Materials.Count > 1)
            {
                // マテリアルごとに分割
                int faceOffset = 0;
                for (int matIdx = 0; matIdx < doc.Materials.Count; matIdx++)
                {
                    var mat = doc.Materials[matIdx];
                    var meshContext = ConvertMaterialRange(doc, matIdx, faceOffset, mat.FaceCount);
                    meshContext.Name = !string.IsNullOrEmpty(mat.Name) ? mat.Name : $"Material_{matIdx}";
                    results.Add(meshContext);
                    faceOffset += mat.FaceCount;
                }
            }
            else
            {
                // 単一メッシュとして変換
                var meshContext = ConvertAll(doc);
                meshContext.Name = !string.IsNullOrEmpty(doc.ModelInfo?.Name) 
                    ? doc.ModelInfo.Name 
                    : Path.GetFileNameWithoutExtension(doc.FilePath);
                results.Add(meshContext);
            }

            return results;
        }

        /// <summary>
        /// PMXモデル全体を単一MeshContextに変換
        /// </summary>
        private static MeshContext ConvertAll(PMXDocument doc)
        {
            var mesh = new MeshObject();

            // 頂点
            foreach (var v in doc.Vertices)
            {
                mesh.AddVertex(ConvertPosition(v.Position), ConvertNormal(v.Normal), v.UV);
            }

            // 面（頂点インデックスを反転してUnityの座標系に対応）
            // マテリアル情報も同時に設定
            int faceIdx = 0;
            int currentMaterial = 0;
            int facesInCurrentMaterial = doc.Materials.Count > 0 ? doc.Materials[0].FaceCount : int.MaxValue;
            
            foreach (var f in doc.Faces)
            {
                // マテリアル境界をチェック
                while (currentMaterial < doc.Materials.Count - 1 && faceIdx >= facesInCurrentMaterial)
                {
                    currentMaterial++;
                    facesInCurrentMaterial += doc.Materials[currentMaterial].FaceCount;
                }
                
                // VertexIndex2とVertexIndex3を入れ替えて面を追加（座標系変換）
                var face = new Face(f.VertexIndex1, f.VertexIndex3, f.VertexIndex2, currentMaterial);
                mesh.AddFace(face);
                faceIdx++;
            }

            mesh.CalculateBounds();

            return new MeshContext
            {
                MeshObject = mesh,
                Type = MeshType.Mesh
            };
        }

        /// <summary>
        /// 指定マテリアル範囲をMeshContextに変換
        /// </summary>
        private static MeshContext ConvertMaterialRange(PMXDocument doc, int materialIndex, int faceOffset, int faceCount)
        {
            var mesh = new MeshObject();
            var vertexMap = new Dictionary<int, int>();  // PMX頂点Index → 新頂点Index

            // 使用する頂点を収集して追加
            for (int i = 0; i < faceCount && faceOffset + i < doc.Faces.Count; i++)
            {
                var face = doc.Faces[faceOffset + i];
                AddVertexIfNeeded(doc, mesh, vertexMap, face.VertexIndex1);
                AddVertexIfNeeded(doc, mesh, vertexMap, face.VertexIndex2);
                AddVertexIfNeeded(doc, mesh, vertexMap, face.VertexIndex3);
            }

            // 面を追加（頂点インデックスを新しいものに変換）
            for (int i = 0; i < faceCount && faceOffset + i < doc.Faces.Count; i++)
            {
                var f = doc.Faces[faceOffset + i];
                int v0 = vertexMap[f.VertexIndex1];
                int v1 = vertexMap[f.VertexIndex2];
                int v2 = vertexMap[f.VertexIndex3];
                var face = new Face(v0, v2, v1, materialIndex);  // v1とv2を入れ替え（座標系変換）
                mesh.AddFace(face);
            }

            mesh.CalculateBounds();

            return new MeshContext
            {
                MeshObject = mesh,
                Type = MeshType.Mesh
            };
        }

        private static void AddVertexIfNeeded(PMXDocument doc, MeshObject mesh, Dictionary<int, int> vertexMap, int pmxIndex)
        {
            if (vertexMap.ContainsKey(pmxIndex))
                return;

            var v = doc.Vertices[pmxIndex];
            int newIndex = mesh.AddVertex(ConvertPosition(v.Position), ConvertNormal(v.Normal), v.UV);
            vertexMap[pmxIndex] = newIndex;
        }

        /// <summary>
        /// PMX座標系（左手系）からUnity座標系（左手系だがZ反転）に変換
        /// PMX: X右, Y上, Z奥
        /// Unity: X右, Y上, Z手前
        /// </summary>
        private static Vector3 ConvertPosition(Vector3 pmxPos)
        {
            // Z軸を反転
            return new Vector3(pmxPos.x, pmxPos.y, -pmxPos.z);
        }

        private static Vector3 ConvertNormal(Vector3 pmxNormal)
        {
            return new Vector3(pmxNormal.x, pmxNormal.y, -pmxNormal.z);
        }

        #region Extended Information

        /// <summary>
        /// ボーン情報も含めて変換（将来拡張用）
        /// </summary>
        public static (MeshContext meshContext, List<PMXBone> bones) LoadWithBones(string filePath)
        {
            var doc = PMXReader.Load(filePath);
            var meshContext = ConvertAll(doc);
            meshContext.Name = doc.ModelInfo?.Name ?? Path.GetFileNameWithoutExtension(filePath);
            return (meshContext, doc.Bones);
        }

        /// <summary>
        /// モーフ情報を頂点オフセットとして取得（将来拡張用）
        /// </summary>
        public static Dictionary<string, Vector3[]> GetVertexMorphs(PMXDocument doc)
        {
            var morphs = new Dictionary<string, Vector3[]>();

            foreach (var morph in doc.Morphs)
            {
                if (morph.MorphType != 1)  // 1 = Vertex morph
                    continue;

                var offsets = new Vector3[doc.Vertices.Count];
                foreach (var offset in morph.Offsets)
                {
                    var vertexOffset = offset as PMXVertexMorphOffset;
                    if (vertexOffset != null && vertexOffset.VertexIndex >= 0 && vertexOffset.VertexIndex < offsets.Length)
                    {
                        offsets[vertexOffset.VertexIndex] = ConvertPosition(vertexOffset.Offset);
                    }
                }
                morphs[morph.Name] = offsets;
            }

            return morphs;
        }

        #endregion
    }
}
