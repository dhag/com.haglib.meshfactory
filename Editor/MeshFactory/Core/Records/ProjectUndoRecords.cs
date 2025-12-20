using MeshFactory.Serialization;
using MeshFactory.Tools;
using MeshFactory.UndoSystem;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace MeshEditor
{
    /// <summary>
    /// モデル全体のスナップショット（メッシュリスト + エディタ状態）
    /// </summary>
    public class ModelSnapshot
    {
        public string Name;
        public List<MeshContextSnapshot> MeshSnapshots;
        public WorkPlaneData WorkPlane;
        public EditorStateData EditorState;

        /// <summary>
        /// メッシュコンテキストリストからスナップショットを作成
        /// </summary>
        public static ModelSnapshot Capture(
            List<SimpleMeshFactory.MeshContext> meshContextList,
            string name = null,
            WorkPlane workPlane = null,
            EditorStateData editorState = null)
        {
            if (meshContextList == null) return null;

            var snapshot = new ModelSnapshot
            {
                Name = name ?? "Model",
                MeshSnapshots = new List<MeshContextSnapshot>(),
                EditorState = editorState != null ? CloneEditorState(editorState) : null
            };

            // 各メッシュのスナップショットを作成
            foreach (var meshContext in meshContextList)
            {
                var meshSnapshot = MeshContextSnapshot.Capture(meshContext);
                if (meshSnapshot != null)
                {
                    snapshot.MeshSnapshots.Add(meshSnapshot);
                }
            }

            // WorkPlaneのスナップショット
            if (workPlane != null)
            {
                snapshot.WorkPlane = ModelSerializer.ToWorkPlaneData(workPlane);
            }

            return snapshot;
        }

        /// <summary>
        /// スナップショットからメッシュコンテキストリストを復元
        /// </summary>
        public List<SimpleMeshFactory.MeshContext> ToMeshContextList()
        {
            var list = new List<SimpleMeshFactory.MeshContext>();

            if (MeshSnapshots != null)
            {
                foreach (var meshSnapshot in MeshSnapshots)
                {
                    var meshContext = meshSnapshot?.ToMeshContext();
                    if (meshContext != null)
                    {
                        list.Add(meshContext);
                    }
                }
            }

            return list;
        }

        /// <summary>
        /// WorkPlaneを復元
        /// </summary>
        public void ApplyToWorkPlane(WorkPlane workPlane)
        {
            if (WorkPlane != null && workPlane != null)
            {
                ModelSerializer.ApplyToWorkPlane(WorkPlane, workPlane);
            }
        }

        private static EditorStateData CloneEditorState(EditorStateData source)
        {
            if (source == null) return null;

            return new EditorStateData
            {
                rotationX = source.rotationX,
                rotationY = source.rotationY,
                cameraDistance = source.cameraDistance,
                cameraTarget = source.cameraTarget != null 
                    ? (float[])source.cameraTarget.Clone() 
                    : null,
                showWireframe = source.showWireframe,
                showVertices = source.showVertices,
                vertexEditMode = source.vertexEditMode,
                currentToolName = source.currentToolName,
                selectedMeshIndex = source.selectedMeshIndex
            };
        }
    }

    /// <summary>
    /// プロジェクト全体のスナップショット（複数モデル対応）
    /// </summary>
    public class ProjectSnapshot
    {
        public string Version;
        public string Name;
        public string CreatedAt;
        public string ModifiedAt;
        public List<ModelSnapshot> ModelSnapshots;

        /// <summary>
        /// プロジェクトデータからスナップショットを作成
        /// </summary>
        public static ProjectSnapshot Capture(ProjectData projectData)
        {
            if (projectData == null) return null;

            var snapshot = new ProjectSnapshot
            {
                Version = projectData.version,
                Name = projectData.name,
                CreatedAt = projectData.createdAt,
                ModifiedAt = projectData.modifiedAt,
                ModelSnapshots = new List<ModelSnapshot>()
            };

            // 各モデルのスナップショットを作成
            // 注: ProjectDataはシリアライズ用データなので、
            //     実際の使用時はModelContextから直接キャプチャする方が望ましい
            if (projectData.models != null)
            {
                foreach (var modelData in projectData.models)
                {
                    var modelSnapshot = CaptureFromModelData(modelData);
                    if (modelSnapshot != null)
                    {
                        snapshot.ModelSnapshots.Add(modelSnapshot);
                    }
                }
            }

            return snapshot;
        }

        /// <summary>
        /// 現在のエディタ状態からスナップショットを作成（単一モデル用）
        /// </summary>
        public static ProjectSnapshot CaptureFromEditor(
            List<SimpleMeshFactory.MeshContext> meshContextList,
            string projectName,
            WorkPlane workPlane = null,
            EditorStateData editorState = null)
        {
            var snapshot = new ProjectSnapshot
            {
                Version = "1.0",
                Name = projectName ?? "Project",
                CreatedAt = DateTime.Now.ToString("o"),
                ModifiedAt = DateTime.Now.ToString("o"),
                ModelSnapshots = new List<ModelSnapshot>()
            };

            var modelSnapshot = ModelSnapshot.Capture(
                meshContextList,
                projectName,
                workPlane,
                editorState);

            if (modelSnapshot != null)
            {
                snapshot.ModelSnapshots.Add(modelSnapshot);
            }

            return snapshot;
        }

        /// <summary>
        /// スナップショットからProjectDataを復元
        /// </summary>
        public ProjectData ToProjectData()
        {
            var projectData = new ProjectData
            {
                version = Version ?? "1.0",
                name = Name ?? "Project",
                createdAt = CreatedAt,
                modifiedAt = ModifiedAt,
                models = new List<ModelData>()
            };

            if (ModelSnapshots != null)
            {
                foreach (var modelSnapshot in ModelSnapshots)
                {
                    var modelData = ToModelData(modelSnapshot);
                    if (modelData != null)
                    {
                        projectData.models.Add(modelData);
                    }
                }
            }

            return projectData;
        }

        /// <summary>
        /// 最初のモデルのメッシュコンテキストリストを復元（単一モデル用）
        /// </summary>
        public List<SimpleMeshFactory.MeshContext> ToMeshContextList()
        {
            if (ModelSnapshots == null || ModelSnapshots.Count == 0)
            {
                return new List<SimpleMeshFactory.MeshContext>();
            }

            return ModelSnapshots[0].ToMeshContextList();
        }

        private static ModelSnapshot CaptureFromModelData(ModelData modelData)
        {
            if (modelData == null) return null;

            var snapshot = new ModelSnapshot
            {
                Name = modelData.name,
                MeshSnapshots = new List<MeshContextSnapshot>(),
                WorkPlane = modelData.workPlane,
                EditorState = modelData.editorState
            };

            // MeshContextDataからMeshContextSnapshotへ変換
            if (modelData.meshContextList != null)
            {
                foreach (var meshContextData in modelData.meshContextList)
                {
                    var meshContext = ModelSerializer.ToMeshContext(meshContextData);
                    if (meshContext != null)
                    {
                        var meshSnapshot = MeshContextSnapshot.Capture(meshContext);
                        if (meshSnapshot != null)
                        {
                            snapshot.MeshSnapshots.Add(meshSnapshot);
                        }
                    }
                }
            }

            return snapshot;
        }

        private static ModelData ToModelData(ModelSnapshot modelSnapshot)
        {
            if (modelSnapshot == null) return null;

            var modelData = new ModelData
            {
                name = modelSnapshot.Name,
                meshContextList = new List<MeshContextData>(),
                workPlane = modelSnapshot.WorkPlane,
                editorState = modelSnapshot.EditorState
            };

            if (modelSnapshot.MeshSnapshots != null)
            {
                foreach (var meshSnapshot in modelSnapshot.MeshSnapshots)
                {
                    var meshContext = meshSnapshot?.ToMeshContext();
                    if (meshContext != null)
                    {
                        var meshContextData = ModelSerializer.FromMeshContext(meshContext, null);
                        if (meshContextData != null)
                        {
                            modelData.meshContextList.Add(meshContextData);
                        }
                    }
                }
            }

            return modelData;
        }
    }

    /// <summary>
    /// メッシュリスト変更の記録（メッシュ追加/削除用）
    /// </summary>
    public class MeshListRecord
    {
        public enum OperationType
        {
            Add,
            Remove,
            Reorder,
            Replace
        }

        public OperationType Operation;
        public int Index;
        public MeshContextSnapshot BeforeSnapshot;  // Remove/Replace時の元データ
        public MeshContextSnapshot AfterSnapshot;   // Add/Replace時の新データ
        public int[] ReorderMap;                    // Reorder時のインデックスマップ

        /// <summary>
        /// メッシュ追加の記録を作成
        /// </summary>
        public static MeshListRecord CreateAdd(int index, SimpleMeshFactory.MeshContext meshContext)
        {
            return new MeshListRecord
            {
                Operation = OperationType.Add,
                Index = index,
                AfterSnapshot = MeshContextSnapshot.Capture(meshContext)
            };
        }

        /// <summary>
        /// メッシュ削除の記録を作成
        /// </summary>
        public static MeshListRecord CreateRemove(int index, SimpleMeshFactory.MeshContext meshContext)
        {
            return new MeshListRecord
            {
                Operation = OperationType.Remove,
                Index = index,
                BeforeSnapshot = MeshContextSnapshot.Capture(meshContext)
            };
        }

        /// <summary>
        /// メッシュ置換の記録を作成
        /// </summary>
        public static MeshListRecord CreateReplace(
            int index,
            SimpleMeshFactory.MeshContext before,
            SimpleMeshFactory.MeshContext after)
        {
            return new MeshListRecord
            {
                Operation = OperationType.Replace,
                Index = index,
                BeforeSnapshot = MeshContextSnapshot.Capture(before),
                AfterSnapshot = MeshContextSnapshot.Capture(after)
            };
        }

        /// <summary>
        /// メッシュ順序変更の記録を作成
        /// </summary>
        public static MeshListRecord CreateReorder(int[] beforeToAfterMap)
        {
            return new MeshListRecord
            {
                Operation = OperationType.Reorder,
                ReorderMap = (int[])beforeToAfterMap.Clone()
            };
        }
    }

    /// <summary>
    /// プロジェクト操作の記録（読み込み/新規作成用）
    /// </summary>
    public class ProjectRecord
    {
        public enum OperationType
        {
            New,
            Load,
            Import
        }

        public OperationType Operation;
        public ProjectSnapshot BeforeSnapshot;
        public ProjectSnapshot AfterSnapshot;
        public string FilePath;  // Load/Import時のファイルパス

        /// <summary>
        /// 新規作成の記録を作成
        /// </summary>
        public static ProjectRecord CreateNew(ProjectSnapshot before, ProjectSnapshot after)
        {
            return new ProjectRecord
            {
                Operation = OperationType.New,
                BeforeSnapshot = before,
                AfterSnapshot = after
            };
        }

        /// <summary>
        /// 読み込みの記録を作成
        /// </summary>
        public static ProjectRecord CreateLoad(ProjectSnapshot before, ProjectSnapshot after, string filePath)
        {
            return new ProjectRecord
            {
                Operation = OperationType.Load,
                BeforeSnapshot = before,
                AfterSnapshot = after,
                FilePath = filePath
            };
        }

        /// <summary>
        /// インポートの記録を作成
        /// </summary>
        public static ProjectRecord CreateImport(ProjectSnapshot before, ProjectSnapshot after, string filePath)
        {
            return new ProjectRecord
            {
                Operation = OperationType.Import,
                BeforeSnapshot = before,
                AfterSnapshot = after,
                FilePath = filePath
            };
        }
    }
}
