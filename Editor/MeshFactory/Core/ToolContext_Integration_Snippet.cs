// ================================================================
// ToolContext.cs への追加（スニペット）
// ================================================================
// 以下のコードを ToolContext.cs に追加してください

/*
// === 既存の using に追加 ===
using static MeshFactory.Gizmo.HandlesGizmoDrawer;
using static MeshFactory.Gizmo.GLGizmoDrawer;

// === ToolContext クラス内に追加 ===

// ギズモ描画コンテキスト
public GizmoContext Gizmo { get; set; }

// 便利プロパティ（よく使う2D描画へのショートカット）
public IGizmoDrawer2D GizmoDraw => Gizmo?.Get2D();
public IGizmoDrawer3D GizmoDraw3D => Gizmo?.Get3D();
*/

// ================================================================
// 使用例：ツール側での描画
// ================================================================
/*
public class MoveTool : IEditTool
{
    public void DrawGizmo(ToolContext ctx)
    {
        var g = ctx.GizmoDraw;
        if (g == null) return;  // フォールバック：従来のHandles描画
        
        g.Begin();
        
        // 軸線描画
        g.Color = Color.red;
        g.DrawLine(originScreen, xEndScreen, 2f);
        
        g.Color = Color.green;
        g.DrawLine(originScreen, yEndScreen, 2f);
        
        // 中央四角
        g.DrawSolidRectWithOutline(centerRect, Color.clear, Color.white);
        
        g.End();
    }
}
*/

// ================================================================
// 初期化例：SimpleMeshFactory側
// ================================================================
/*
// SimpleMeshFactory.cs の初期化部分

private IGizmoDrawer _gizmoDrawer;

void OnEnable()
{
    // エディタ用Drawer作成
    #if UNITY_EDITOR
    _gizmoDrawer = new EditorGizmoDrawer();
    #else
    _gizmoDrawer = new RuntimeGizmoDrawer();
    #endif
    
    // ToolContextに注入
    _toolContext.Gizmo = GizmoContext.Create(_gizmoDrawer);
}
*/

// ================================================================
// 移行パターン：段階的な置き換え
// ================================================================
/*
// Before（現状）
public void DrawGizmo(ToolContext ctx)
{
    UnityEditor_Handles.BeginGUI();
    UnityEditor_Handles.color = Color.red;
    UnityEditor_Handles.DrawAAPolyLine(2f, p1, p2);
    UnityEditor_Handles.EndGUI();
}

// After（移行後）
public void DrawGizmo(ToolContext ctx)
{
    var g = ctx.GizmoDraw;
    if (g == null)
    {
        // フォールバック：従来コード
        UnityEditor_Handles.BeginGUI();
        UnityEditor_Handles.color = Color.red;
        UnityEditor_Handles.DrawAAPolyLine(2f, p1, p2);
        UnityEditor_Handles.EndGUI();
        return;
    }
    
    // 新コード
    g.Begin();
    g.Color = Color.red;
    g.DrawPolyLine(2f, p1, p2);
    g.End();
}

// 完全移行後（フォールバック削除）
public void DrawGizmo(ToolContext ctx)
{
    var g = ctx.GizmoDraw;
    if (g == null) return;
    
    g.Begin();
    g.Color = Color.red;
    g.DrawPolyLine(2f, p1, p2);
    g.End();
}
*/
