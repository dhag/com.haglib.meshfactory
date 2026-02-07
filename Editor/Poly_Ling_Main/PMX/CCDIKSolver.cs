// CCDIKSolver.cs
// CCD法によるIKソルバー（元ライブラリ NCSHAGLIB/VMD/CCDIK.cs 準拠）
//
// ■ 座標系
// インポータがFlipZ=falseの場合、ボーン空間はPMX右手系のまま。
// VMDApplierもApplyCoordinateConversion=falseなら座標変換なし。
// つまりIK計算も右手系で行う（元ライブラリと同じ）。

using System;
using System.Collections.Generic;
using UnityEngine;
using Poly_Ling.Data;

namespace Poly_Ling.VMD
{
    public class CCDIKSolver
    {
        private Dictionary<int, Quaternion> _ikRotations = new Dictionary<int, Quaternion>();

        public void Solve(Model.ModelContext model)
        {
            if (model == null || model.MeshContextList == null)
                return;

            _footIKDebugCount = 0;

            // 前回のIK回転をクリア
            foreach (var kvp in _ikRotations)
            {
                int idx = kvp.Key;
                if (idx >= 0 && idx < model.MeshContextList.Count)
                {
                    var ctx = model.MeshContextList[idx];
                    if (ctx?.BonePoseData != null)
                        ctx.BonePoseData.ClearLayer("IK");
                }
            }
            _ikRotations.Clear();

            model.ComputeWorldMatrices();

            for (int i = 0; i < model.MeshContextList.Count; i++)
            {
                var ctx = model.MeshContextList[i];
                if (ctx == null || !ctx.IsIK || ctx.IKLinks == null || ctx.IKLinks.Count == 0)
                    continue;
                if (ctx.IKTargetIndex < 0 || ctx.IKTargetIndex >= model.MeshContextList.Count)
                    continue;

                SolveIKBone(model, i);
            }
        }

        private void SolveIKBone(Model.ModelContext model, int ikBoneIndex)
        {
            var ikBone = model.MeshContextList[ikBoneIndex];

            bool isFootIK = ikBone.Name != null && ikBone.Name.Contains("足ＩＫ");
            if (isFootIK)
            {
                int eff = ikBone.IKTargetIndex;
                Vector3 tgtPos = GetWorldPosition(model.MeshContextList[ikBoneIndex]);
                Vector3 effPos = GetWorldPosition(model.MeshContextList[eff]);
                Debug.Log($"[CCDIK] START '{ikBone.Name}' tgtPos={tgtPos} effPos={effPos} dist={Vector3.Distance(tgtPos, effPos):F4}");
            }

            for (int it = 0; it < ikBone.IKLoopCount; it++)
            {
                IKLoop(model, ikBoneIndex, it);
            }

            if (isFootIK)
            {
                int eff = ikBone.IKTargetIndex;
                Vector3 tgtPos = GetWorldPosition(model.MeshContextList[ikBoneIndex]);
                Vector3 effPos = GetWorldPosition(model.MeshContextList[eff]);
                Debug.Log($"[CCDIK] END '{ikBone.Name}' tgtPos={tgtPos} effPos={effPos} dist={Vector3.Distance(tgtPos, effPos):F4}");
            }

            if (isFootIK)
                _footIKDebugCount++;
        }

