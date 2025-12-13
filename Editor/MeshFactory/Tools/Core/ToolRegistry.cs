// Assets/Editor/MeshFactory/Tools/Core/ToolRegistry.cs
// 全ツールの登録を一箇所で管理
// 新しいツールを追加する際はここだけ修正すればよい

using System;

namespace MeshFactory.Tools
{
    /// <summary>
    /// 全ツールの登録を一元管理
    /// 新しいツールを追加する場合はRegisterAllToolsメソッドに追加
    /// </summary>
    public static class ToolRegistry
    {
        // ================================================================
        // ツールカテゴリ定義
        // ================================================================

        /// <summary>
        /// ツールのカテゴリ
        /// </summary>
        public enum ToolCategory
        {
            Selection,      // 選択系
            Transform,      // 変形系
            Topology,       // トポロジ編集系
            Utility         // ユーティリティ
        }

        /// <summary>
        /// ツール情報（UI表示用）
        /// </summary>
        public class ToolInfo
        {
            public string Name { get; }
            public string DisplayName { get; }
            public ToolCategory Category { get; }
            public Func<IEditTool> Factory { get; }

            public ToolInfo(string name, string displayName, ToolCategory category, Func<IEditTool> factory)
            {
                Name = name;
                DisplayName = displayName;
                Category = category;
                Factory = factory;
            }
        }

        // ================================================================
        // ツール定義
        // ================================================================

        /// <summary>
        /// 全ツール定義
        /// 新しいツールを追加する場合はここに追加
        /// </summary>
        public static readonly ToolInfo[] AllToolInfos = new ToolInfo[]
        {
            // Selection
            new ToolInfo("Select", "Select", ToolCategory.Selection, () => new SelectTool()),
            new ToolInfo("Sel+", "Sel+", ToolCategory.Selection, () => new AdvancedSelectTool()),

            // Transform
            new ToolInfo("Move", "Move", ToolCategory.Transform, () => new MoveTool()),
            new ToolInfo("Sculpt", "Sculpt", ToolCategory.Transform, () => new SculptTool()),

            // Topology
            new ToolInfo("AddFace", "AddFace", ToolCategory.Topology, () => new AddFaceTool()),
            new ToolInfo("Knife", "Knife", ToolCategory.Topology, () => new KnifeTool()),
            new ToolInfo("EdgeTopo", "EdgeTopo", ToolCategory.Topology, () => new EdgeTopologyTool()),
            new ToolInfo("Merge", "Merge", ToolCategory.Topology, () => new MergeVerticesTool()),
            new ToolInfo("Extrude", "Extrude edge", ToolCategory.Topology, () => new EdgeExtrudeTool()),
            new ToolInfo("Push", "Extrude face", ToolCategory.Topology, () => new FaceExtrudeTool()),
            new ToolInfo("Bevel", "Bevel", ToolCategory.Topology, () => new EdgeBevelTool()),
            new ToolInfo("Flip", "Flip", ToolCategory.Utility, () => new FlipFaceTool()),
            new ToolInfo("Line Ext", "Line Ext", ToolCategory.Utility, () => new LineExtrudeTool()),

            // Utility
            new ToolInfo("Pivot", "Pivot", ToolCategory.Utility, () => new PivotOffsetTool()),
        };

        // ================================================================
        // 登録メソッド
        // ================================================================

        /// <summary>
        /// 全ツールをToolManagerに登録
        /// </summary>
        public static void RegisterAllTools(ToolManager manager)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            foreach (var info in AllToolInfos)
            {
                var tool = info.Factory();
                manager.Register(tool);
            }

            // デフォルトツールを設定
            manager.SetDefault("Select");
        }

        /// <summary>
        /// 指定カテゴリのツールのみ登録
        /// </summary>
        public static void RegisterCategory(ToolManager manager, ToolCategory category)
        {
            if (manager == null)
                throw new ArgumentNullException(nameof(manager));

            foreach (var info in AllToolInfos)
            {
                if (info.Category == category)
                {
                    var tool = info.Factory();
                    manager.Register(tool);
                }
            }
        }

        /// <summary>
        /// ツール名から表示名を取得
        /// </summary>
        public static string GetDisplayName(string toolName)
        {
            foreach (var info in AllToolInfos)
            {
                if (info.Name == toolName)
                    return info.DisplayName;
            }
            return toolName;
        }

        /// <summary>
        /// ツール名からカテゴリを取得
        /// </summary>
        public static ToolCategory? GetCategory(string toolName)
        {
            foreach (var info in AllToolInfos)
            {
                if (info.Name == toolName)
                    return info.Category;
            }
            return null;
        }
    }
}
