// Assets/Editor/UndoSystem/MeshEditor/MeshFactoryUndoController.cs
// SimpleMeshEditorに組み込むためのUndoコントローラー
// MeshData（Vertex/Face）ベースに対応

using System;
using System.Collections.Generic;
using UnityEngine;
using MeshEditor.Data;

namespace MeshEditor.UndoSystem
{
    /// <summary>
    /// メッシュエディタ用Undoコントローラー
    /// SimpleMeshEditorに組み込んで使用
    /// MeshDataベースの新構造対応
    /// </summary>
    public class MeshFactoryUndoController : IDisposable
    {
        // === Undoノード構造 ===
        private UndoGroup _mainGroup;
        private UndoStack<MeshEditContext> _vertexEditStack;
        private UndoStack<EditorStateContext> _editorStateStack;
        private UndoGroup _subWindowGroup;

        // === コンテキスト ===
        private MeshEditContext _meshContext;
        private EditorStateContext _editorStateContext;

        // === 状態追跡（変更検出用） ===
        private Vector3[] _lastVertexPositions;
        private int _dragStartGroupId = -1;
        private bool _isDragging;

        // エディタ状態ドラッグ用
        private bool _isEditorStateDragging = false;
        private EditorStateSnapshot _editorStateStartSnapshot;

        // === プロパティ ===
        public MeshEditContext MeshContext => _meshContext;
        public EditorStateContext EditorState => _editorStateContext;

        /// <summary>MeshDataへの直接アクセス</summary>
        public MeshData MeshData => _meshContext?.MeshData;

        // 後方互換
        public ViewContext ViewContext => _editorStateContext as ViewContext ?? new ViewContext();

        public bool CanUndo => _mainGroup.CanUndo;
        public bool CanRedo => _mainGroup.CanRedo;

        public UndoGroup MainGroup => _mainGroup;
        public UndoStack<MeshEditContext> VertexEditStack => _vertexEditStack;
        public UndoStack<EditorStateContext> EditorStateStack => _editorStateStack;
        public UndoGroup SubWindowGroup => _subWindowGroup;

        // === イベント ===
        public event Action OnUndoRedoPerformed;

        // === コンストラクタ ===
        public MeshFactoryUndoController(string windowId = "MainEditor")
        {
            // メイングループ作成
            _mainGroup = new UndoGroup(windowId, "Mesh Editor");
            _mainGroup.ResolutionPolicy = UndoResolutionPolicy.FocusThenTimestamp;

            // 頂点編集スタック
            _meshContext = new MeshEditContext();
            _vertexEditStack = new UndoStack<MeshEditContext>(
                $"{windowId}/VertexEdit",
                "Vertex Edit",
                _meshContext
            );
            _mainGroup.AddChild(_vertexEditStack);

            // エディタ状態スタック（カメラ、表示、モード統合）
            _editorStateContext = new EditorStateContext();
            _editorStateStack = new UndoStack<EditorStateContext>(
                $"{windowId}/EditorState",
                "Editor State",
                _editorStateContext
            );
            _mainGroup.AddChild(_editorStateStack);

            // サブウインドウグループ
            _subWindowGroup = new UndoGroup($"{windowId}/SubWindows", "Sub Windows");
            _mainGroup.AddChild(_subWindowGroup);

            // イベント購読
            _vertexEditStack.OnUndoPerformed += _ => OnUndoRedoPerformed?.Invoke();
            _vertexEditStack.OnRedoPerformed += _ => OnUndoRedoPerformed?.Invoke();
            _editorStateStack.OnUndoPerformed += _ => OnUndoRedoPerformed?.Invoke();
            _editorStateStack.OnRedoPerformed += _ => OnUndoRedoPerformed?.Invoke();

            // グローバルマネージャーに登録
            UndoManager.Instance.AddChild(_mainGroup);
        }

        // === 初期化・クリーンアップ ===

        /// <summary>
        /// メッシュをコンテキストに読み込む
        /// </summary>
        public void LoadMesh(Mesh mesh, Vector3[] originalVertices = null)
        {
            _meshContext.LoadFromMesh(mesh, true);

            // 元の頂点位置を設定
            if (originalVertices != null)
            {
                _meshContext.OriginalPositions = originalVertices;
            }
            // LoadFromMesh内で既にOriginalPositionsは設定される

            _vertexEditStack.Clear();
        }

