// Assets/Editor/SimpleMeshFactory.GUI.Tools.cs
// 左ペインUI描画（DrawMeshList、ツールバー）

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

        // KnifeToolの設定変更をUndo対応
        if (_currentTool == _knifeTool)
        {
            var oldMode = _knifeTool.Mode;
            var oldEdgeSelect = _knifeTool.EdgeSelect;
            var oldChainMode = _knifeTool.ChainMode;

            _currentTool.DrawSettingsUI();

            // 設定が変更されたらUndo記録
            if (_knifeTool.Mode != oldMode ||
                _knifeTool.EdgeSelect != oldEdgeSelect ||
                _knifeTool.ChainMode != oldChainMode)
            {
                _undoController.EditorState.KnifeMode = oldMode;
                _undoController.EditorState.KnifeEdgeSelect = oldEdgeSelect;
                _undoController.EditorState.KnifeChainMode = oldChainMode;
                _undoController.BeginEditorStateDrag();
                _undoController.EditorState.KnifeMode = _knifeTool.Mode;
                _undoController.EditorState.KnifeEdgeSelect = _knifeTool.EdgeSelect;
                _undoController.EditorState.KnifeChainMode = _knifeTool.ChainMode;
                _undoController.EndEditorStateDrag("Change Knife Settings");
            }
        }
        else
        {
            _currentTool?.DrawSettingsUI();
        }

        // ツール設定をシリアライズ用に同期
        SyncSettingsFromTool();

        EditorGUI.indentLevel--;
    }

}