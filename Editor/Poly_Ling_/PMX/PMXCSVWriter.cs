// Assets/Editor/Poly_Ling/PMX/Export/PMXCSVWriter.cs
// PMX CSV形式での出力

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

namespace Poly_Ling.PMX
{
    /// <summary>
    /// PMX CSVファイルライター
    /// PMXEditorと互換のCSV形式で出力
    /// </summary>
    public static class PMXCSVWriter
    {
        /// <summary>
        /// PMXドキュメントをCSV形式で保存
        /// </summary>
        public static void Save(PMXDocument document, string filePath, int decimalPrecision = 6)
        {
            var sb = new StringBuilder();
            string fmt = $"F{decimalPrecision}";

            WriteHeader(sb, document);
            WriteModelInfo(sb, document.ModelInfo);
            WriteVertices(sb, document, fmt);
            WriteFaces(sb, document);
            WriteMaterials(sb, document, fmt);
            WriteBones(sb, document, fmt);
            WriteMorphs(sb, document, fmt);
            WriteBodies(sb, document, fmt);
            WriteJoints(sb, document, fmt);

            File.WriteAllText(filePath, sb.ToString(), Encoding.UTF8);
            Debug.Log($"[PMXCSVWriter] Saved to: {filePath}");
        }

        // ================================================================
        // ヘッダー
        // ================================================================

        private static void WriteHeader(StringBuilder sb, PMXDocument document)
        {
            sb.AppendLine("Pmx,version,CharacterEncoding,UvCount");
            sb.AppendLine($"Pmx,{document.Version:F1},{document.CharacterEncoding},{document.AdditionalUVCount}");
            sb.AppendLine();
        }

        // ================================================================
        // モデル情報
        // ================================================================

        private static void WriteModelInfo(StringBuilder sb, PMXModelInfo info)
        {
            sb.AppendLine("ModelInfo,Name,NameE,Comment,CommentE");
            sb.AppendLine($"ModelInfo,{Escape(info.Name)},{Escape(info.NameEnglish)},{Escape(info.Comment)},{Escape(info.CommentEnglish)}");
            sb.AppendLine();
        }

        // ================================================================
        // 頂点
        // ================================================================