        /// <summary>
        /// MeshDataを直接設定
        /// </summary>
        public void SetMeshData(MeshData meshData, Mesh targetMesh = null)
        {
            _meshContext.MeshData = meshData;
            _meshContext.TargetMesh = targetMesh;
            _meshContext.OriginalPositions = meshData.Vertices
                .ConvertAll(v => v.Position).ToArray();
            _meshContext.SelectedVertices.Clear();
            _meshContext.SelectedFaces.Clear();
            _vertexEditStack.Clear();
        }

        /// <summary>
        /// エディタ状態を設定
        /// </summary>
        public void SetEditorState(
            float rotX, float rotY, float camDist, Vector3 camTarget,
            bool wireframe, bool showVerts, bool vertexEditMode = false)
        {
            _editorStateContext.RotationX = rotX;
            _editorStateContext.RotationY = rotY;
            _editorStateContext.CameraDistance = camDist;
            _editorStateContext.CameraTarget = camTarget;
            _editorStateContext.ShowWireframe = wireframe;
            _editorStateContext.ShowVertices = showVerts;
            _editorStateContext.VertexEditMode = vertexEditMode;
        }

        /// <summary>
        /// 表示設定をコンテキストに設定（後方互換）
        /// </summary>
        public void SetViewContext(
            float rotX, float rotY, float camDist, Vector3 camTarget,
            bool wireframe, bool showVerts)
        {
            SetEditorState(rotX, rotY, camDist, camTarget, wireframe, showVerts, _editorStateContext.VertexEditMode);
        }

        public void Dispose()
        {
            UndoManager.Instance.RemoveChild(_mainGroup);
        }

        // === フォーカス管理 ===

        /// <summary>
        /// 頂点編集モードにフォーカス
        /// </summary>
        public void FocusVertexEdit()
        {
            _mainGroup.FocusedChildId = _vertexEditStack.Id;
            UndoManager.Instance.FocusedChildId = _mainGroup.Id;
        }

        /// <summary>
        /// エディタ状態（カメラ/表示/モード）にフォーカス
        /// </summary>
        public void FocusEditorState()
        {
            _mainGroup.FocusedChildId = _editorStateStack.Id;
            UndoManager.Instance.FocusedChildId = _mainGroup.Id;
        }

        /// <summary>
        /// 表示モードにフォーカス（後方互換）
        /// </summary>
        public void FocusView()
        {
            FocusEditorState();
        }

        // === エディタ状態の記録（シンプル化） ===

        /// <summary>
        /// エディタ状態ドラッグ開始
        /// </summary>
        public void BeginEditorStateDrag()
        {
            if (_isEditorStateDragging) return;
            _isEditorStateDragging = true;
            _editorStateStartSnapshot = _editorStateContext.Capture();
        }

        /// <summary>
        /// エディタ状態ドラッグ終了（変更があれば記録）
        /// </summary>
        public void EndEditorStateDrag(string description = "Change Editor State")
        {
            if (!_isEditorStateDragging) return;
            _isEditorStateDragging = false;

            var currentSnapshot = _editorStateContext.Capture();
            if (currentSnapshot.IsDifferentFrom(_editorStateStartSnapshot))
            {
                var record = new EditorStateChangeRecord(_editorStateStartSnapshot, currentSnapshot);
                _editorStateStack.Record(record, description);
            }
        }

        /// <summary>
        /// エディタ状態を即座に記録（チェックボックス等）
        /// </summary>
        public void RecordEditorStateChange(string description = "Change Editor State")
        {
            if (!_isEditorStateDragging)
            {
                // ドラッグ中でなければ、前回の状態から記録
                BeginEditorStateDrag();
            }
            EndEditorStateDrag(description);
        }

        // === 頂点編集操作の記録 ===

        /// <summary>
        /// 頂点ドラッグ開始
        /// </summary>
        public void BeginVertexDrag(Vector3[] currentPositions)
        {
            if (_isDragging) return;

            _isDragging = true;
            _lastVertexPositions = (Vector3[])currentPositions.Clone();
            _dragStartGroupId = _vertexEditStack.BeginGroup("Move Vertices");
            FocusVertexEdit();
        }