        private void IKLoop(Model.ModelContext model, int ikBoneIndex, int iteration)
        {
            var ikBone = model.MeshContextList[ikBoneIndex];
            int effectorIndex = ikBone.IKTargetIndex;

            Vector3 ikBonePosition = GetWorldPosition(model.MeshContextList[ikBoneIndex]);
            Vector3 effectorPosition = GetWorldPosition(model.MeshContextList[effectorIndex]);

            bool isFootIK = ikBone.Name != null && ikBone.Name.Contains("足ＩＫ");

            foreach (var link in ikBone.IKLinks)
            {
                if (link.BoneIndex < 0 || link.BoneIndex >= model.MeshContextList.Count)
                    continue;

                var linkCtx = model.MeshContextList[link.BoneIndex];
                if (linkCtx.BonePoseData == null)
                    linkCtx.BonePoseData = new BonePoseData();

                Matrix4x4 linkGlobal = linkCtx.WorldMatrix;
                Vector3 linkPosition = GetWorldPosition(linkCtx);

                // ワールド→リンクローカル変換
                Matrix4x4 toLinkLocal = linkGlobal.inverse;

                // リンクローカル空間での各位置
                Vector3 linkLocal = toLinkLocal.MultiplyPoint3x4(linkPosition);      // ≈ (0,0,0)
                Vector3 effectorLocal = toLinkLocal.MultiplyPoint3x4(effectorPosition);
                Vector3 targetLocal = toLinkLocal.MultiplyPoint3x4(ikBonePosition);

                // 方向ベクトル
                Vector3 link2Effector = (effectorLocal - linkLocal).normalized;
                Vector3 link2Target = (targetLocal - linkLocal).normalized;

                // 回転角度
                float dot = Vector3.Dot(link2Effector, link2Target);
                dot = Mathf.Clamp(dot, -1f, 1f);
                float rotationAngle = Mathf.Acos(dot);
                rotationAngle = Mathf.Clamp(rotationAngle, -ikBone.IKLimitAngle, ikBone.IKLimitAngle);
                if (float.IsNaN(rotationAngle) || rotationAngle <= 1.0e-3f)
                    continue;

                // 回転軸
                Vector3 rotationAxis = Vector3.Cross(link2Effector, link2Target);
                if (rotationAxis.sqrMagnitude < 1e-10f)
                    continue;
                rotationAxis.Normalize();

                // ローカル空間での回転Quaternion
                Quaternion rotation = Quaternion.AngleAxis(rotationAngle * Mathf.Rad2Deg, rotationAxis).normalized;

                // IK回転を累積（左掛け = 元ライブラリの rotation * LocalRotation）
                if (!_ikRotations.ContainsKey(link.BoneIndex))
                    _ikRotations[link.BoneIndex] = Quaternion.identity;
                _ikRotations[link.BoneIndex] = rotation * _ikRotations[link.BoneIndex];

                // BonePoseDataのIKレイヤーに設定
                linkCtx.BonePoseData.IsActive = true;
                linkCtx.BonePoseData.SetLayerRotation("IK", _ikRotations[link.BoneIndex]);

                // デバッグ（1回目のIKLoopの左足IKのみ）
                if (isFootIK && _footIKDebugCount < 1 && iteration == 0)
                {
                    var q = _ikRotations[link.BoneIndex];
                    Debug.Log($"[CCDIK DETAIL] link='{linkCtx.Name}' effWorld={effectorPosition} tgtWorld={ikBonePosition} linkWorld={linkPosition}");
                    Debug.Log($"[CCDIK DETAIL]   effLocal={effectorLocal} tgtLocal={targetLocal} linkLocal={linkLocal}");
                    Debug.Log($"[CCDIK DETAIL]   l2e={link2Effector} l2t={link2Target}");
                    Debug.Log($"[CCDIK DETAIL]   axis={rotationAxis} angle={rotationAngle * Mathf.Rad2Deg:F2}° dot={dot:F6}");
                    Debug.Log($"[CCDIK DETAIL]   ikRot=({q.x:F4},{q.y:F4},{q.z:F4},{q.w:F4}) hasLimit={link.HasLimit}");
                }

                // 角度制限（元ライブラリのRestrictRotation相当）
                if (link.HasLimit)
                {
                    Quaternion before = _ikRotations[link.BoneIndex];
                    Quaternion restricted = RestrictRotation(
                        _ikRotations[link.BoneIndex], link.LimitMin, link.LimitMax);
                    _ikRotations[link.BoneIndex] = restricted;
                    linkCtx.BonePoseData.SetLayerRotation("IK", restricted);

                    if (isFootIK && _footIKDebugCount < 1 && iteration == 0)
                    {
                        Debug.Log($"[CCDIK DETAIL]   limit min={link.LimitMin} max={link.LimitMax}");
                        Debug.Log($"[CCDIK DETAIL]   before=({before.x:F4},{before.y:F4},{before.z:F4},{before.w:F4})");
                        Debug.Log($"[CCDIK DETAIL]   after =({restricted.x:F4},{restricted.y:F4},{restricted.z:F4},{restricted.w:F4})");
                    }
                }
            }

            // 全リンク処理後にWorldMatrix再計算
            model.ComputeWorldMatrices();
        }

        private int _footIKDebugCount = 0;

        private Vector3 GetWorldPosition(MeshContext ctx)
        {
            return ctx.WorldMatrix.GetColumn(3);
        }

