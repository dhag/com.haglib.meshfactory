// Assets/Editor/Poly_Ling/PMX/Export/PMXWriter.cs
// PMXバイナリ形式での出力

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXバイナリファイルライター
    /// PMX 2.1形式で出力
    /// </summary>
    public static class PMXWriter
    {
        // PMXマジックナンバー
        private static readonly byte[] PMX_MAGIC = { 0x50, 0x4D, 0x58, 0x20 };  // "PMX "

        /// <summary>
        /// PMXファイルを書き出し
        /// </summary>
        public static void Save(PMXDocument document, string filePath)
        {
            using (var stream = new FileStream(filePath, FileMode.Create))
            using (var writer = new BinaryWriter(stream))
            {
                WriteHeader(writer, document);
                WriteModelInfo(writer, document.ModelInfo);
                WriteVertices(writer, document);
                WriteFaces(writer, document);
                WriteTextures(writer, document);
                WriteMaterials(writer, document);
                WriteBones(writer, document);
                WriteMorphs(writer, document);
                WriteDisplayFrames(writer, document);
                WriteBodies(writer, document);
                WriteJoints(writer, document);
            }

            Debug.Log($"[PMXWriter] Saved to: {filePath}");
        }

        // ================================================================
        // ヘッダー
        // ================================================================

        private static void WriteHeader(BinaryWriter writer, PMXDocument document)
        {
            // マジックナンバー "PMX "
            writer.Write(PMX_MAGIC);

            // バージョン (float)
            writer.Write(document.Version);

            // グローバル情報のバイト数
            writer.Write((byte)8);

            // エンコード (0:UTF-16, 1:UTF-8)
            writer.Write((byte)document.CharacterEncoding);

            // 追加UV数
            writer.Write((byte)document.AdditionalUVCount);

            // 各インデックスサイズ
            int vertexCount = document.Vertices.Count;
            int textureCount = GetTextureCount(document);
            int materialCount = document.Materials.Count;
            int boneCount = document.Bones.Count;
            int morphCount = document.Morphs.Count;
            int bodyCount = document.RigidBodies.Count;

            writer.Write(GetIndexSize(vertexCount));   // 頂点インデックスサイズ
            writer.Write(GetIndexSize(textureCount));  // テクスチャインデックスサイズ
            writer.Write(GetIndexSize(materialCount)); // マテリアルインデックスサイズ
            writer.Write(GetIndexSize(boneCount));     // ボーンインデックスサイズ
            writer.Write(GetIndexSize(morphCount));    // モーフインデックスサイズ
            writer.Write(GetIndexSize(bodyCount));     // 剛体インデックスサイズ
        }

        private static byte GetIndexSize(int count)
        {
            if (count <= 255) return 1;
            if (count <= 65535) return 2;
            return 4;
        }

        // ================================================================
        // モデル情報
        // ================================================================

        private static void WriteModelInfo(BinaryWriter writer, PMXModelInfo info)
        {
            WriteText(writer, info.Name ?? "");
            WriteText(writer, info.NameEnglish ?? "");
            WriteText(writer, info.Comment ?? "");
            WriteText(writer, info.CommentEnglish ?? "");
        }

        // ================================================================
        // 頂点
        // ================================================================

        private static void WriteVertices(BinaryWriter writer, PMXDocument document)
        {
            writer.Write(document.Vertices.Count);

            int boneIndexSize = GetIndexSize(document.Bones.Count);

            foreach (var vertex in document.Vertices)
            {
                // 位置
                WriteVector3(writer, vertex.Position);

                // 法線
                WriteVector3(writer, vertex.Normal);

                // UV
                WriteVector2(writer, vertex.UV);

                // 追加UV
                for (int i = 0; i < document.AdditionalUVCount; i++)
                {
                    if (vertex.AdditionalUVs != null && i < vertex.AdditionalUVs.Length)
                        WriteVector4(writer, vertex.AdditionalUVs[i]);
                    else
                        WriteVector4(writer, Vector4.zero);
                }

                // ウェイトタイプ
                writer.Write((byte)vertex.WeightType);

                // ボーンウェイト
                WriteBoneWeight(writer, document, vertex, boneIndexSize);

                // エッジ倍率
                writer.Write(vertex.EdgeScale);
            }
        }

        private static void WriteBoneWeight(BinaryWriter writer, PMXDocument document, PMXVertex vertex, int boneIndexSize)
        {
            var boneWeights = vertex.BoneWeights ?? new PMXBoneWeight[0];

            switch (vertex.WeightType)
            {
                case 0:  // BDEF1
                    WriteBoneIndex(writer, document, GetBoneName(boneWeights, 0), boneIndexSize);
                    break;

                case 1:  // BDEF2
                    WriteBoneIndex(writer, document, GetBoneName(boneWeights, 0), boneIndexSize);
                    WriteBoneIndex(writer, document, GetBoneName(boneWeights, 1), boneIndexSize);
                    writer.Write(GetWeight(boneWeights, 0));
                    break;

                case 2:  // BDEF4
                    WriteBoneIndex(writer, document, GetBoneName(boneWeights, 0), boneIndexSize);
                    WriteBoneIndex(writer, document, GetBoneName(boneWeights, 1), boneIndexSize);
                    WriteBoneIndex(writer, document, GetBoneName(boneWeights, 2), boneIndexSize);
                    WriteBoneIndex(writer, document, GetBoneName(boneWeights, 3), boneIndexSize);
                    writer.Write(GetWeight(boneWeights, 0));
                    writer.Write(GetWeight(boneWeights, 1));
                    writer.Write(GetWeight(boneWeights, 2));
                    writer.Write(GetWeight(boneWeights, 3));
                    break;

                case 3:  // SDEF
                    WriteBoneIndex(writer, document, GetBoneName(boneWeights, 0), boneIndexSize);
                    WriteBoneIndex(writer, document, GetBoneName(boneWeights, 1), boneIndexSize);
                    writer.Write(GetWeight(boneWeights, 0));
                    WriteVector3(writer, vertex.SDEF_C);
                    WriteVector3(writer, vertex.SDEF_R0);
                    WriteVector3(writer, vertex.SDEF_R1);
                    break;

                case 4:  // QDEF
                    WriteBoneIndex(writer, document, GetBoneName(boneWeights, 0), boneIndexSize);
                    WriteBoneIndex(writer, document, GetBoneName(boneWeights, 1), boneIndexSize);
                    WriteBoneIndex(writer, document, GetBoneName(boneWeights, 2), boneIndexSize);
                    WriteBoneIndex(writer, document, GetBoneName(boneWeights, 3), boneIndexSize);
                    writer.Write(GetWeight(boneWeights, 0));
                    writer.Write(GetWeight(boneWeights, 1));
                    writer.Write(GetWeight(boneWeights, 2));
                    writer.Write(GetWeight(boneWeights, 3));
                    break;
            }
        }

        private static string GetBoneName(PMXBoneWeight[] weights, int index)
        {
            return (weights != null && index < weights.Length) ? weights[index].BoneName : "";
        }

        private static float GetWeight(PMXBoneWeight[] weights, int index)
        {
            return (weights != null && index < weights.Length) ? weights[index].Weight : 0f;
        }

        private static void WriteBoneIndex(BinaryWriter writer, PMXDocument document, string boneName, int indexSize)
        {
            int index = document.GetBoneIndex(boneName);
            if (index < 0) index = 0;

            WriteIndex(writer, index, indexSize);
        }

        // ================================================================
        // 面
        // ================================================================

        private static void WriteFaces(BinaryWriter writer, PMXDocument document)
        {
            // 面数 × 3 = 頂点インデックス数
            writer.Write(document.Faces.Count * 3);

            int vertexIndexSize = GetIndexSize(document.Vertices.Count);

            foreach (var face in document.Faces)
            {
                WriteIndex(writer, face.VertexIndex1, vertexIndexSize);
                WriteIndex(writer, face.VertexIndex2, vertexIndexSize);
                WriteIndex(writer, face.VertexIndex3, vertexIndexSize);
            }
        }

        // ================================================================
        // テクスチャ
        // ================================================================

        private static void WriteTextures(BinaryWriter writer, PMXDocument document)
        {
            // 使用されているテクスチャパスを収集
            var textures = new List<string>();
            foreach (var mat in document.Materials)
            {
                if (!string.IsNullOrEmpty(mat.TexturePath) && !textures.Contains(mat.TexturePath))
                    textures.Add(mat.TexturePath);
                if (!string.IsNullOrEmpty(mat.SphereTexturePath) && !textures.Contains(mat.SphereTexturePath))
                    textures.Add(mat.SphereTexturePath);
                if (!string.IsNullOrEmpty(mat.ToonTexturePath) && !mat.SharedToon && !textures.Contains(mat.ToonTexturePath))
                    textures.Add(mat.ToonTexturePath);
            }

            writer.Write(textures.Count);
            foreach (var tex in textures)
            {
                WriteText(writer, tex);
            }
        }

        private static int GetTextureCount(PMXDocument document)
        {
            var textures = new HashSet<string>();
            foreach (var mat in document.Materials)
            {
                if (!string.IsNullOrEmpty(mat.TexturePath))
                    textures.Add(mat.TexturePath);
                if (!string.IsNullOrEmpty(mat.SphereTexturePath))
                    textures.Add(mat.SphereTexturePath);
                if (!string.IsNullOrEmpty(mat.ToonTexturePath) && !mat.SharedToon)
                    textures.Add(mat.ToonTexturePath);
            }
            return textures.Count;
        }

        private static int GetTextureIndex(PMXDocument document, string texturePath)
        {
            if (string.IsNullOrEmpty(texturePath)) return -1;

            var textures = new List<string>();
            foreach (var mat in document.Materials)
            {
                if (!string.IsNullOrEmpty(mat.TexturePath) && !textures.Contains(mat.TexturePath))
                    textures.Add(mat.TexturePath);
                if (!string.IsNullOrEmpty(mat.SphereTexturePath) && !textures.Contains(mat.SphereTexturePath))
                    textures.Add(mat.SphereTexturePath);
                if (!string.IsNullOrEmpty(mat.ToonTexturePath) && !mat.SharedToon && !textures.Contains(mat.ToonTexturePath))
                    textures.Add(mat.ToonTexturePath);
            }

            return textures.IndexOf(texturePath);
        }

        // ================================================================
        // マテリアル
        // ================================================================

        private static void WriteMaterials(BinaryWriter writer, PMXDocument document)
        {
            writer.Write(document.Materials.Count);

            int textureIndexSize = GetIndexSize(GetTextureCount(document));

            foreach (var mat in document.Materials)
            {
                WriteText(writer, mat.Name ?? "");
                WriteText(writer, mat.NameEnglish ?? "");

                // Diffuse (RGBA)
                WriteColor4(writer, mat.Diffuse);

                // Specular (RGB)
                WriteColor3(writer, mat.Specular);

                // SpecularPower
                writer.Write(mat.SpecularPower);

                // Ambient (RGB)
                WriteColor3(writer, mat.Ambient);

                // 描画フラグ
                writer.Write((byte)mat.DrawFlags);

                // エッジ色
                WriteColor4(writer, mat.EdgeColor);

                // エッジサイズ
                writer.Write(mat.EdgeSize);

                // テクスチャインデックス
                WriteIndex(writer, GetTextureIndex(document, mat.TexturePath), textureIndexSize);

                // スフィアテクスチャインデックス
                WriteIndex(writer, GetTextureIndex(document, mat.SphereTexturePath), textureIndexSize);

                // スフィアモード
                writer.Write((byte)mat.SphereMode);

                // 共有Toonフラグ
                writer.Write((byte)(mat.SharedToon ? 1 : 0));

                // Toonテクスチャ
                if (mat.SharedToon)
                {
                    int toonIndex = mat.ToonTextureIndex >= 0 ? mat.ToonTextureIndex : 0;
                    writer.Write((byte)toonIndex);
                }
                else
                {
                    WriteIndex(writer, GetTextureIndex(document, mat.ToonTexturePath), textureIndexSize);
                }

                // メモ
                WriteText(writer, mat.Memo ?? "");

                // 面数（頂点数ではなく、インデックス数 = 面数 × 3）
                writer.Write(mat.FaceCount * 3);
            }
        }

        // ================================================================
        // ボーン
        // ================================================================

        private static void WriteBones(BinaryWriter writer, PMXDocument document)
        {
            writer.Write(document.Bones.Count);

            int boneIndexSize = GetIndexSize(document.Bones.Count);

            foreach (var bone in document.Bones)
            {
                WriteText(writer, bone.Name ?? "");
                WriteText(writer, bone.NameEnglish ?? "");

                // 位置
                WriteVector3(writer, bone.Position);

                // 親ボーンインデックス
                int parentIndex = document.GetBoneIndex(bone.ParentBoneName);
                WriteIndex(writer, parentIndex, boneIndexSize);

                // 変形階層
                writer.Write(bone.TransformLevel);

                // フラグ
                writer.Write((ushort)bone.Flags);

                // 接続先
                bool connectByBone = (bone.Flags & 0x0001) != 0;
                if (connectByBone)
                {
                    int connectIndex = document.GetBoneIndex(bone.ConnectBoneName);
                    WriteIndex(writer, connectIndex, boneIndexSize);
                }
                else
                {
                    WriteVector3(writer, bone.ConnectOffset);
                }

                // 回転付与・移動付与
                bool hasGrant = (bone.Flags & 0x0100) != 0 || (bone.Flags & 0x0200) != 0;
                if (hasGrant)
                {
                    int grantParentIndex = document.GetBoneIndex(bone.GrantParentBoneName);
                    WriteIndex(writer, grantParentIndex, boneIndexSize);
                    writer.Write(bone.GrantRate);
                }

                // 軸固定
                bool hasFixedAxis = (bone.Flags & 0x0400) != 0;
                if (hasFixedAxis)
                {
                    WriteVector3(writer, bone.FixedAxis);
                }

                // ローカル軸
                bool hasLocalAxis = (bone.Flags & 0x0800) != 0;
                if (hasLocalAxis)
                {
                    WriteVector3(writer, bone.LocalAxisX);
                    WriteVector3(writer, bone.LocalAxisZ);
                }

                // 外部親
                bool hasExternalParent = (bone.Flags & 0x2000) != 0;
                if (hasExternalParent)
                {
                    writer.Write(bone.ExternalParentKey);
                }

                // IK
                bool hasIK = (bone.Flags & 0x0020) != 0;
                if (hasIK)
                {
                    int ikTargetIndex = document.GetBoneIndex(bone.IKTargetBoneName);
                    WriteIndex(writer, ikTargetIndex, boneIndexSize);
                    writer.Write(bone.IKLoopCount);
                    writer.Write(bone.IKLimitAngle);

                    writer.Write(bone.IKLinks.Count);
                    foreach (var link in bone.IKLinks)
                    {
                        int linkIndex = document.GetBoneIndex(link.BoneName);
                        WriteIndex(writer, linkIndex, boneIndexSize);
                        writer.Write((byte)(link.HasLimit ? 1 : 0));
                        if (link.HasLimit)
                        {
                            WriteVector3(writer, link.LimitMin);
                            WriteVector3(writer, link.LimitMax);
                        }
                    }
                }
            }
        }

        // ================================================================
        // モーフ
        // ================================================================

        private static void WriteMorphs(BinaryWriter writer, PMXDocument document)
        {
            writer.Write(document.Morphs.Count);

            int vertexIndexSize = GetIndexSize(document.Vertices.Count);
            int boneIndexSize = GetIndexSize(document.Bones.Count);
            int materialIndexSize = GetIndexSize(document.Materials.Count);
            int morphIndexSize = GetIndexSize(document.Morphs.Count);

            foreach (var morph in document.Morphs)
            {
                WriteText(writer, morph.Name ?? "");
                WriteText(writer, morph.NameEnglish ?? "");

                writer.Write((byte)morph.Panel);
                writer.Write((byte)morph.MorphType);

                writer.Write(morph.Offsets.Count);

                foreach (var offset in morph.Offsets)
                {
                    if (offset is PMXVertexMorphOffset vertexOffset)
                    {
                        WriteIndex(writer, vertexOffset.VertexIndex, vertexIndexSize);
                        WriteVector3(writer, vertexOffset.Offset);
                    }
                    // 他のモーフタイプは必要に応じて追加
                }
            }
        }

        // ================================================================
        // 表示枠
        // ================================================================

        private static void WriteDisplayFrames(BinaryWriter writer, PMXDocument document)
        {
            // 最小限の表示枠を出力
            writer.Write(2);  // Root + 表情

            // Root
            WriteText(writer, "Root");
            WriteText(writer, "Root");
            writer.Write((byte)1);  // 特殊枠
            writer.Write(0);  // 要素数

            // 表情
            WriteText(writer, "表情");
            WriteText(writer, "Exp");
            writer.Write((byte)1);  // 特殊枠
            writer.Write(0);  // 要素数
        }

        // ================================================================
        // 剛体
        // ================================================================

        private static void WriteBodies(BinaryWriter writer, PMXDocument document)
        {
            writer.Write(document.RigidBodies.Count);

            int boneIndexSize = GetIndexSize(document.Bones.Count);

            foreach (var body in document.RigidBodies)
            {
                WriteText(writer, body.Name ?? "");
                WriteText(writer, body.NameEnglish ?? "");

                int boneIndex = document.GetBoneIndex(body.RelatedBoneName);
                WriteIndex(writer, boneIndex, boneIndexSize);

                writer.Write((byte)body.Group);

                // 非衝突グループ（16ビットフラグ）
                ushort nonCollisionFlag = ParseNonCollisionGroups(body.NonCollisionGroups);
                writer.Write(nonCollisionFlag);

                writer.Write((byte)body.Shape);
                WriteVector3(writer, body.Size);
                WriteVector3(writer, body.Position);
                WriteVector3(writer, body.Rotation * Mathf.Deg2Rad);  // 度→ラジアン

                writer.Write(body.Mass);
                writer.Write(body.LinearDamping);
                writer.Write(body.AngularDamping);
                writer.Write(body.Restitution);
                writer.Write(body.Friction);

                writer.Write((byte)body.PhysicsMode);
            }
        }

        private static ushort ParseNonCollisionGroups(string groups)
        {
            if (string.IsNullOrEmpty(groups)) return 0;

            ushort result = 0;
            foreach (var part in groups.Split(',', ' '))
            {
                if (int.TryParse(part.Trim(), out int group) && group >= 0 && group < 16)
                {
                    result |= (ushort)(1 << group);
                }
            }
            return result;
        }

        // ================================================================
        // ジョイント
        // ================================================================

        private static void WriteJoints(BinaryWriter writer, PMXDocument document)
        {
            writer.Write(document.Joints.Count);

            int bodyIndexSize = GetIndexSize(document.RigidBodies.Count);

            foreach (var joint in document.Joints)
            {
                WriteText(writer, joint.Name ?? "");
                WriteText(writer, joint.NameEnglish ?? "");

                writer.Write((byte)joint.JointType);

                int bodyAIndex = GetBodyIndex(document, joint.BodyAName);
                int bodyBIndex = GetBodyIndex(document, joint.BodyBName);
                WriteIndex(writer, bodyAIndex, bodyIndexSize);
                WriteIndex(writer, bodyBIndex, bodyIndexSize);

                WriteVector3(writer, joint.Position);
                WriteVector3(writer, joint.Rotation * Mathf.Deg2Rad);

                WriteVector3(writer, joint.TranslationMin);
                WriteVector3(writer, joint.TranslationMax);
                WriteVector3(writer, joint.RotationMin * Mathf.Deg2Rad);
                WriteVector3(writer, joint.RotationMax * Mathf.Deg2Rad);

                WriteVector3(writer, joint.SpringTranslation);
                WriteVector3(writer, joint.SpringRotation);
            }
        }

        private static int GetBodyIndex(PMXDocument document, string bodyName)
        {
            for (int i = 0; i < document.RigidBodies.Count; i++)
            {
                if (document.RigidBodies[i].Name == bodyName)
                    return i;
            }
            return -1;
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static void WriteText(BinaryWriter writer, string text)
        {
            byte[] bytes = Encoding.Unicode.GetBytes(text ?? "");
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static void WriteVector2(BinaryWriter writer, Vector2 v)
        {
            writer.Write(v.x);
            writer.Write(v.y);
        }

        private static void WriteVector3(BinaryWriter writer, Vector3 v)
        {
            writer.Write(v.x);
            writer.Write(v.y);
            writer.Write(v.z);
        }

        private static void WriteVector4(BinaryWriter writer, Vector4 v)
        {
            writer.Write(v.x);
            writer.Write(v.y);
            writer.Write(v.z);
            writer.Write(v.w);
        }

        private static void WriteColor3(BinaryWriter writer, Color c)
        {
            writer.Write(c.r);
            writer.Write(c.g);
            writer.Write(c.b);
        }

        private static void WriteColor4(BinaryWriter writer, Color c)
        {
            writer.Write(c.r);
            writer.Write(c.g);
            writer.Write(c.b);
            writer.Write(c.a);
        }

        private static void WriteIndex(BinaryWriter writer, int index, int indexSize)
        {
            switch (indexSize)
            {
                case 1:
                    writer.Write((byte)(index >= 0 ? index : 255));
                    break;
                case 2:
                    writer.Write((ushort)(index >= 0 ? index : 65535));
                    break;
                case 4:
                    writer.Write(index);
                    break;
            }
        }
    }
}
