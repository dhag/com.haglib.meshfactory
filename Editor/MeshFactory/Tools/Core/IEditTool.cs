// Tools/IEditTool.cs
// 編集ツールの共通インターフェース
// IToolSettings対応版

using UnityEngine;

namespace MeshFactory.Tools
{
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
    /// 編集ツールの共通インターフェース
    /// </summary>
    public interface IEditTool
    {
        /// <summary>ツール名（内部識別子）</summary>
        string Name { get; }

        /// <summary>表示名</summary>
        string DisplayName { get; }

        /// <summary>カテゴリ</summary>
        //ToolCategory Category { get; }

        /// <summary>
        /// ツール設定（Undo対応）
        /// 設定を持たないツールはnullを返す
        /// </summary>
        IToolSettings Settings { get; }

        /// <summary>
        /// マウスダウン処理
        /// </summary>
        /// <returns>イベントを消費したか</returns>
        bool OnMouseDown(ToolContext ctx, Vector2 mousePos);

        /// <summary>
        /// マウスドラッグ処理
        /// </summary>
        /// <returns>イベントを消費したか</returns>
        bool OnMouseDrag(ToolContext ctx, Vector2 mousePos, Vector2 delta);

        /// <summary>
        /// マウスアップ処理
        /// </summary>
        /// <returns>イベントを消費したか</returns>
        bool OnMouseUp(ToolContext ctx, Vector2 mousePos);

        /// <summary>
        /// ギズモ描画
        /// </summary>
        void DrawGizmo(ToolContext ctx);

        /// <summary>
        /// ツール固有のUI描画
        /// </summary>
        void DrawSettingsUI();

        /// <summary>
        /// ツールがアクティブになった時
        /// </summary>
        void OnActivate(ToolContext ctx);

        /// <summary>
        /// ツールが非アクティブになった時
        /// </summary>
        void OnDeactivate(ToolContext ctx);

        /// <summary>
        /// 状態リセット
        /// </summary>
        void Reset();
    }
}
