// Assets/Editor/SimpleMeshFactory.GUI.Tools.cs
// 左ペインUI描画（DrawMeshList、ツールバー）
// IToolSettings対応版

using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using MeshFactory.Tools;
using MeshFactory.Selection;


public partial class SimpleMeshFactory
{

    // ================================================================
    // Tools セクション描画
    // ================================================================
    private void DrawToolsSection()
    {
        _foldTools = EditorGUILayout.Foldout(_foldTools, "Tools", true);
        if (!_foldTools)
            return;

        EditorGUI.indentLevel++;

        // ツールボタン（排他選択）
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawToolButton(_selectTool, "Select");
            DrawToolButton(_moveTool, "Move");
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawToolButton(_addFaceTool, "AddFace");
            DrawToolButton(_knifeTool, "Knife");
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawToolButton(_edgeTopoTool, "EdgeTopo");
            DrawToolButton(_advancedSelectTool, "Sel+");
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawToolButton(_sculptTool, "Sculpt");
            DrawToolButton(_mergeTool, "Merge");
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawToolButton(_extrudeTool, "Extrude edge");
            DrawToolButton(_faceExtrudeTool, "Extrude face");
        }
        using (new EditorGUILayout.HorizontalScope())
        {
            DrawToolButton(_edgeBevelTool, "Bevel");
            DrawToolButton(_pivotOffsetTool, "Pivot");
        }

        // 現在のツールの設定UI
        EditorGUILayout.Space(3);

        // ツール設定UIの描画とUndo処理
        DrawCurrentToolSettingsWithUndo();

        // ツール設定をシリアライズ用に同期
        SyncSettingsFromTool();

        EditorGUI.indentLevel--;
    }

    // ================================================================
    // ツール設定UI描画 + Undo処理
    // ================================================================

    /// <summary>
    /// 現在のツールの設定UIを描画し、変更があればUndo記録
    /// </summary>
    private void DrawCurrentToolSettingsWithUndo()
    {
        if (_currentTool == null)
            return;

        // --------------------------------------------------------
        // KnifeTool: 既存の処理を維持（後方互換）
        // 将来的にKnifeSettingsに移行後、この分岐は削除可能
        // --------------------------------------------------------
        if (_currentTool == _knifeTool)
        {
            DrawKnifeToolSettingsWithUndo();
            return;
        }

        // --------------------------------------------------------
        // IToolSettings対応ツール: 汎用Undo処理
        // --------------------------------------------------------
        var settings = _currentTool.Settings;
        if (settings != null)
        {
            // Before: スナップショット取得
            IToolSettings before = settings.Clone();

            // UI描画（ツール側で自由に実装）
            _currentTool.DrawSettingsUI();

            // After: 変更検出 & Undo記録
            if (settings.IsDifferentFrom(before))
            {
                RecordToolSettingsChange(before, settings.Clone());
            }
        }
        else
        {
            // 設定を持たないツール（SelectToolなど）
            _currentTool.DrawSettingsUI();
        }
    }

    /// <summary>
    /// ツール設定変更をUndo記録
    /// </summary>
    private void RecordToolSettingsChange(IToolSettings before, IToolSettings after)
    {
        if (_undoController == null || before == null || after == null)
            return;

        var editorState = _undoController.EditorState;
        if (editorState.ToolSettings == null)
            editorState.ToolSettings = new ToolSettingsStorage();

        // Before: 変更前の設定をEditorStateContextに設定
        editorState.ToolSettings.Set(before);

        // BeginDrag: スナップショット取得
        _undoController.BeginEditorStateDrag();

        // After: 変更後の設定をEditorStateContextに設定
        editorState.ToolSettings.Set(after);

        // EndDrag: 差異検出 & Undo記録
        _undoController.EndEditorStateDrag($"Change {after.ToolName} Settings");

        Repaint();
    }

    // ================================================================
    // KnifeTool専用処理（既存コード維持）
    // ================================================================

    /// <summary>
    /// KnifeToolの設定UIを描画（既存処理維持）
    /// </summary>
    private void DrawKnifeToolSettingsWithUndo()
    {
        // 変更前の状態を保存
        var oldMode = _knifeTool.knifeProperty.Mode;
        var oldEdgeSelect = _knifeTool.knifeProperty.EdgeSelect;
        var oldChainMode = _knifeTool.knifeProperty.ChainMode;

        // UI描画
        _currentTool.DrawSettingsUI();

        // 設定が変更されたらUndo記録
        if (_knifeTool.knifeProperty.Mode != oldMode ||
            _knifeTool.knifeProperty.EdgeSelect != oldEdgeSelect ||
            _knifeTool.knifeProperty.ChainMode != oldChainMode)
        {
            // 変更前の状態を復元（BeginDrag前に必要）
            _undoController.EditorState.knifeProperty.Mode = oldMode;
            _undoController.EditorState.knifeProperty.EdgeSelect = oldEdgeSelect;
            _undoController.EditorState.knifeProperty.ChainMode = oldChainMode;

            _undoController.BeginEditorStateDrag();

            // 変更後の状態を設定
            _undoController.EditorState.knifeProperty.Mode = _knifeTool.knifeProperty.Mode;
            _undoController.EditorState.knifeProperty.EdgeSelect = _knifeTool.knifeProperty.EdgeSelect;
            _undoController.EditorState.knifeProperty.ChainMode = _knifeTool.knifeProperty.ChainMode;

            _undoController.EndEditorStateDrag("Change Knife Settings");
        }
    }
}