        // =================================================================
        // RestrictRotation — 元ライブラリ QuaternionHelper.RestrictRotation 準拠
        // =================================================================

        /// <summary>
        /// IK累積回転をオイラー角に分解し、LimitMin/LimitMax（ラジアン）でクランプ
        /// </summary>
        private Quaternion RestrictRotation(Quaternion rotation, Vector3 limitMin, Vector3 limitMax)
        {
            float xRot, yRot, zRot;
            int type = SplitRotation(rotation, out xRot, out yRot, out zRot);

            // NormalizeEular: -PI～PI の範囲に正規化
            xRot = NormalizeAngle(xRot, -Mathf.PI, Mathf.PI);
            yRot = NormalizeAngle(yRot, -Mathf.PI * 0.5f, Mathf.PI * 0.5f);
            zRot = NormalizeAngle(zRot, -Mathf.PI, Mathf.PI);

            // クランプ（ラジアン）
            xRot = Mathf.Clamp(xRot, limitMin.x, limitMax.x);
            yRot = Mathf.Clamp(yRot, limitMin.y, limitMax.y);
            zRot = Mathf.Clamp(zRot, limitMin.z, limitMax.z);

            // Quaternion再構成
            // 元ライブラリ（行優先）: RotX * RotY * RotZ
            // Unity（列優先）等価: RotZ * RotY * RotX
            Quaternion result;
            switch (type)
            {
                case 0: // XYZ → Unity: Z * Y * X
                    result = Quaternion.AngleAxis(zRot * Mathf.Rad2Deg, Vector3.forward)
                           * Quaternion.AngleAxis(yRot * Mathf.Rad2Deg, Vector3.up)
                           * Quaternion.AngleAxis(xRot * Mathf.Rad2Deg, Vector3.right);
                    break;
                case 1: // YZX → Unity: X * Z * Y
                    result = Quaternion.AngleAxis(xRot * Mathf.Rad2Deg, Vector3.right)
                           * Quaternion.AngleAxis(zRot * Mathf.Rad2Deg, Vector3.forward)
                           * Quaternion.AngleAxis(yRot * Mathf.Rad2Deg, Vector3.up);
                    break;
                default: // ZXY → Unity: Y * X * Z
                    result = Quaternion.AngleAxis(yRot * Mathf.Rad2Deg, Vector3.up)
                           * Quaternion.AngleAxis(xRot * Mathf.Rad2Deg, Vector3.right)
                           * Quaternion.AngleAxis(zRot * Mathf.Rad2Deg, Vector3.forward);
                    break;
            }
            return result.normalized;
        }

        private float NormalizeAngle(float angle, float min, float max)
        {
            if (angle < min) angle += Mathf.PI * 2f;
            else if (angle > max) angle -= Mathf.PI * 2f;
            return angle;
        }

        // =================================================================
        // SplitRotation — 元ライブラリ QuaternionHelper.SplitRotation 準拠
        // 行列要素アクセス: 元Mij = Unity m[j-1,i-1] (転置)
        // =================================================================

        private int SplitRotation(Quaternion rotation, out float xRot, out float yRot, out float zRot)
        {
            if (FactoringXYZ(rotation, out xRot, out yRot, out zRot)) return 0;
            if (FactoringYZX(rotation, out xRot, out yRot, out zRot)) return 1;
            FactoringZXY(rotation, out xRot, out yRot, out zRot);
            return 2;
        }