        private static void WriteVertices(StringBuilder sb, PMXDocument document, string fmt)
        {
            sb.AppendLine("Vertex,Index,Position.x,Position.y,Position.z,Normal.x,Normal.y,Normal.z,UV.x,UV.y,EdgeScale,WeightType,Bone1,Bone2,Bone3,Bone4,Weight1,Weight2,Weight3,Weight4");

            foreach (var vertex in document.Vertices)
            {
                var bw = vertex.BoneWeights ?? new PMXBoneWeight[0];

                string bone1 = bw.Length > 0 ? Escape(bw[0].BoneName) : "";
                string bone2 = bw.Length > 1 ? Escape(bw[1].BoneName) : "";
                string bone3 = bw.Length > 2 ? Escape(bw[2].BoneName) : "";
                string bone4 = bw.Length > 3 ? Escape(bw[3].BoneName) : "";

                float w1 = bw.Length > 0 ? bw[0].Weight : 0;
                float w2 = bw.Length > 1 ? bw[1].Weight : 0;
                float w3 = bw.Length > 2 ? bw[2].Weight : 0;
                float w4 = bw.Length > 3 ? bw[3].Weight : 0;

                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Vertex,{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18}",
                    vertex.Index,
                    vertex.Position.x.ToString(fmt, CultureInfo.InvariantCulture),
                    vertex.Position.y.ToString(fmt, CultureInfo.InvariantCulture),
                    vertex.Position.z.ToString(fmt, CultureInfo.InvariantCulture),
                    vertex.Normal.x.ToString(fmt, CultureInfo.InvariantCulture),
                    vertex.Normal.y.ToString(fmt, CultureInfo.InvariantCulture),
                    vertex.Normal.z.ToString(fmt, CultureInfo.InvariantCulture),
                    vertex.UV.x.ToString(fmt, CultureInfo.InvariantCulture),
                    vertex.UV.y.ToString(fmt, CultureInfo.InvariantCulture),
                    vertex.EdgeScale.ToString(fmt, CultureInfo.InvariantCulture),
                    vertex.WeightType,
                    bone1, bone2, bone3, bone4,
                    w1.ToString(fmt, CultureInfo.InvariantCulture),
                    w2.ToString(fmt, CultureInfo.InvariantCulture),
                    w3.ToString(fmt, CultureInfo.InvariantCulture),
                    w4.ToString(fmt, CultureInfo.InvariantCulture)
                ));
            }
            sb.AppendLine();
        }

        // ================================================================
        // 面
        // ================================================================

        private static void WriteFaces(StringBuilder sb, PMXDocument document)
        {
            sb.AppendLine("Face,MaterialName,FaceIndex,V1,V2,V3");

            foreach (var face in document.Faces)
            {
                sb.AppendLine($"Face,{Escape(face.MaterialName)},{face.FaceIndex},{face.VertexIndex1},{face.VertexIndex2},{face.VertexIndex3}");
            }
            sb.AppendLine();
        }

        // ================================================================
        // マテリアル
        // ================================================================

        private static void WriteMaterials(StringBuilder sb, PMXDocument document, string fmt)
        {
            sb.AppendLine("Material,Name,NameE,Diffuse.R,Diffuse.G,Diffuse.B,Diffuse.A,Specular.R,Specular.G,Specular.B,SpecularPower,Ambient.R,Ambient.G,Ambient.B,DrawFlags,EdgeColor.R,EdgeColor.G,EdgeColor.B,EdgeColor.A,EdgeSize,Texture,SphereTexture,SphereMode,SharedToon,ToonTexture,Memo,FaceCount");

            foreach (var mat in document.Materials)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Material,{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25}",
                    Escape(mat.Name),
                    Escape(mat.NameEnglish),
                    mat.Diffuse.r.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.Diffuse.g.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.Diffuse.b.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.Diffuse.a.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.Specular.r.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.Specular.g.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.Specular.b.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.SpecularPower.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.Ambient.r.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.Ambient.g.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.Ambient.b.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.DrawFlags,
                    mat.EdgeColor.r.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.EdgeColor.g.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.EdgeColor.b.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.EdgeColor.a.ToString(fmt, CultureInfo.InvariantCulture),
                    mat.EdgeSize.ToString(fmt, CultureInfo.InvariantCulture),
                    Escape(mat.TexturePath),
                    Escape(mat.SphereTexturePath),
                    mat.SphereMode,
                    mat.SharedToon ? 1 : 0,
                    mat.SharedToon ? mat.ToonTextureIndex.ToString() : Escape(mat.ToonTexturePath),
                    Escape(mat.Memo),
                    mat.FaceCount
                ));
            }
            sb.AppendLine();
        }

        // ================================================================
        // ボーン
        // ================================================================

        private static void WriteBones(StringBuilder sb, PMXDocument document, string fmt)
        {
            sb.AppendLine("Bone,Name,NameE,Position.x,Position.y,Position.z,ParentBone,TransformLevel,Flags,ConnectBone,ConnectOffset.x,ConnectOffset.y,ConnectOffset.z,GrantParent,GrantRate,FixedAxis.x,FixedAxis.y,FixedAxis.z,LocalAxisX.x,LocalAxisX.y,LocalAxisX.z,LocalAxisZ.x,LocalAxisZ.y,LocalAxisZ.z,ExternalParentKey,IKTarget,IKLoopCount,IKLimitAngle");

            foreach (var bone in document.Bones)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Bone,{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25},{26}",
                    Escape(bone.Name),
                    Escape(bone.NameEnglish),
                    bone.Position.x.ToString(fmt, CultureInfo.InvariantCulture),
                    bone.Position.y.ToString(fmt, CultureInfo.InvariantCulture),
                    bone.Position.z.ToString(fmt, CultureInfo.InvariantCulture),
                    Escape(bone.ParentBoneName),
                    bone.TransformLevel,
                    bone.Flags,
                    Escape(bone.ConnectBoneName),
                    bone.ConnectOffset.x.ToString(fmt, CultureInfo.InvariantCulture),
                    bone.ConnectOffset.y.ToString(fmt, CultureInfo.InvariantCulture),
                    bone.ConnectOffset.z.ToString(fmt, CultureInfo.InvariantCulture),
                    Escape(bone.GrantParentBoneName),
                    bone.GrantRate.ToString(fmt, CultureInfo.InvariantCulture),
                    bone.FixedAxis.x.ToString(fmt, CultureInfo.InvariantCulture),
                    bone.FixedAxis.y.ToString(fmt, CultureInfo.InvariantCulture),
                    bone.FixedAxis.z.ToString(fmt, CultureInfo.InvariantCulture),
                    bone.LocalAxisX.x.ToString(fmt, CultureInfo.InvariantCulture),
                    bone.LocalAxisX.y.ToString(fmt, CultureInfo.InvariantCulture),
                    bone.LocalAxisX.z.ToString(fmt, CultureInfo.InvariantCulture),
                    bone.LocalAxisZ.x.ToString(fmt, CultureInfo.InvariantCulture),
                    bone.LocalAxisZ.y.ToString(fmt, CultureInfo.InvariantCulture),
                    bone.LocalAxisZ.z.ToString(fmt, CultureInfo.InvariantCulture),
                    bone.ExternalParentKey,
                    Escape(bone.IKTargetBoneName),
                    bone.IKLoopCount,
                    bone.IKLimitAngle.ToString(fmt, CultureInfo.InvariantCulture)
                ));
            }
            sb.AppendLine();
        }

        // ================================================================
        // モーフ
        // ================================================================

        private static void WriteMorphs(StringBuilder sb, PMXDocument document, string fmt)
        {
            sb.AppendLine("Morph,Name,NameE,Panel,MorphType");

            foreach (var morph in document.Morphs)
            {
                sb.AppendLine($"Morph,{Escape(morph.Name)},{Escape(morph.NameEnglish)},{morph.Panel},{morph.MorphType}");

                // 頂点モーフオフセット
                if (morph.MorphType == 1)  // 頂点モーフ
                {
                    foreach (var offset in morph.Offsets)
                    {
                        if (offset is PMXVertexMorphOffset vertexOffset)
                        {
                            sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                                "MorphOffset,{0},{1},{2},{3}",
                                vertexOffset.VertexIndex,
                                vertexOffset.Offset.x.ToString(fmt, CultureInfo.InvariantCulture),
                                vertexOffset.Offset.y.ToString(fmt, CultureInfo.InvariantCulture),
                                vertexOffset.Offset.z.ToString(fmt, CultureInfo.InvariantCulture)
                            ));
                        }
                    }
                }
            }
            sb.AppendLine();
        }

        // ================================================================
        // 剛体
        // ================================================================

        private static void WriteBodies(StringBuilder sb, PMXDocument document, string fmt)
        {
            sb.AppendLine("Body,Name,NameE,RelatedBone,BodyType,Group,NonCollisionGroups,Shape,Size.x,Size.y,Size.z,Position.x,Position.y,Position.z,Rotation.x,Rotation.y,Rotation.z,Mass,LinearDamping,AngularDamping,Restitution,Friction");

            foreach (var body in document.RigidBodies)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Body,{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20}",
                    Escape(body.Name),
                    Escape(body.NameEnglish),
                    Escape(body.RelatedBoneName),
                    body.PhysicsMode,
                    body.Group,
                    Escape(body.NonCollisionGroups),
                    body.Shape,
                    body.Size.x.ToString(fmt, CultureInfo.InvariantCulture),
                    body.Size.y.ToString(fmt, CultureInfo.InvariantCulture),
                    body.Size.z.ToString(fmt, CultureInfo.InvariantCulture),
                    body.Position.x.ToString(fmt, CultureInfo.InvariantCulture),
                    body.Position.y.ToString(fmt, CultureInfo.InvariantCulture),
                    body.Position.z.ToString(fmt, CultureInfo.InvariantCulture),
                    body.Rotation.x.ToString(fmt, CultureInfo.InvariantCulture),
                    body.Rotation.y.ToString(fmt, CultureInfo.InvariantCulture),
                    body.Rotation.z.ToString(fmt, CultureInfo.InvariantCulture),
                    body.Mass.ToString(fmt, CultureInfo.InvariantCulture),
                    body.LinearDamping.ToString(fmt, CultureInfo.InvariantCulture),
                    body.AngularDamping.ToString(fmt, CultureInfo.InvariantCulture),
                    body.Restitution.ToString(fmt, CultureInfo.InvariantCulture),
                    body.Friction.ToString(fmt, CultureInfo.InvariantCulture)
                ));
            }
            sb.AppendLine();
        }

        // ================================================================
        // ジョイント
        // ================================================================

        private static void WriteJoints(StringBuilder sb, PMXDocument document, string fmt)
        {
            sb.AppendLine("Joint,Name,NameE,BodyA,BodyB,JointType,Position.x,Position.y,Position.z,Rotation.x,Rotation.y,Rotation.z,TranslationMin.x,TranslationMin.y,TranslationMin.z,TranslationMax.x,TranslationMax.y,TranslationMax.z,RotationMin.x,RotationMin.y,RotationMin.z,RotationMax.x,RotationMax.y,RotationMax.z,SpringTranslation.x,SpringTranslation.y,SpringTranslation.z,SpringRotation.x,SpringRotation.y,SpringRotation.z");

            foreach (var joint in document.Joints)
            {
                sb.AppendLine(string.Format(CultureInfo.InvariantCulture,
                    "Joint,{0},{1},{2},{3},{4},{5},{6},{7},{8},{9},{10},{11},{12},{13},{14},{15},{16},{17},{18},{19},{20},{21},{22},{23},{24},{25},{26},{27},{28}",
                    Escape(joint.Name),
                    Escape(joint.NameEnglish),
                    Escape(joint.BodyAName),
                    Escape(joint.BodyBName),
                    joint.JointType,
                    joint.Position.x.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.Position.y.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.Position.z.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.Rotation.x.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.Rotation.y.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.Rotation.z.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.TranslationMin.x.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.TranslationMin.y.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.TranslationMin.z.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.TranslationMax.x.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.TranslationMax.y.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.TranslationMax.z.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.RotationMin.x.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.RotationMin.y.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.RotationMin.z.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.RotationMax.x.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.RotationMax.y.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.RotationMax.z.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.SpringTranslation.x.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.SpringTranslation.y.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.SpringTranslation.z.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.SpringRotation.x.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.SpringRotation.y.ToString(fmt, CultureInfo.InvariantCulture),
                    joint.SpringRotation.z.ToString(fmt, CultureInfo.InvariantCulture)
                ));
            }
            sb.AppendLine();
        }

        // ================================================================
        // ヘルパー
        // ================================================================

        private static string Escape(string text)
        {
            if (string.IsNullOrEmpty(text))
                return "";

            // カンマや改行を含む場合はダブルクォートで囲む
            if (text.Contains(",") || text.Contains("\n") || text.Contains("\""))
            {
                return "\"" + text.Replace("\"", "\"\"") + "\"";
            }
            return text;
        }
    }
}
