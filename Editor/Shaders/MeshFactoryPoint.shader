// Assets/Shaders/MeshFactory/MeshFactoryPoint.shader
// メッシュエディタ用 頂点描画シェーダー（URP対応）
// DrawProceduralでインスタンシング描画

Shader "MeshFactory/Point"
{
    Properties
    {
        _PointSize ("Point Size", Float) = 8.0
        _NormalColor ("Normal Color", Color) = (1, 1, 1, 1)
        _NormalBorderColor ("Normal Border Color", Color) = (0.5, 0.5, 0.5, 1)
        _SelectedColor ("Selected Color", Color) = (1, 0.8, 0, 1)
        _SelectedBorderColor ("Selected Border Color", Color) = (1, 0, 0, 1)
        _BorderWidth ("Border Width", Float) = 1.0
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Overlay" 
            "RenderPipeline" = "UniversalPipeline" 
        }
        
        Pass
        {
            Name "Point"
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest Always
            Cull Off
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 4.5
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            // ============================================================
            // バッファ
            // ============================================================
            
            StructuredBuffer<float4> _ScreenPositionBuffer;  // xy=スクリーン座標, z=深度, w=valid
            StructuredBuffer<float> _VertexVisibilityBuffer;
            StructuredBuffer<uint> _SelectionBuffer;         // 選択状態 (0 or 1)
            
            // ============================================================
            // プロパティ（URPと衝突しない名前を使用）
            // ============================================================
            
            float _PointSize;
            float4 _NormalColor;
            float4 _NormalBorderColor;
            float4 _SelectedColor;
            float4 _SelectedBorderColor;
            float _BorderWidth;
            float2 _MeshFactoryScreenSize;
            
            // ============================================================
            // 構造体
            // ============================================================
            
            struct Attributes
            {
                uint vertexID : SV_VertexID;
                uint instanceID : SV_InstanceID;
            };
            
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 fillColor : COLOR0;
                float4 borderColor : COLOR1;
                float visibility : TEXCOORD1;
            };
            
            // ============================================================
            // 頂点シェーダー
            // ============================================================
            
            Varyings vert(Attributes input)
            {
                Varyings o;
                
                uint pointIndex = input.instanceID;
                uint quadVertex = input.vertexID;  // 0-5 (2 triangles)
                
                // スクリーン座標と可視性を取得
                float4 screenPos = _ScreenPositionBuffer[pointIndex];
                float visibility = _VertexVisibilityBuffer[pointIndex];
                
                // 無効な頂点は画面外へ
                if (screenPos.w < 0.5 || visibility < 0.5)
                {
                    o.positionCS = float4(-10, -10, 0, 1);
                    o.uv = float2(0, 0);
                    o.fillColor = float4(0, 0, 0, 0);
                    o.borderColor = float4(0, 0, 0, 0);
                    o.visibility = 0;
                    return o;
                }
                
                // 6頂点でQuad（2三角形）を構成
                // Triangle 1: 0,1,2  Triangle 2: 3,4,5
                float2 offsets[6] = {
                    float2(-1, -1),  // 0: 左下
                    float2( 1, -1),  // 1: 右下
                    float2(-1,  1),  // 2: 左上
                    float2(-1,  1),  // 3: 左上
                    float2( 1, -1),  // 4: 右下
                    float2( 1,  1)   // 5: 右上
                };
                
                float2 uvOffsets[6] = {
                    float2(0, 0),
                    float2(1, 0),
                    float2(0, 1),
                    float2(0, 1),
                    float2(1, 0),
                    float2(1, 1)
                };
                
                float halfSize = _PointSize * 0.5;
                float2 pixelPos = screenPos.xy + offsets[quadVertex] * halfSize;
                
                // スクリーン座標 → クリップ座標
                o.positionCS.x = (pixelPos.x / _MeshFactoryScreenSize.x) * 2.0 - 1.0;
                o.positionCS.y = 1.0 - (pixelPos.y / _MeshFactoryScreenSize.y) * 2.0;
                o.positionCS.z = 0.5;
                o.positionCS.w = 1.0;
                
                // UV（0-1）
                o.uv = uvOffsets[quadVertex];
                
                // 選択状態で色分け
                uint isSelected = _SelectionBuffer[pointIndex];
                o.fillColor = isSelected ? _SelectedColor : _NormalColor;
                o.borderColor = isSelected ? _SelectedBorderColor : _NormalBorderColor;
                
                o.visibility = visibility;
                
                return o;
            }
            
            // ============================================================
            // フラグメントシェーダー
            // ============================================================
            
            float4 frag(Varyings i) : SV_Target
            {
                // 非表示ならdiscard
                if (i.visibility < 0.5)
                    discard;
                
                // 矩形の内側判定
                float2 center = i.uv - 0.5;
                float2 absCenter = abs(center);
                
                // 枠線の太さ（UV空間）
                float borderThickness = _BorderWidth / _PointSize;
                
                // 枠線領域かどうか
                bool isBorder = absCenter.x > (0.5 - borderThickness) || 
                               absCenter.y > (0.5 - borderThickness);
                
                float4 col = isBorder ? i.borderColor : i.fillColor;
                
                return col;
            }
            ENDHLSL
        }
    }
    
    FallBack Off
}
