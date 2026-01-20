// MeshAttributeChangeRecord.cs
// メッシュ属性変更のUndo/Redo記録用クラス

using System;
using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Data;
using Poly_Ling.Model;

namespace Poly_Ling.UndoSystem
{
    /// <summary>
    /// メッシュ属性（IsVisible, IsLocked, MirrorType等）の変更を記録するレコード。
    /// MeshListStackで使用される。
    /// </summary>
    [Serializable]
    public class MeshAttributeChangeRecord : IUndoRecord<ModelContext>
    {
        /// <summary>操作のメタ情報</summary>
        public UndoOperationInfo Info { get; set; }

        /// <summary>対象メッシュのインデックス</summary>
        public int MeshIndex { get; set; }

        /// <summary>変更されたプロパティ名</summary>
        public string PropertyName { get; set; }

        /// <summary>変更前の値</summary>
        public object OldValue { get; set; }

        /// <summary>変更後の値</summary>
        public object NewValue { get; set; }

        /// <summary>
        /// Undo実行：変更前の値に戻す
        /// </summary>
        public void Undo(ModelContext context)
        {
            ApplyValue(context, OldValue);
        }

        /// <summary>
        /// Redo実行：変更後の値を再適用
        /// </summary>
        public void Redo(ModelContext context)
        {
            ApplyValue(context, NewValue);
        }

        /// <summary>
        /// 値を適用
        /// </summary>
        private void ApplyValue(ModelContext context, object value)
        {
            if (context == null) return;
            if (MeshIndex < 0 || MeshIndex >= context.MeshContextCount) return;

            var meshContext = context.GetMeshContext(MeshIndex);
            if (meshContext == null) return;

            switch (PropertyName)
            {
                case "IsVisible":
                    if (value is bool visible)
                        meshContext.IsVisible = visible;
                    break;

                case "IsLocked":
                    if (value is bool locked)
                        meshContext.IsLocked = locked;
                    break;

                case "MirrorType":
                    if (value is int mirrorType)
                        meshContext.MirrorType = mirrorType;
                    break;

                case "Name":
                    if (value is string name)
                        meshContext.Name = name;
                    break;

                // 必要に応じて他のプロパティを追加
            }
        }

        /// <summary>
        /// 同じ属性変更をマージ可能か判定
        /// </summary>
        public bool CanMergeWith(MeshAttributeChangeRecord other)
        {
            return other != null &&
                   MeshIndex == other.MeshIndex &&
                   PropertyName == other.PropertyName;
        }

        /// <summary>
        /// 別のレコードをマージ（連続した同一属性変更を統合）
        /// </summary>
        public void MergeWith(MeshAttributeChangeRecord other)
        {
            if (other == null) return;
            // OldValueは最初の値を維持、NewValueは最新の値に更新
            NewValue = other.NewValue;
        }

        public override string ToString()
        {
            return $"MeshAttributeChange[{MeshIndex}].{PropertyName}: {OldValue} → {NewValue}";
        }
    }

    /// <summary>
    /// 複数メッシュの選択変更を記録するレコード
    /// </summary>
    [Serializable]
    public class MeshMultiSelectionChangeRecord : IUndoRecord<ModelContext>
    {
        /// <summary>操作のメタ情報</summary>
        public UndoOperationInfo Info { get; set; }

        /// <summary>変更前の選択インデックスセット</summary>
        public int[] OldSelectedIndices { get; set; }

        /// <summary>変更後の選択インデックスセット</summary>
        public int[] NewSelectedIndices { get; set; }

        public MeshMultiSelectionChangeRecord() { }

        public MeshMultiSelectionChangeRecord(int[] oldIndices, int[] newIndices)
        {
            OldSelectedIndices = oldIndices ?? Array.Empty<int>();
            NewSelectedIndices = newIndices ?? Array.Empty<int>();
        }

        /// <summary>
        /// Undo実行：以前の選択状態に戻す
        /// </summary>
        public void Undo(ModelContext context)
        {
            if (context == null) return;

            context.SelectedMeshContextIndices.Clear();
            foreach (var index in OldSelectedIndices)
            {
                if (index >= 0 && index < context.MeshContextCount)
                {
                    context.SelectedMeshContextIndices.Add(index);
                }
            }
        }

        /// <summary>
        /// Redo実行：新しい選択状態を再適用
        /// </summary>
        public void Redo(ModelContext context)
        {
            if (context == null) return;

            context.SelectedMeshContextIndices.Clear();
            foreach (var index in NewSelectedIndices)
            {
                if (index >= 0 && index < context.MeshContextCount)
                {
                    context.SelectedMeshContextIndices.Add(index);
                }
            }
        }

        public override string ToString()
        {
            return $"MeshMultiSelectionChange: [{string.Join(",", OldSelectedIndices)}] → [{string.Join(",", NewSelectedIndices)}]";
        }
    }

    /// <summary>
    /// メッシュの順序・階層変更を記録するレコード（D&D用）
    /// </summary>
    [Serializable]
    public class MeshReorderChangeRecord : IUndoRecord<ModelContext>
    {
        /// <summary>操作のメタ情報</summary>
        public UndoOperationInfo Info { get; set; }

        /// <summary>変更前のメッシュインデックス順序</summary>
        public int[] OldOrder { get; set; }

        /// <summary>変更後のメッシュインデックス順序</summary>
        public int[] NewOrder { get; set; }

        /// <summary>変更前の親インデックスマップ（メッシュIndex → 親Index, -1=ルート）</summary>
        public Dictionary<int, int> OldParentIndices { get; set; }

        /// <summary>変更後の親インデックスマップ</summary>
        public Dictionary<int, int> NewParentIndices { get; set; }

        public MeshReorderChangeRecord()
        {
            OldOrder = Array.Empty<int>();
            NewOrder = Array.Empty<int>();
            OldParentIndices = new Dictionary<int, int>();
            NewParentIndices = new Dictionary<int, int>();
        }

        /// <summary>
        /// Undo実行：以前の順序に戻す
        /// </summary>
        public void Undo(ModelContext context)
        {
            ApplyOrder(context, OldOrder, OldParentIndices);
        }

        /// <summary>
        /// Redo実行：新しい順序を再適用
        /// </summary>
        public void Redo(ModelContext context)
        {
            ApplyOrder(context, NewOrder, NewParentIndices);
        }

        /// <summary>
        /// 順序を適用
        /// </summary>
        private void ApplyOrder(ModelContext context, int[] order, Dictionary<int, int> parentIndices)
        {
            if (context == null || order == null || order.Length == 0) return;

            // 現在のMeshContextリストを取得
            var currentList = context.MeshContextList.ToList();
            if (currentList.Count != order.Length) return;

            // インデックスでソートし直す
            var reordered = new List<MeshContext>(order.Length);
            foreach (var idx in order)
            {
                if (idx >= 0 && idx < currentList.Count)
                {
                    reordered.Add(currentList[idx]);
                }
            }

            // リストを更新
            context.MeshContextList.Clear();
            context.MeshContextList.AddRange(reordered);

            // 親インデックスを更新
            if (parentIndices != null)
            {
                for (int i = 0; i < context.MeshContextCount; i++)
                {
                    var mc = context.GetMeshContext(i);
                    if (mc != null && parentIndices.TryGetValue(i, out var parentIdx))
                    {
                        mc.HierarchyParentIndex = parentIdx;
                    }
                }
            }
        }

        public override string ToString()
        {
            return $"MeshReorderChange: [{string.Join(",", OldOrder)}] → [{string.Join(",", NewOrder)}]";
        }
    }
}
