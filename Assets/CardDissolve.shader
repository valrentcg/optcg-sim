Shader "UI/CardDissolve"
{
    // Dissolve/incineration shader for UGUI RawImage. Burns the card away following a noise mask
    // (_Cutoff rising 0->~1.2), with a glowing green burning edge. Mirrors the project's other UI
    // shaders (clip rect / stencil) so it behaves under URP + UGUI.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _NoiseTex ("Noise", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Cutoff ("Cutoff", Range(0,1.3)) = 0
        _EdgeWidth ("Edge Width", Range(0.001,0.4)) = 0.14
        _EdgeColor ("Edge Color", Color) = (0.2,1.0,0.4,1)
        _EmberColor ("Ember Color", Color) = (0.85,1.0,0.55,1)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off  Lighting Off  ZWrite Off  ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT

            struct appdata_t { float4 vertex:POSITION; float4 color:COLOR; float2 texcoord:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 texcoord:TEXCOORD0; float4 worldPosition:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            sampler2D _MainTex; sampler2D _NoiseTex;
            fixed4 _Color; fixed4 _TextureSampleAdd; float4 _ClipRect; float4 _MainTex_ST;
            float _Cutoff; float _EdgeWidth; fixed4 _EdgeColor; fixed4 _EmberColor;

            v2f vert (appdata_t v)
            {
                v2f OUT; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex; OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex); OUT.color = v.color * _Color; return OUT;
            }

            fixed4 frag (v2f IN) : SV_Target
            {
                half4 c = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;
                float n = tex2D(_NoiseTex, IN.texcoord).r;
                float burn = n - _Cutoff;
                clip(burn);                                  // already burned away
                if (burn < _EdgeWidth)
                {
                    float e = saturate(burn / _EdgeWidth);   // 0 at the burning edge, 1 inside
                    fixed3 hot = lerp(_EmberColor.rgb, _EdgeColor.rgb, e);
                    c.rgb = lerp(hot, c.rgb, e);
                    c.a = max(c.a, 1.0 - e);                 // keep the glowing edge solid
                }
                float a = c.a;
                #ifdef UNITY_UI_CLIP_RECT
                a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                clip(a - 0.001);
                return fixed4(c.rgb, a);
            }
            ENDCG
        }
    }
}
