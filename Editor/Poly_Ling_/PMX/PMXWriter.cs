// PMXWriter.cs - PMXバイナリ書き出し
// BinaryWriterを直接使用、PMXDocumentから出力

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMXファイルライター（バイナリ）
    /// </summary>
    public static class PMXWriter
    {
        /// <summary>
        /// PMXDocumentをファイルに保存
        /// </summary>
        public static void Save(PMXDocument doc, string filePath)
        {
            using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            using (var writer = new BinaryWriter(fs))
            {
                // マジックナンバー "PMX "
                writer.Write(Encoding.ASCII.GetBytes("PMX "));

                // インデックスサイズを計算
                int vertexIndexSize = CalculateIndexSize(doc.Vertices.Count, false);
                int textureIndexSize = CalculateIndexSize(doc.TexturePaths.Count, true);
                int materialIndexSize = CalculateIndexSize(doc.Materials.Count, true);
                int boneIndexSize = CalculateIndexSize(doc.Bones.Count, true);
                int morphIndexSize = CalculateIndexSize(doc.Morphs.Count, true);
                int rigidBodyIndexSize = CalculateIndexSize(doc.RigidBodies.Count, true);

                // ヘッダー
                WriteHeader(writer, doc, vertexIndexSize, textureIndexSize, materialIndexSize, 
                           boneIndexSize, morphIndexSize, rigidBodyIndexSize);

                // モデル情報
                WriteModelInfo(writer, doc);

                // 頂点
                writer.Write(doc.Vertices.Count);
                foreach (var v in doc.Vertices)
                    WriteVertex(writer, doc, v, boneIndexSize);

                // 面
                writer.Write(doc.Faces.Count * 3);
                foreach (var f in doc.Faces)
                    WriteFace(writer, f, vertexIndexSize);

                // テクスチャ
                writer.Write(doc.TexturePaths.Count);
                foreach (var path in doc.TexturePaths)
                    WriteText(writer, path, doc.CharacterEncoding);

                // マテリアル
                writer.Write(doc.Materials.Count);
                foreach (var mat in doc.Materials)
                    WriteMaterial(writer, doc, mat, textureIndexSize);

                // ボーン
                writer.Write(doc.Bones.Count);
                foreach (var bone in doc.Bones)
                    WriteBone(writer, doc, bone, boneIndexSize);

                // モーフ
                writer.Write(doc.Morphs.Count);
                foreach (var morph in doc.Morphs)
                    WriteMorph(writer, doc, morph, vertexIndexSize, boneIndexSize, 
                              materialIndexSize, morphIndexSize, rigidBodyIndexSize);

                // 表示枠
                writer.Write(doc.DisplayFrames.Count);
                foreach (var frame in doc.DisplayFrames)
                    WriteDisplayFrame(writer, doc, frame, boneIndexSize, morphIndexSize);

                // 剛体
                writer.Write(doc.RigidBodies.Count);
                foreach (var body in doc.RigidBodies)
                    WriteRigidBody(writer, doc, body, boneIndexSize);

                // ジョイント
                writer.Write(doc.Joints.Count);
                foreach (var joint in doc.Joints)
                    WriteJoint(writer, doc, joint, rigidBodyIndexSize);

                // ソフトボディ（PMX 2.1以降）
                if (doc.Version >= 2.1f)
                {
                    writer.Write(doc.SoftBodies.Count);
                    foreach (var body in doc.SoftBodies)
                        WriteSoftBody(writer, doc, body, materialIndexSize, rigidBodyIndexSize, vertexIndexSize);
                }
            }
        }

        #region Index Size Calculation

        private static int CalculateIndexSize(int count, bool signed)
        {
            if (signed)
            {
                // 符号付き（-1が必要）
                if (count <= 127) return 1;
                if (count <= 32767) return 2;
                return 4;
            }
            else
            {
                // 符号なし
                if (count <= 255) return 1;
                if (count <= 65535) return 2;
                return 4;
            }
        }

        #endregion

        #region Header & ModelInfo

        private static void WriteHeader(BinaryWriter writer, PMXDocument doc,
            int vertexIndexSize, int textureIndexSize, int materialIndexSize,
            int boneIndexSize, int morphIndexSize, int rigidBodyIndexSize)
        {
            writer.Write(doc.Version);
            writer.Write((byte)8);  // データサイズ

            writer.Write((byte)doc.CharacterEncoding);
            writer.Write((byte)doc.AdditionalUVCount);
            writer.Write((byte)vertexIndexSize);
            writer.Write((byte)textureIndexSize);
            writer.Write((byte)materialIndexSize);
            writer.Write((byte)boneIndexSize);
            writer.Write((byte)morphIndexSize);
            writer.Write((byte)rigidBodyIndexSize);
        }

        private static void WriteModelInfo(BinaryWriter writer, PMXDocument doc)
        {
            WriteText(writer, doc.ModelInfo.Name ?? "", doc.CharacterEncoding);
            WriteText(writer, doc.ModelInfo.NameEnglish ?? "", doc.CharacterEncoding);
            WriteText(writer, doc.ModelInfo.Comment ?? "", doc.CharacterEncoding);
            WriteText(writer, doc.ModelInfo.CommentEnglish ?? "", doc.CharacterEncoding);
        }

        #endregion

        #region Vertex & Face

        private static void WriteVertex(BinaryWriter writer, PMXDocument doc, PMXVertex v, int boneIndexSize)
        {
            WriteVector3(writer, v.Position);
            WriteVector3(writer, v.Normal);
            WriteVector2(writer, v.UV);

            // 追加UV
            for (int i = 0; i < doc.AdditionalUVCount; i++)
            {
                if (v.AdditionalUVs != null && i < v.AdditionalUVs.Length)
                    WriteVector4(writer, v.AdditionalUVs[i]);
                else
                    WriteVector4(writer, Vector4.zero);
            }

            // ボーンウェイト
            writer.Write((byte)v.WeightType);

            switch (v.WeightType)
            {
                case 0: // BDEF1
                    WriteIndex(writer, GetBoneIndex(v, 0), boneIndexSize);
                    break;
                case 1: // BDEF2
                    WriteIndex(writer, GetBoneIndex(v, 0), boneIndexSize);
                    WriteIndex(writer, GetBoneIndex(v, 1), boneIndexSize);
                    writer.Write(GetBoneWeight(v, 0));
                    break;
                case 2: // BDEF4
                case 4: // QDEF
                    for (int i = 0; i < 4; i++)
                        WriteIndex(writer, GetBoneIndex(v, i), boneIndexSize);
                    for (int i = 0; i < 4; i++)
                        writer.Write(GetBoneWeight(v, i));
                    break;
                case 3: // SDEF
                    WriteIndex(writer, GetBoneIndex(v, 0), boneIndexSize);
                    WriteIndex(writer, GetBoneIndex(v, 1), boneIndexSize);
                    writer.Write(GetBoneWeight(v, 0));
                    WriteVector3(writer, v.SDEF_C);
                    WriteVector3(writer, v.SDEF_R0);
                    WriteVector3(writer, v.SDEF_R1);
                    break;
            }

            writer.Write(v.EdgeScale);
        }

        private static int GetBoneIndex(PMXVertex v, int idx)
        {
            if (v.BoneWeights == null || idx >= v.BoneWeights.Length)
                return -1;
            return v.BoneWeights[idx].BoneIndex;
        }

        private static float GetBoneWeight(PMXVertex v, int idx)
        {
            if (v.BoneWeights == null || idx >= v.BoneWeights.Length)
                return 0f;
            return v.BoneWeights[idx].Weight;
        }

        private static void WriteFace(BinaryWriter writer, PMXFace f, int vertexIndexSize)
        {
            WriteUnsignedIndex(writer, f.VertexIndex1, vertexIndexSize);
            WriteUnsignedIndex(writer, f.VertexIndex2, vertexIndexSize);
            WriteUnsignedIndex(writer, f.VertexIndex3, vertexIndexSize);
        }

        #endregion

        #region Material

        private static void WriteMaterial(BinaryWriter writer, PMXDocument doc, PMXMaterial mat, int textureIndexSize)
        {
            WriteText(writer, mat.Name ?? "", doc.CharacterEncoding);
            WriteText(writer, mat.NameEnglish ?? "", doc.CharacterEncoding);

            WriteColor4(writer, mat.Diffuse);
            WriteColor3(writer, mat.Specular);
            writer.Write(mat.SpecularPower);
            WriteColor3(writer, mat.Ambient);

            writer.Write((byte)mat.DrawFlags);
            WriteColor4(writer, mat.EdgeColor);
            writer.Write(mat.EdgeSize);

            WriteIndex(writer, mat.TextureIndex, textureIndexSize);
            WriteIndex(writer, mat.SphereTextureIndex, textureIndexSize);
            writer.Write((byte)mat.SphereMode);

            writer.Write((byte)(mat.SharedToon ? 1 : 0));
            if (mat.SharedToon)
                writer.Write((byte)mat.ToonTextureIndex);
            else
                WriteIndex(writer, mat.ToonTextureIndex, textureIndexSize);

            WriteText(writer, mat.Memo ?? "", doc.CharacterEncoding);
            writer.Write(mat.FaceCount * 3);
        }

        #endregion

        #region Bone

        private static void WriteBone(BinaryWriter writer, PMXDocument doc, PMXBone bone, int boneIndexSize)
        {
            WriteText(writer, bone.Name ?? "", doc.CharacterEncoding);
            WriteText(writer, bone.NameEnglish ?? "", doc.CharacterEncoding);
            WriteVector3(writer, bone.Position);
            WriteIndex(writer, bone.ParentIndex, boneIndexSize);
            writer.Write(bone.TransformLevel);
            writer.Write((ushort)bone.Flags);

            bool connected = (bone.Flags & 0x0001) != 0;
            if (connected)
                WriteIndex(writer, bone.ConnectBoneIndex, boneIndexSize);
            else
                WriteVector3(writer, bone.ConnectOffset);

            bool hasGrant = (bone.Flags & 0x0100) != 0 || (bone.Flags & 0x0200) != 0;
            if (hasGrant)
            {
                WriteIndex(writer, bone.GrantParentIndex, boneIndexSize);
                writer.Write(bone.GrantRate);
            }

            if ((bone.Flags & 0x0400) != 0)
                WriteVector3(writer, bone.FixedAxis);

            if ((bone.Flags & 0x0800) != 0)
            {
                WriteVector3(writer, bone.LocalAxisX);
                WriteVector3(writer, bone.LocalAxisZ);
            }

            if ((bone.Flags & 0x2000) != 0)
                writer.Write(bone.ExternalParentKey);

            if ((bone.Flags & 0x0020) != 0)
            {
                WriteIndex(writer, bone.IKTargetIndex, boneIndexSize);
                writer.Write(bone.IKLoopCount);
                writer.Write(bone.IKLimitAngle);

                writer.Write(bone.IKLinks.Count);
                foreach (var link in bone.IKLinks)
                {
                    WriteIndex(writer, link.BoneIndex, boneIndexSize);
                    writer.Write((byte)(link.HasLimit ? 1 : 0));
                    if (link.HasLimit)
                    {
                        WriteVector3(writer, link.LimitMin);
                        WriteVector3(writer, link.LimitMax);
                    }
                }
            }
        }

        #endregion

        #region Morph

        private static void WriteMorph(BinaryWriter writer, PMXDocument doc, PMXMorph morph,
            int vertexIndexSize, int boneIndexSize, int materialIndexSize, int morphIndexSize, int rigidBodyIndexSize)
        {
            WriteText(writer, morph.Name ?? "", doc.CharacterEncoding);
            WriteText(writer, morph.NameEnglish ?? "", doc.CharacterEncoding);
            writer.Write((byte)morph.Panel);
            writer.Write((byte)morph.MorphType);

            writer.Write(morph.Offsets.Count);
            foreach (var offset in morph.Offsets)
            {
                switch (morph.MorphType)
                {
                    case 0: // Group
                    case 9: // Flip
                        var groupOffset = offset as PMXGroupMorphOffset;
                        WriteIndex(writer, groupOffset?.MorphIndex ?? 0, morphIndexSize);
                        writer.Write(groupOffset?.Weight ?? 0f);
                        break;

                    case 1: // Vertex
                        var vertexOffset = offset as PMXVertexMorphOffset;
                        WriteIndex(writer, vertexOffset?.VertexIndex ?? 0, vertexIndexSize);
                        WriteVector3(writer, vertexOffset?.Offset ?? Vector3.zero);
                        break;

                    case 2: // Bone
                        var boneOffset = offset as PMXBoneMorphOffset;
                        WriteIndex(writer, boneOffset?.BoneIndex ?? 0, boneIndexSize);
                        WriteVector3(writer, boneOffset?.Translation ?? Vector3.zero);
                        WriteQuaternion(writer, boneOffset?.Rotation ?? Quaternion.identity);
                        break;

                    case 3: case 4: case 5: case 6: case 7: // UV
                        var uvOffset = offset as PMXUVMorphOffset;
                        WriteIndex(writer, uvOffset?.VertexIndex ?? 0, vertexIndexSize);
                        WriteVector4(writer, uvOffset?.Offset ?? Vector4.zero);
                        break;

                    case 8: // Material
                        var matOffset = offset as PMXMaterialMorphOffset;
                        WriteIndex(writer, matOffset?.MaterialIndex ?? 0, materialIndexSize);
                        writer.Write(matOffset?.Operation ?? (byte)0);
                        WriteColor4(writer, matOffset?.Diffuse ?? Color.white);
                        WriteColor3(writer, matOffset?.Specular ?? Color.white);
                        writer.Write(matOffset?.SpecularPower ?? 0f);
                        WriteColor3(writer, matOffset?.Ambient ?? Color.gray);
                        WriteColor4(writer, matOffset?.EdgeColor ?? Color.black);
                        writer.Write(matOffset?.EdgeSize ?? 0f);
                        WriteColor4(writer, matOffset?.TextureCoef ?? Color.white);
                        WriteColor4(writer, matOffset?.SphereCoef ?? Color.white);
                        WriteColor4(writer, matOffset?.ToonCoef ?? Color.white);
                        break;

                    case 10: // Impulse
                        var impulseOffset = offset as PMXImpulseMorphOffset;
                        WriteIndex(writer, impulseOffset?.RigidBodyIndex ?? 0, rigidBodyIndexSize);
                        writer.Write((byte)(impulseOffset?.IsLocal == true ? 1 : 0));
                        WriteVector3(writer, impulseOffset?.Velocity ?? Vector3.zero);
                        WriteVector3(writer, impulseOffset?.Torque ?? Vector3.zero);
                        break;
                }
            }
        }

        #endregion

        #region DisplayFrame

        private static void WriteDisplayFrame(BinaryWriter writer, PMXDocument doc, PMXDisplayFrame frame,
            int boneIndexSize, int morphIndexSize)
        {
            WriteText(writer, frame.Name ?? "", doc.CharacterEncoding);
            WriteText(writer, frame.NameEnglish ?? "", doc.CharacterEncoding);
            writer.Write((byte)(frame.IsSpecial ? 1 : 0));

            writer.Write(frame.Elements.Count);
            foreach (var element in frame.Elements)
            {
                writer.Write((byte)(element.IsMorph ? 1 : 0));
                WriteIndex(writer, element.Index, element.IsMorph ? morphIndexSize : boneIndexSize);
            }
        }

        #endregion

        #region RigidBody & Joint

        private static void WriteRigidBody(BinaryWriter writer, PMXDocument doc, PMXRigidBody body, int boneIndexSize)
        {
            WriteText(writer, body.Name ?? "", doc.CharacterEncoding);
            WriteText(writer, body.NameEnglish ?? "", doc.CharacterEncoding);
            WriteIndex(writer, body.BoneIndex, boneIndexSize);
            writer.Write((byte)body.Group);
            writer.Write(body.CollisionMask);
            writer.Write((byte)body.Shape);
            WriteVector3(writer, body.Size);
            WriteVector3(writer, body.Position);
            WriteVector3(writer, body.Rotation);
            writer.Write(body.Mass);
            writer.Write(body.LinearDamping);
            writer.Write(body.AngularDamping);
            writer.Write(body.Restitution);
            writer.Write(body.Friction);
            writer.Write((byte)body.PhysicsMode);
        }

        private static void WriteJoint(BinaryWriter writer, PMXDocument doc, PMXJoint joint, int rigidBodyIndexSize)
        {
            WriteText(writer, joint.Name ?? "", doc.CharacterEncoding);
            WriteText(writer, joint.NameEnglish ?? "", doc.CharacterEncoding);
            writer.Write((byte)joint.JointType);
            WriteIndex(writer, joint.RigidBodyIndexA, rigidBodyIndexSize);
            WriteIndex(writer, joint.RigidBodyIndexB, rigidBodyIndexSize);
            WriteVector3(writer, joint.Position);
            WriteVector3(writer, joint.Rotation);
            WriteVector3(writer, joint.TranslationMin);
            WriteVector3(writer, joint.TranslationMax);
            WriteVector3(writer, joint.RotationMin);
            WriteVector3(writer, joint.RotationMax);
            WriteVector3(writer, joint.SpringTranslation);
            WriteVector3(writer, joint.SpringRotation);
        }

        #endregion

        #region SoftBody (PMX 2.1)

        private static void WriteSoftBody(BinaryWriter writer, PMXDocument doc, PMXSoftBody body,
            int materialIndexSize, int rigidBodyIndexSize, int vertexIndexSize)
        {
            WriteText(writer, body.Name ?? "", doc.CharacterEncoding);
            WriteText(writer, body.NameEnglish ?? "", doc.CharacterEncoding);
            writer.Write(body.Shape);
            WriteIndex(writer, body.MaterialIndex, materialIndexSize);
            writer.Write(body.Group);
            writer.Write(body.CollisionMask);
            writer.Write(body.Flags);
            writer.Write(body.BendingLinkDistance);
            writer.Write(body.ClusterCount);
            writer.Write(body.TotalMass);
            writer.Write(body.Margin);

            writer.Write(body.AeroModel);
            writer.Write(body.VCF); writer.Write(body.DP);
            writer.Write(body.DG); writer.Write(body.LF);
            writer.Write(body.PR); writer.Write(body.VC);
            writer.Write(body.DF); writer.Write(body.MT);
            writer.Write(body.CHR); writer.Write(body.KHR);
            writer.Write(body.SHR); writer.Write(body.AHR);
            writer.Write(body.SRHR_CL); writer.Write(body.SKHR_CL);
            writer.Write(body.SSHR_CL); writer.Write(body.SR_SPLT_CL);
            writer.Write(body.SK_SPLT_CL); writer.Write(body.SS_SPLT_CL);
            writer.Write(body.V_IT); writer.Write(body.P_IT);
            writer.Write(body.D_IT); writer.Write(body.C_IT);
            writer.Write(body.LST); writer.Write(body.AST);
            writer.Write(body.VST);

            writer.Write(body.Anchors.Count);
            foreach (var anchor in body.Anchors)
            {
                WriteIndex(writer, anchor.RigidBodyIndex, rigidBodyIndexSize);
                WriteIndex(writer, anchor.VertexIndex, vertexIndexSize);
                writer.Write((byte)(anchor.NearMode ? 1 : 0));
            }

            writer.Write(body.PinnedVertices.Count);
            foreach (var pin in body.PinnedVertices)
                WriteIndex(writer, pin, vertexIndexSize);
        }

        #endregion

        #region Primitive Writers

        private static void WriteText(BinaryWriter writer, string text, int encoding)
        {
            byte[] bytes = encoding == 1
                ? Encoding.UTF8.GetBytes(text ?? "")
                : Encoding.Unicode.GetBytes(text ?? "");
            writer.Write(bytes.Length);
            writer.Write(bytes);
        }

        private static void WriteIndex(BinaryWriter writer, int index, int size)
        {
            switch (size)
            {
                case 1: writer.Write((sbyte)index); break;
                case 2: writer.Write((short)index); break;
                case 4: writer.Write(index); break;
            }
        }

        private static void WriteUnsignedIndex(BinaryWriter writer, int index, int size)
        {
            switch (size)
            {
                case 1: writer.Write((byte)index); break;
                case 2: writer.Write((ushort)index); break;
                case 4: writer.Write((uint)index); break;
            }
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

        private static void WriteQuaternion(BinaryWriter writer, Quaternion q)
        {
            writer.Write(q.x);
            writer.Write(q.y);
            writer.Write(q.z);
            writer.Write(q.w);
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

        #endregion
    }
}
