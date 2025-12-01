// Assets/Editor/UndoSystem/Core/UndoGroup.cs
// 複数のUndoノードを束ねるグループ実装

using System;
using System.Collections.Generic;
using System.Linq;

namespace MeshEditor.UndoSystem
{
    /// <summary>
    /// Undoノードのグループ
    /// 複数のスタックや子グループを束ねて、調停しながらUndo/Redoを実行
    /// </summary>
    public class UndoGroup : IUndoGroup
    {
        // === フィールド ===
        private readonly List<IUndoNode> _children = new();
        private string _focusedChildId;

        // === プロパティ: IUndoNode ===
        public string Id { get; }
        public string DisplayName { get; set; }
        public IUndoNode Parent { get; set; }

        public bool CanUndo => _children.Any(c => c.CanUndo);
        public bool CanRedo => _children.Any(c => c.CanRedo);

        public UndoOperationInfo LatestOperation
        {
            get
            {
                return _children
                    .Select(c => c.LatestOperation)
                    .Where(op => op != null)
                    .OrderByDescending(op => op.Timestamp)
                    .FirstOrDefault();
            }
        }

        // === プロパティ: IUndoGroup ===
        public IReadOnlyList<IUndoNode> Children => _children;
        
        public string FocusedChildId
        {
            get => _focusedChildId;
            set
            {
                if (_focusedChildId != value)
                {
                    _focusedChildId = value;
                    OnFocusChanged?.Invoke(value);
                }
            }
        }

        public UndoResolutionPolicy ResolutionPolicy { get; set; } = UndoResolutionPolicy.FocusThenTimestamp;

        // === イベント ===
        public event Action<UndoOperationInfo> OnUndoPerformed;
        public event Action<UndoOperationInfo> OnRedoPerformed;
        public event Action<UndoOperationInfo> OnOperationRecorded;
        public event Action<string> OnFocusChanged;

        // === コンストラクタ ===
        public UndoGroup(string id, string displayName = null)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = displayName ?? id;
        }

        // === 子ノード管理 ===

        /// <summary>
        /// 子ノードを追加
        /// </summary>
        public void AddChild(IUndoNode child)
        {
            if (child == null)
                throw new ArgumentNullException(nameof(child));

            if (_children.Any(c => c.Id == child.Id))
                throw new InvalidOperationException($"Child with ID '{child.Id}' already exists");

            child.Parent = this;
            _children.Add(child);

            // イベント転送
            child.OnUndoPerformed += info => OnUndoPerformed?.Invoke(info);
            child.OnRedoPerformed += info => OnRedoPerformed?.Invoke(info);
            child.OnOperationRecorded += info => OnOperationRecorded?.Invoke(info);
        }

        /// <summary>
        /// 子ノードを削除
        /// </summary>
        public bool RemoveChild(IUndoNode child)
        {
            if (child == null)
                return false;

            var removed = _children.Remove(child);
            if (removed)
            {
                child.Parent = null;
                if (_focusedChildId == child.Id)
                    _focusedChildId = null;
            }
            return removed;
        }

        /// <summary>
        /// IDで子ノードを検索（再帰）
        /// </summary>
        public IUndoNode FindById(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;

            foreach (var child in _children)
            {
                if (child.Id == id)
                    return child;

                if (child is IUndoGroup group)
                {
                    var found = group.FindById(id);
                    if (found != null)
                        return found;
                }
            }

            return null;
        }

        // === Undo/Redo実行 ===

        /// <summary>
        /// Undo実行（調停ポリシーに基づく）
        /// </summary>
        public bool PerformUndo()
        {
            var target = ResolveUndoTarget();
            return target?.PerformUndo() ?? false;
        }

        /// <summary>
        /// Redo実行（調停ポリシーに基づく）
        /// </summary>
        public bool PerformRedo()
        {
            var target = ResolveRedoTarget();
            return target?.PerformRedo() ?? false;
        }

        /// <summary>
        /// 全履歴をクリア
        /// </summary>
        public void Clear()
        {
            foreach (var child in _children)
            {
                child.Clear();
            }
        }

        // === 調停ロジック ===

