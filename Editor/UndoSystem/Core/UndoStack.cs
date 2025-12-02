// Assets/Editor/UndoSystem/Core/UndoStack.cs
// 汎用Undoスタック実装

using System;
using System.Collections.Generic;

namespace MeshFactory.UndoSystem
{
    /// <summary>
    /// 汎用Undoスタック
    /// 任意のコンテキスト型に対応した末端スタック
    /// </summary>
    public class UndoStack<TContext> : IUndoStack<TContext>
    {
        // === フィールド ===
        private readonly List<IUndoRecord<TContext>> _undoStack = new();
        private readonly List<IUndoRecord<TContext>> _redoStack = new();
        private int _currentGroupId = 0;
        private int _activeGroupId = -1;
        private string _activeGroupName;

        // === プロパティ: IUndoNode ===
        public string Id { get; }
        public string DisplayName { get; set; }
        public IUndoNode Parent { get; set; }
        
        public bool CanUndo => _undoStack.Count > 0;
        public bool CanRedo => _redoStack.Count > 0;
        
        public UndoOperationInfo LatestOperation => 
            _undoStack.Count > 0 ? _undoStack[^1].Info : null;

        /// <summary>
        /// 次にRedoされる操作の情報
        /// Redoスタックは末尾から取り出すため、末尾の要素を返す
        /// </summary>
        public UndoOperationInfo NextRedoOperation => 
            _redoStack.Count > 0 ? _redoStack[^1].Info : null;

        // === プロパティ: IUndoStack ===
        public TContext Context { get; set; }
        public int UndoCount => _undoStack.Count;
        public int RedoCount => _redoStack.Count;
        public int MaxSize { get; set; } = 50;

        // === イベント ===
        public event Action<UndoOperationInfo> OnUndoPerformed;
        public event Action<UndoOperationInfo> OnRedoPerformed;
        public event Action<UndoOperationInfo> OnOperationRecorded;

        // === コンストラクタ ===
        public UndoStack(string id, string displayName = null, TContext context = default)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            DisplayName = displayName ?? id;
            Context = context;
        }

        // === 操作記録 ===
        
        /// <summary>
        /// 操作を記録
        /// </summary>
        public void Record(IUndoRecord<TContext> record, string description = null)
        {
            if (record == null)
                throw new ArgumentNullException(nameof(record));

            // メタ情報を設定
            var groupId = _activeGroupId >= 0 ? _activeGroupId : _currentGroupId++;
            record.Info = new UndoOperationInfo(
                description ?? "Operation",
                Id,
                groupId
            );

            _undoStack.Add(record);
            _redoStack.Clear();

            // サイズ制限
            EnforceMaxSize();

            OnOperationRecorded?.Invoke(record.Info);
        }

        /// <summary>
        /// グループを開始（複数操作を1つのUndoにまとめる）
        /// </summary>
        public int BeginGroup(string groupName = null)
        {
            _activeGroupId = _currentGroupId++;
            _activeGroupName = groupName;
            return _activeGroupId;
        }

        /// <summary>
        /// グループを終了
        /// </summary>
        public void EndGroup()
        {
            _activeGroupId = -1;
            _activeGroupName = null;
        }

        /// <summary>
        /// 指定グループIDまでの操作をまとめる
        /// </summary>
        public void CollapseToGroup(int groupId)
        {
            // 指定グループID以降の操作を同じグループIDに設定
            for (int i = _undoStack.Count - 1; i >= 0; i--)
            {
                if (_undoStack[i].Info.GroupId < groupId)
                    break;
                _undoStack[i].Info.GroupId = groupId;
            }
        }

        // === Undo/Redo実行 ===

        /// <summary>
        /// Undo実行
        /// </summary>
        public bool PerformUndo()
        {
            if (!CanUndo || Context == null)
                return false;

            // 同じグループIDの操作をまとめてUndo
            var lastGroupId = _undoStack[^1].Info.GroupId;
            var undoneRecords = new List<IUndoRecord<TContext>>();

            while (_undoStack.Count > 0 && _undoStack[^1].Info.GroupId == lastGroupId)
            {
                var record = _undoStack[^1];
                _undoStack.RemoveAt(_undoStack.Count - 1);
                
                record.Undo(Context);
                undoneRecords.Add(record);
            }

            // Redoスタックに逆順で追加（Redo時は元の順序で実行）
            for (int i = undoneRecords.Count - 1; i >= 0; i--)
            {
                _redoStack.Add(undoneRecords[i]);
            }

            if (undoneRecords.Count > 0)
            {
                OnUndoPerformed?.Invoke(undoneRecords[0].Info);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Redo実行
        /// </summary>
        public bool PerformRedo()
        {
            if (!CanRedo || Context == null)
                return false;

            // 同じグループIDの操作をまとめてRedo
            var lastGroupId = _redoStack[^1].Info.GroupId;
            var redoneRecords = new List<IUndoRecord<TContext>>();

            while (_redoStack.Count > 0 && _redoStack[^1].Info.GroupId == lastGroupId)
            {
                var record = _redoStack[^1];
                _redoStack.RemoveAt(_redoStack.Count - 1);
                
                record.Redo(Context);
                redoneRecords.Add(record);
            }

            // Undoスタックに逆順で追加
            for (int i = redoneRecords.Count - 1; i >= 0; i--)
            {
                _undoStack.Add(redoneRecords[i]);
            }

            if (redoneRecords.Count > 0)
            {
                OnRedoPerformed?.Invoke(redoneRecords[0].Info);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 履歴をクリア
        /// </summary>
        public void Clear()
        {
            _undoStack.Clear();
            _redoStack.Clear();
            _activeGroupId = -1;
        }

        // === 内部メソッド ===

        private void EnforceMaxSize()
        {
            while (_undoStack.Count > MaxSize)
            {
                _undoStack.RemoveAt(0);
            }
        }

        // === デバッグ用 ===

        /// <summary>
        /// スタックの状態を文字列で取得
        /// </summary>
        public string GetDebugInfo()
        {
            return $"[{Id}] Undo: {_undoStack.Count}, Redo: {_redoStack.Count}, " +
                   $"Group: {_activeGroupId}, Latest: {LatestOperation?.Description ?? "none"}";
        }
    }
}