        /// <summary>
        /// 頂点ドラッグ開始（MeshDataから自動取得）
        /// </summary>
        public void BeginVertexDrag()
        {
            if (_isDragging) return;
            if (_meshContext?.MeshData == null) return;

            BeginVertexDrag(_meshContext.GetAllPositions());
        }

        /// <summary>
        /// 頂点ドラッグ終了
        /// </summary>
        public void EndVertexDrag(int[] movedIndices, Vector3[] newPositions)
        {
            if (!_isDragging) return;

            _isDragging = false;

            if (movedIndices != null && movedIndices.Length > 0)
            {
                // 移動した頂点の記録を作成
                var oldPositions = new Vector3[movedIndices.Length];
                var newPos = new Vector3[movedIndices.Length];

                for (int i = 0; i < movedIndices.Length; i++)
                {
                    int idx = movedIndices[i];
                    oldPositions[i] = _lastVertexPositions[idx];
                    newPos[i] = newPositions[idx];
                }

                var record = new VertexMoveRecord(movedIndices, oldPositions, newPos);
                _vertexEditStack.Record(record, "Move Vertices");
            }

            _vertexEditStack.EndGroup();
            _lastVertexPositions = null;
        }

        /// <summary>
        /// 頂点ドラッグ終了（MeshDataから自動取得）
        /// </summary>
        public void EndVertexDrag(int[] movedIndices)
        {
            if (_meshContext?.MeshData == null) return;
            EndVertexDrag(movedIndices, _meshContext.GetAllPositions());
        }

        /// <summary>
        /// 頂点グループ移動を記録（スライダー編集用）
        /// </summary>
        public void RecordVertexGroupMove(
            List<int>[] groups,
            Vector3[] oldOffsets,
            Vector3[] newOffsets,
            Vector3[] originalVertices)
        {
            var record = new VertexGroupMoveRecord(
                groups,
                (Vector3[])oldOffsets.Clone(),
                (Vector3[])newOffsets.Clone(),
                (Vector3[])originalVertices.Clone()
            );
            _vertexEditStack.Record(record, "Move Vertex Group");
            FocusVertexEdit();
        }

        /// <summary>
        /// 選択状態変更を記録
        /// </summary>
        public void RecordSelectionChange(
            HashSet<int> oldVertices,
            HashSet<int> newVertices,
            HashSet<int> oldFaces = null,
            HashSet<int> newFaces = null)
        {
            var record = new SelectionChangeRecord(oldVertices, newVertices, oldFaces, newFaces);
            _vertexEditStack.Record(record, "Change Selection");
        }

        // === スナップショット（トポロジー変更用） ===

        /// <summary>
        /// トポロジー変更前にスナップショットを取得（新形式）
        /// </summary>
        public MeshDataSnapshot CaptureMeshDataSnapshot()
        {
            return MeshDataSnapshot.Capture(_meshContext);
        }

        /// <summary>
        /// トポロジー変更を記録（新形式）
        /// </summary>
        public void RecordTopologyChange(MeshDataSnapshot before, MeshDataSnapshot after, string description = "Topology Change")
        {
            var record = new MeshSnapshotRecord(before, after);
            _vertexEditStack.Record(record, description);
            FocusVertexEdit();
        }

        /// <summary>
        /// トポロジー変更前にスナップショットを取得（後方互換）
        /// </summary>
        public MeshSnapshot CaptureSnapshot()
        {
            return MeshSnapshot.Capture(_meshContext);
        }

        /// <summary>
        /// トポロジー変更を記録（後方互換）
        /// </summary>
        public void RecordTopologyChange(MeshSnapshot before, MeshSnapshot after, string description = "Topology Change")
        {
            // 旧形式のスナップショットを新形式に変換して記録
            var beforeData = new MeshDataSnapshot();
            before.ApplyTo(_meshContext);
            beforeData = MeshDataSnapshot.Capture(_meshContext);

            after.ApplyTo(_meshContext);
            var afterData = MeshDataSnapshot.Capture(_meshContext);

            var record = new MeshSnapshotRecord(beforeData, afterData);
            _vertexEditStack.Record(record, description);
            FocusVertexEdit();
        }

        // === 面操作の記録（新機能） ===

        /// <summary>
        /// 面追加を記録
        /// </summary>
        public void RecordFaceAdd(Face face, int index)
        {
            var record = new FaceAddRecord(face, index);
            _vertexEditStack.Record(record, "Add Face");
            FocusVertexEdit();
        }

