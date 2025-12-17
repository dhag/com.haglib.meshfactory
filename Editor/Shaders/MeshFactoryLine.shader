// Assets/Shaders/MeshFactory/MeshFactoryLine.shader
// メッシュエディタ用 線分描画シェーダー（URP対応）
// DrawProceduralでインスタンシング描画

Shader "MeshFactory/Line"
{
    Properties
    {
        _LineWidth ("Line Width", Float) = 2.0
        _EdgeColor ("Edge Color", Color) = (0, 1, 0.5, 0.9)
        _AuxLineColor ("Aux Line Color", Color) = (1, 0.3, 1, 0.9)
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
            Name "Line"
            
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
            // 構造体
            // ============================================================
            
            struct LineSegment
            {
                int v1;
                int v2;
                int faceIndex;
                int lineType;  // 0=通常エッジ, 1=補助線
            };
            
            // ============================================================
            // バッファ
            // ============================================================
            
            StructuredBuffer<float4> _ScreenPositionBuffer;  // xy=スクリーン座標, z=深度, w=valid
            StructuredBuffer<LineSegment> _LineBuffer;
            StructuredBuffer<float> _LineVisibilityBuffer;
            
            // ============================================================
            // プロパティ（URPと衝突しない名前を使用）
            // ============================================================
            
            float _LineWidth;
            float4 _EdgeColor;
            float4 _AuxLineColor;
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
                float4 color : COLOR;
                float visibility : TEXCOORD0;
                float2 lineUV : TEXCOORD1;  // x=along line (0-1), y=across line (-1 to 1)
            };
            
            // ============================================================
            // 頂点シェーダー
            // ============================================================
            
            Varyings vert(Attributes input)
            {
                Varyings o;
                
                uint lineIndex = input.instanceID;
                uint quadVertex = input.vertexID;  // 0-5 (2 triangles)
                
                LineSegment seg = _LineBuffer[lineIndex];
                float visibility = _LineVisibilityBuffer[lineIndex];
                
                float4 p1 = _ScreenPositionBuffer[seg.v1];
                float4 p2 = _ScreenPositionBuffer[seg.v2];
                
                // 無効な線分は画面外へ
                if (visibility < 0.5 || p1.w < 0.5 || p2.w < 0.5)
                {
                    o.positionCS = float4(-10, -10, 0, 1);
                    o.color = float4(0, 0, 0, 0);
                    o.visibility = 0;
                    o.lineUV = float2(0, 0);
                    return o;
                }
                
                // 線分の方向と法線
                float2 delta = p2.xy - p1.xy;
                float len = length(delta);
                
                // 長さ0の線分は描画しない
                if (len < 0.001)
                {
                    o.positionCS = float4(-10, -10, 0, 1);
                    o.color = float4(0, 0, 0, 0);
                    o.visibility = 0;
                    o.lineUV = float2(0, 0);
                    return o;
                }
                
                float2 dir = delta / len;
                float2 normal = float2(-dir.y, dir.x);
                float halfWidth = _LineWidth * 0.5;
                
                // 四隅の座標
                float2 corner0 = p1.xy + normal * halfWidth;  // 左上
                float2 corner1 = p1.xy - normal * halfWidth;  // 左下
                float2 corner2 = p2.xy + normal * halfWidth;  // 右上
                float2 corner3 = p2.xy - normal * halfWidth;  // 右下
                
                // 6頂点でQuad（2三角形）を構成
                float2 positions[6] = {
                    corner0, corner1, corner2,  // Triangle 1
                    corner2, corner1, corner3   // Triangle 2
                };
                
                // UV: x=along line, y=across line
                float2 uvs[6] = {
                    float2(0, 1),
                    float2(0, -1),
                    float2(1, 1),
                    float2(1, 1),
                    float2(0, -1),
                    float2(1, -1)
                };
                
                float2 pixelPos = positions[quadVertex];
                
                // スクリーン座標 → クリップ座標
                o.positionCS.x = (pixelPos.x / _MeshFactoryScreenSize.x) * 2.0 - 1.0;
                o.positionCS.y = 1.0 - (pixelPos.y / _MeshFactoryScreenSize.y) * 2.0;
                o.positionCS.z = 0.5;
                o.positionCS.w = 1.0;
                
                o.visibility = visibility;
                o.lineUV = uvs[quadVertex];
                
                // 線種で色分け
                o.color = (seg.lineType == 1) ? _AuxLineColor : _EdgeColor;
                
                return o;
            }
            
            // ============================================================
            // フラグメントシェーダー
            // ============================================================
            
            float4 frag(Varyings i) : SV_Target
            {
                if (i.visibility < 0.5)
                    discard;
                
                // エッジのアンチエイリアス
                float edgeDist = abs(i.lineUV.y);  // 0-1 (中心からの距離)
                float alpha = 1.0 - smoothstep(0.7, 1.0, edgeDist);
                
                float4 col = i.color;
                col.a *= alpha;
                
                return col;
            }
            ENDHLSL
        }
    }
    
    FallBack Off
}
