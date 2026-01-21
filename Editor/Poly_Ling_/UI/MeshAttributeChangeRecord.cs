// MeshAttributeChangeRecord.cs
// メッシュ選択・順序変更のUndo/Redo記録用クラス
// Note: 属性変更はMeshAttributesBatchChangeRecord（MeshListRecords.cs）に統合

using System;
using System.Collections.Generic;
using System.Linq;
using Poly_Ling.Data;
using Poly_Ling.Model;

namespace Poly_Ling.UndoSystem
{
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