        /// <summary>XYZ順でオイラー分解（元ライブラリFactoringQuaternionXYZ準拠）</summary>
        private bool FactoringXYZ(Quaternion q, out float xRot, out float yRot, out float zRot)
        {
            Matrix4x4 rot = Matrix4x4.Rotate(q.normalized);
            // 元M13 = Unity m[0,2] = rot.m20 ... 転置: 元Mij = Unity m(j-1,i-1)
            // 元M13 → row=1,col=3 → Unity m[2,0] = rot.m20
            // 元M23 → row=2,col=3 → Unity m[2,1] = rot.m21
            // 元M33 → row=3,col=3 → Unity m[2,2] = rot.m22
            // 元M12 → row=1,col=2 → Unity m[1,0] = rot.m10
            // 元M11 → row=1,col=1 → Unity m[0,0] = rot.m00
            // 元M21 → row=2,col=1 → Unity m[0,1] = rot.m01
            // 元M22 → row=2,col=2 → Unity m[1,1] = rot.m11

            float m13 = rot.m20; // 元M13
            if (m13 > 1f - 1.0e-4f || m13 < -1f + 1.0e-4f)
            {
                xRot = 0;
                yRot = m13 < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f;
                zRot = -Mathf.Atan2(-rot.m01, rot.m11); // 元-Atan2(-M21, M22)
                return false;
            }
            yRot = -Mathf.Asin(m13);
            float cosY = Mathf.Cos(yRot);
            xRot = Mathf.Asin(rot.m21 / cosY); // 元M23
            if (float.IsNaN(xRot))
            {
                xRot = 0;
                yRot = m13 < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f;
                zRot = -Mathf.Atan2(-rot.m01, rot.m11);
                return false;
            }
            if (rot.m22 < 0) // 元M33
                xRot = Mathf.PI - xRot;
            zRot = Mathf.Atan2(rot.m10, rot.m00); // 元Atan2(M12, M11)
            return true;
        }

        /// <summary>YZX順でオイラー分解（元ライブラリFactoringQuaternionYZX準拠）</summary>
        private bool FactoringYZX(Quaternion q, out float xRot, out float yRot, out float zRot)
        {
            Matrix4x4 rot = Matrix4x4.Rotate(q.normalized);
            // 元M21 → Unity m[0,1] = rot.m01
            // 元M31 → Unity m[0,2] = rot.m02
            // 元M11 → Unity m[0,0] = rot.m00
            // 元M23 → Unity m[2,1] = rot.m21
            // 元M22 → Unity m[1,1] = rot.m11
            // 元M32 → Unity m[1,2] = rot.m12
            // 元M33 → Unity m[2,2] = rot.m22

            float m21 = rot.m01; // 元M21
            if (m21 > 1f - 1.0e-4f || m21 < -1f + 1.0e-4f)
            {
                yRot = 0;
                zRot = m21 < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f;
                xRot = -Mathf.Atan2(-rot.m12, rot.m22); // 元-Atan2(-M32, M33)
                return false;
            }
            zRot = -Mathf.Asin(m21);
            float cosZ = Mathf.Cos(zRot);
            yRot = Mathf.Asin(rot.m02 / cosZ); // 元M31
            if (float.IsNaN(yRot))
            {
                yRot = 0;
                zRot = m21 < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f;
                xRot = -Mathf.Atan2(-rot.m12, rot.m22);
                return false;
            }
            if (rot.m00 < 0) // 元M11
                yRot = Mathf.PI - yRot;
            xRot = Mathf.Atan2(rot.m21, rot.m11); // 元Atan2(M23, M22)
            return true;
        }

        /// <summary>ZXY順でオイラー分解（元ライブラリFactoringQuaternionZXY準拠）</summary>
        private void FactoringZXY(Quaternion q, out float xRot, out float yRot, out float zRot)
        {
            Matrix4x4 rot = Matrix4x4.Rotate(q.normalized);
            // 元M32 → Unity m[1,2] = rot.m12
            // 元M12 → Unity m[1,0] = rot.m10
            // 元M22 → Unity m[1,1] = rot.m11
            // 元M31 → Unity m[0,2] = rot.m02
            // 元M33 → Unity m[2,2] = rot.m22
            // 元M13 → Unity m[2,0] = rot.m20
            // 元M11 → Unity m[0,0] = rot.m00

            float m32 = rot.m12; // 元M32
            if (m32 > 1f - 1.0e-4f || m32 < -1f + 1.0e-4f)
            {
                xRot = m32 < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f;
                zRot = 0;
                yRot = Mathf.Atan2(-rot.m20, rot.m00); // 元Atan2(-M13, M11)
                return;
            }
            xRot = -Mathf.Asin(m32);
            float cosX = Mathf.Cos(xRot);
            zRot = Mathf.Asin(rot.m10 / cosX); // 元M12
            if (float.IsNaN(zRot))
            {
                xRot = m32 < 0 ? Mathf.PI / 2f : -Mathf.PI / 2f;
                zRot = 0;
                yRot = Mathf.Atan2(-rot.m20, rot.m00);
                return;
            }
            if (rot.m11 < 0) // 元M22
                zRot = Mathf.PI - zRot;
            yRot = Mathf.Atan2(rot.m02, rot.m22); // 元Atan2(M31, M33)
        }
    }
}