        /// <summary>
        /// 面削除を記録
        /// </summary>
        public void RecordFaceDelete(Face face, int index)
        {
            var record = new FaceDeleteRecord(face, index);
            _vertexEditStack.Record(record, "Delete Face");
            FocusVertexEdit();
        }

        /// <summary>
        /// 頂点追加を記録
        /// </summary>
        public void RecordVertexAdd(Vertex vertex, int index)
        {
            var record = new VertexAddRecord(vertex, index);
            _vertexEditStack.Record(record, "Add Vertex");
            FocusVertexEdit();
        }

        // === 表示操作の記録 ===

        /// <summary>
        /// カメラ変更を記録（後方互換）
        /// </summary>
        public void RecordViewChange(
            float oldRotX, float oldRotY, float oldDist, Vector3 oldTarget,
            float newRotX, float newRotY, float newDist, Vector3 newTarget)
        {
            var before = new EditorStateSnapshot
            {
                RotationX = oldRotX,
                RotationY = oldRotY,
                CameraDistance = oldDist,
                CameraTarget = oldTarget,
                ShowWireframe = _editorStateContext.ShowWireframe,
                ShowVertices = _editorStateContext.ShowVertices,
                VertexEditMode = _editorStateContext.VertexEditMode
            };
            var after = new EditorStateSnapshot
            {
                RotationX = newRotX,
                RotationY = newRotY,
                CameraDistance = newDist,
                CameraTarget = newTarget,
                ShowWireframe = _editorStateContext.ShowWireframe,
                ShowVertices = _editorStateContext.ShowVertices,
                VertexEditMode = _editorStateContext.VertexEditMode
            };
            var record = new EditorStateChangeRecord(before, after);
            _editorStateStack.Record(record, "Change View");
        }

        // === サブウインドウ管理 ===

        /// <summary>
        /// サブウインドウ用のスタックを作成
        /// </summary>
        public UndoStack<TContext> CreateSubWindowStack<TContext>(
            string id,
            string displayName,
            TContext context)
        {
            var stack = new UndoStack<TContext>(id, displayName, context);
            _subWindowGroup.AddChild(stack);
            return stack;
        }

        /// <summary>
        /// サブウインドウ用のスタックを削除
        /// </summary>
        public bool RemoveSubWindowStack(string id)
        {
            var node = _subWindowGroup.FindById(id);
            return node != null && _subWindowGroup.RemoveChild(node);
        }

        /// <summary>
        /// サブウインドウにフォーカス
        /// </summary>
        public void FocusSubWindow(string id)
        {
            _mainGroup.FocusedChildId = _subWindowGroup.Id;
            _subWindowGroup.FocusedChildId = id;
            UndoManager.Instance.FocusedChildId = _mainGroup.Id;
        }

        // === Undo/Redo実行 ===

        /// <summary>
        /// Undo実行
        /// </summary>
        public bool Undo()
        {
            return _mainGroup.PerformUndo();
        }

        /// <summary>
        /// Redo実行
        /// </summary>
        public bool Redo()
        {
            return _mainGroup.PerformRedo();
        }

        /// <summary>
        /// キーボードショートカット処理
        /// </summary>
        public bool HandleKeyboardShortcuts(Event e)
        {
            if (e.type != EventType.KeyDown)
                return false;

            bool ctrl = e.control || e.command;

            if (ctrl && e.keyCode == KeyCode.Z && !e.shift)
            {
                if (Undo())
                {
                    e.Use();
                    return true;
                }
            }

            if ((ctrl && e.keyCode == KeyCode.Y) ||
                (ctrl && e.shift && e.keyCode == KeyCode.Z))
            {
                if (Redo())
                {
                    e.Use();
                    return true;
                }
            }

            return false;
        }

        // === デバッグ ===

        public string GetDebugInfo()
        {
            var meshInfo = _meshContext?.MeshData?.GetDebugInfo() ?? "No MeshData";
            return $"[MeshFactoryUndo]\n" +
                   $"  MeshData: {meshInfo}\n" +
                   $"  Vertex: {_vertexEditStack.GetDebugInfo()}\n" +
                   $"  EditorState: {_editorStateStack.GetDebugInfo()}\n" +
                   $"  SubWindows: {_subWindowGroup.Children.Count}";
        }
    }
}
