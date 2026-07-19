Shader "UI/RoundedCard"
{
    // UGUI image shader that clips the sprite to a rounded rectangle with a smooth,
    // anti-aliased edge (fwidth-based), so masked cards round identically to cards whose
    // PNG already has rounded corners. Replaces the binary stencil Mask (hard edge).
    //
    // A small driver (RoundedCardClip) feeds _Size (the image rect in px) and _Radius (px)
    // each frame, so the corner radius stays a fixed fraction of every card at any size.
    // RectMask2D-safe via UNITY_UI_CLIP_RECT.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _Size ("Rect size (px)", Vector) = (100,140,0,0)
        _Radius ("Corner radius (px)", Float) = 5
        _Saturation ("Saturation", Range(0,1)) = 1

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent"
            "PreviewType"="Plane" "CanUseSpriteAtlas"="True"
        }

        Stencil
        {
            Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp]
            ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask]
        }
        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 2.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t
            {
                float4 vertex   : POSITION;
                float4 color    : COLOR;
                float2 texcoord : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct v2f
            {
                float4 vertex        : SV_POSITION;
                fixed4 color         : COLOR;
                float2 texcoord      : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _MainTex;
            fixed4 _Color;
            fixed4 _TextureSampleAdd;
            float4 _ClipRect;
            float4 _MainTex_ST;
            float4 _Size;
            float  _Radius;
            float  _Saturation;

            v2f vert (appdata_t v)
            {
                v2f OUT;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex;
                OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex);
                OUT.color = v.color * _Color;
                return OUT;
            }

            // Smooth rounded-rectangle mask in pixel space.
            float roundedAlpha (float2 uv)
            {
                float2 p = (uv - 0.5) * _Size.xy;       // px from center
                float2 b = _Size.xy * 0.5;
                float  r = min(_Radius, min(b.x, b.y));
                float2 q = abs(p) - b + r;
                float  d = min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r; // <0 inside
                float  aa = max(fwidth(d), 1e-4);
                return saturate(0.5 - d / aa);          // ~1px anti-aliased edge
            }

            fixed4 frag (v2f IN) : SV_Target
            {
                half4 color = (tex2D(_MainTex, IN.texcoord) + _TextureSampleAdd) * IN.color;
                // Optional colour drain (summoning-sick cards): lerp RGB toward luminance grey.
                half gray = dot(color.rgb, half3(0.299, 0.587, 0.114));
                color.rgb = lerp(half3(gray, gray, gray), color.rgb, _Saturation);
                color.a *= roundedAlpha(IN.texcoord);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}
