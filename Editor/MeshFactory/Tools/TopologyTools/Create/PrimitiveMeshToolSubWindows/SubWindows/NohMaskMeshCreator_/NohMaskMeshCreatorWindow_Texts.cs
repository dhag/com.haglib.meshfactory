// Assets/Editor/MeshCreators/NohMaskMeshCreatorWindow_Texts.cs
// 能面メッシュ生成ウィンドウ - ローカライズ辞書

using System.Collections.Generic;
using MeshFactory.Localization;

public partial class NohMaskMeshCreatorWindow
{
    // ================================================================
    // ローカライズ辞書
    // ================================================================

    private static readonly Dictionary<string, Dictionary<string, string>> Texts = new()
    {
        // ウィンドウ
        ["WindowTitle"] = new() { ["en"] = "Create Noh Mask Mesh", ["ja"] = "能面メッシュ作成", ["hi"] = "のうめんをつくる" },

        // セクション
        ["Parameters"] = new() { ["en"] = "Noh Mask Parameters", ["ja"] = "能面パラメータ", ["hi"] = "のうめんのせってい" },
        ["FaceSize"] = new() { ["en"] = "Face Size", ["ja"] = "顔サイズ", ["hi"] = "かおのおおきさ" },
        ["Nose"] = new() { ["en"] = "Nose", ["ja"] = "鼻", ["hi"] = "はな" },
        ["Curve"] = new() { ["en"] = "Curve", ["ja"] = "曲率", ["hi"] = "まがりぐあい" },
        ["Segments"] = new() { ["en"] = "Segments", ["ja"] = "分割数", ["hi"] = "ぶんかつすう" },
        ["Transform"] = new() { ["en"] = "Transform", ["ja"] = "変形", ["hi"] = "へんけい" },
        ["PivotOffset"] = new() { ["en"] = "Pivot Offset", ["ja"] = "ピボットオフセット", ["hi"] = "ちゅうしんのずれ" },
        ["Preview"] = new() { ["en"] = "Preview", ["ja"] = "プレビュー", ["hi"] = "プレビュー" },

        // フィールド - 基本
        ["Name"] = new() { ["en"] = "Name", ["ja"] = "名前", ["hi"] = "なまえ" },

        // フィールド - 顔サイズ
        ["WidthTop"] = new() { ["en"] = "Width Top", ["ja"] = "上部幅", ["hi"] = "うえのはば" },
        ["WidthBottom"] = new() { ["en"] = "Width Bottom", ["ja"] = "下部幅", ["hi"] = "したのはば" },
        ["Height"] = new() { ["en"] = "Height", ["ja"] = "高さ", ["hi"] = "たかさ" },
        ["DepthTop"] = new() { ["en"] = "Depth Top", ["ja"] = "上部深さ", ["hi"] = "うえのふかさ" },
        ["DepthBottom"] = new() { ["en"] = "Depth Bottom", ["ja"] = "下部深さ", ["hi"] = "したのふかさ" },

        // フィールド - 鼻
        ["NoseHeight"] = new() { ["en"] = "Height", ["ja"] = "高さ", ["hi"] = "たかさ" },
        ["NoseWidth"] = new() { ["en"] = "Width", ["ja"] = "幅", ["hi"] = "はば" },
        ["NoseLength"] = new() { ["en"] = "Length", ["ja"] = "長さ", ["hi"] = "ながさ" },
        ["NosePosition"] = new() { ["en"] = "Position", ["ja"] = "位置", ["hi"] = "いち" },

        // フィールド - 曲率
        ["Top"] = new() { ["en"] = "Top", ["ja"] = "上部", ["hi"] = "うえ" },
        ["Bottom"] = new() { ["en"] = "Bottom", ["ja"] = "下部", ["hi"] = "した" },

        // フィールド - 分割数
        ["Horizontal"] = new() { ["en"] = "Horizontal", ["ja"] = "水平", ["hi"] = "よこ" },
        ["Vertical"] = new() { ["en"] = "Vertical", ["ja"] = "垂直", ["hi"] = "たて" },

        // フィールド - 変形
        ["FlipY"] = new() { ["en"] = "Flip Y", ["ja"] = "Y反転", ["hi"] = "Yはんてん" },
        ["FlipZ"] = new() { ["en"] = "Flip Z", ["ja"] = "Z反転", ["hi"] = "Zはんてん" },

        // ピボット
        ["PivotZ"] = new() { ["en"] = "Z", ["ja"] = "Z", ["hi"] = "Z" },
        ["Center"] = new() { ["en"] = "Center", ["ja"] = "中央", ["hi"] = "まんなか" },

        // 情報
        ["VertsFaces"] = new() { ["en"] = "Vertices: {0}, Faces: {1}", ["ja"] = "頂点: {0}, 面: {1}", ["hi"] = "てん: {0}, めん: {1}" },
    };

    // ================================================================
    // ローカライズヘルパー
    // ================================================================

    /// <summary>テキスト取得</summary>
    private static string T(string key) => L.GetFrom(Texts, key);

    /// <summary>フォーマット付きテキスト取得</summary>
    private static string T(string key, params object[] args) => L.GetFrom(Texts, key, args);
}