        /// <summary>
        /// Undo対象ノードを調停して決定
        /// </summary>
        private IUndoNode ResolveUndoTarget()
        {
            switch (ResolutionPolicy)
            {
                case UndoResolutionPolicy.FocusPriority:
                    return ResolveFocusPriority(n => n.CanUndo);

                case UndoResolutionPolicy.TimestampPriority:
                    return ResolveTimestampPriority(n => n.CanUndo);

                case UndoResolutionPolicy.FocusThenTimestamp:
                default:
                    return ResolveFocusThenTimestamp(n => n.CanUndo);
            }
        }

        /// <summary>
        /// Redo対象ノードを調停して決定
        /// </summary>
        private IUndoNode ResolveRedoTarget()
        {
            // Redoは基本的にUndoと同じノードで行う
            // ただしRedoスタックを持つノードを優先
            switch (ResolutionPolicy)
            {
                case UndoResolutionPolicy.FocusPriority:
                    return ResolveFocusPriority(n => n.CanRedo);

                case UndoResolutionPolicy.TimestampPriority:
                    return ResolveTimestampPriorityForRedo();

                case UndoResolutionPolicy.FocusThenTimestamp:
                default:
                    return ResolveFocusThenTimestamp(n => n.CanRedo);
            }
        }

        private IUndoNode ResolveFocusPriority(Func<IUndoNode, bool> canPerform)
        {
            // フォーカス中のノードが操作可能ならそれを返す
            if (!string.IsNullOrEmpty(_focusedChildId))
            {
                var focused = FindById(_focusedChildId);
                if (focused != null && canPerform(focused))
                    return focused;
            }
            return null;
        }

        private IUndoNode ResolveTimestampPriority(Func<IUndoNode, bool> canPerform)
        {
            // 最新タイムスタンプのノードを返す
            return _children
                .Where(canPerform)
                .Where(c => c.LatestOperation != null)
                .OrderByDescending(c => c.LatestOperation.Timestamp)
                .FirstOrDefault();
        }

        private IUndoNode ResolveTimestampPriorityForRedo()
        {
            // Redo: 最も古いRedo操作を持つノードを優先（Undoの逆順）
            // 実装上はCanRedoがtrueのノードから選ぶ
            return _children
                .Where(c => c.CanRedo)
                .FirstOrDefault();
        }

        private IUndoNode ResolveFocusThenTimestamp(Func<IUndoNode, bool> canPerform)
        {
            // フォーカス優先、なければタイムスタンプ
            var focused = ResolveFocusPriority(canPerform);
            if (focused != null)
                return focused;

            return ResolveTimestampPriority(canPerform);
        }

        // === ユーティリティ ===

        /// <summary>
        /// 特定のスタックでUndo
        /// </summary>
        public bool PerformUndoOn(string stackId)
        {
            var node = FindById(stackId);
            return node?.PerformUndo() ?? false;
        }

        /// <summary>
        /// 特定のスタックでRedo
        /// </summary>
        public bool PerformRedoOn(string stackId)
        {
            var node = FindById(stackId);
            return node?.PerformRedo() ?? false;
        }

        /// <summary>
        /// 子ノードを型指定で取得
        /// </summary>
        public T GetChild<T>(string id) where T : class, IUndoNode
        {
            return FindById(id) as T;
        }

        // === デバッグ用 ===

        /// <summary>
        /// ツリー構造を文字列で取得
        /// </summary>
        public string GetTreeInfo(int indent = 0)
        {
            var prefix = new string(' ', indent * 2);
            var focusMark = _focusedChildId != null ? $" (Focus: {_focusedChildId})" : "";
            var result = $"{prefix}[Group] {Id}{focusMark}\n";

            foreach (var child in _children)
            {
                if (child is UndoGroup childGroup)
                {
                    result += childGroup.GetTreeInfo(indent + 1);
                }
                else if (child is IUndoNode node)
                {
                    var canUndo = node.CanUndo ? "U" : "-";
                    var canRedo = node.CanRedo ? "R" : "-";
                    result += $"{prefix}  [{canUndo}{canRedo}] {child.Id}: {child.DisplayName}\n";
                }
            }

            return result;
        }
    }
}
