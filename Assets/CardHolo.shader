Shader "UI/CardHolo"
{
    // Holographic UGUI card shader (superset of UI/RoundedCard).
    //  * "Spotlight glints": soft round twinkling glow points over the whole card so every
    //    SR/SEC card reads shiny. SR = white/faint-iridescent, SEC = gold.
    //  * Falling embers: slow randomized warm embers drifting down (enabled for red cards).
    // All driven by _Time + a _Pointer the CardHoloDriver feeds. Rounded corners via _Size/_Radius.
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _FoilTex ("Foil pattern (R=cell, G=facet)", 2D) = "black" {}
        _Size ("Rect size (px)", Vector) = (100,140,0,0)
        _Radius ("Corner radius (px)", Float) = 5

        _Pointer ("Pointer (uv)", Vector) = (0.5,0.5,0,0)
        _Pattern ("Pattern (0 SR,1 SEC)", Float) = 0
        _Intensity ("Glint intensity", Range(0,2)) = 0.55
        _Embers ("Embers on (0/1)", Float) = 0
        _EmberAmt ("Ember strength", Range(0,1)) = 0.85

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
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
        Stencil { Ref [_Stencil] Comp [_StencilComp] Pass [_StencilOp] ReadMask [_StencilReadMask] WriteMask [_StencilWriteMask] }
        Cull Off  Lighting Off  ZWrite Off  ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            Name "Default"
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0
            #include "UnityCG.cginc"
            #include "UnityUI.cginc"
            #pragma multi_compile_local _ UNITY_UI_CLIP_RECT
            #pragma multi_compile_local _ UNITY_UI_ALPHACLIP

            struct appdata_t { float4 vertex:POSITION; float4 color:COLOR; float2 texcoord:TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct v2f { float4 vertex:SV_POSITION; fixed4 color:COLOR; float2 texcoord:TEXCOORD0; float4 worldPosition:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };

            sampler2D _MainTex; sampler2D _FoilTex; fixed4 _Color; fixed4 _TextureSampleAdd; float4 _ClipRect; float4 _MainTex_ST;
            float4 _Size; float _Radius; float4 _Pointer; float _Pattern; float _Intensity; float _Embers; float _EmberAmt;

            v2f vert (appdata_t v){
                v2f OUT; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.worldPosition = v.vertex; OUT.vertex = UnityObjectToClipPos(OUT.worldPosition);
                OUT.texcoord = TRANSFORM_TEX(v.texcoord, _MainTex); OUT.color = v.color * _Color; return OUT;
            }

            float roundedAlpha (float2 uv){
                float2 p=(uv-0.5)*_Size.xy; float2 b=_Size.xy*0.5; float r=min(_Radius,min(b.x,b.y));
                float2 q=abs(p)-b+r; float d=min(max(q.x,q.y),0.0)+length(max(q,0.0))-r;
                float aa=max(fwidth(d),1e-4); return saturate(0.5-d/aa);
            }
            float hash21(float2 p){ p=frac(p*float2(123.34,345.45)); p+=dot(p,p+34.345); return frac(p.x*p.y); }
            float3 hue2rgb(float h){ h=frac(h)*6.0; return saturate(float3(abs(h-3.0)-1.0, 2.0-abs(h-2.0), 2.0-abs(h-4.0))); }

            // one layer of slow falling embers — small, sparse, bright glowing points
            float emberLayer(float2 uv, float t, float cols, float rows, float speed, float seed, float aspc){
                float2 gv = float2(uv.x*cols, uv.y*rows + t*speed);   // +t -> features fall downward
                float2 id = floor(gv); float2 f = frac(gv);
                float h = hash21(id + seed);
                if (h < 0.70) return 0.0;                             // only ~30% of cells carry an ember
                float ex = hash21(id + seed + 1.3);
                float ey = hash21(id + seed + 2.7);
                float sway = sin((t + h*6.2831)*1.1) * 0.16;
                float cw = (1.0/cols)*aspc, ch = (1.0/rows);
                float2 d = float2((f.x - ex - sway) * (cw/ch), f.y - ey);
                float dist = length(d);
                float core = smoothstep(0.10, 0.0, dist);            // tiny bright core
                float halo = smoothstep(0.30, 0.0, dist) * 0.35;     // soft glow around it
                float flick = 0.6 + 0.4*sin(t*(2.0 + h*2.0) + h*30.0);
                return (core + halo) * max(flick, 0.0);
            }

            fixed4 frag (v2f IN) : SV_Target
            {
                float2 uv = IN.texcoord;
                half4 base = (tex2D(_MainTex, uv) + _TextureSampleAdd) * IN.color;
                float t = _Time.y;
                float2 px = uv * float2(_Size.x, _Size.y);
                float aspc = _Size.x / max(_Size.y, 1.0);

                // ---- Cracked-ice prismatic foil, computed in-shader (voronoi facets). The
                //      facets shift colour with tilt = authentic holographic. No texture needed. ----
                float2 view = (_Pointer.xy - 0.5) * 2.0;                         // tilt/drag
                float2 fuv = uv * float2(7.0*aspc, 7.0) + view*0.6 + float2(t*0.03, -t*0.02);
                float2 gg = floor(fuv), ff = frac(fuv);
                float d1 = 9.0, d2 = 9.0, cellH = 0.0;
                for (int yy = -1; yy <= 1; yy++)
                  for (int xx = -1; xx <= 1; xx++){
                      float2 nb = float2(xx, yy);
                      float2 o  = float2(hash21(gg+nb), hash21(gg+nb+3.7));
                      float2 pp = nb + o - ff;
                      float dd = dot(pp, pp);
                      if (dd < d1){ d2 = d1; d1 = dd; cellH = hash21(gg+nb+11.3); }
                      else if (dd < d2) d2 = dd;
                  }
                float edge = sqrt(d2) - sqrt(d1);
                float facet = smoothstep(0.10, 0.0, edge);                       // bright crack lines
                float hueShift = (view.x - view.y) * 0.6 + t * 0.03;            // tilt drives colour shift
                float prof = 0.30 + 0.70 * facet;
                float3 sheenCol = (_Pattern < 0.5)
                    ? hue2rgb(frac(cellH * 1.3 + hueShift))                       // SR: faceted rainbow
                    : lerp(float3(0.55,0.40,0.12), float3(1.0,0.92,0.55),
                           frac(cellH * 1.1 + hueShift));                        // SEC: faceted gold

                // ---- Falling embers (red cards) ----
                float emb = 0.0;
                if (_Embers > 0.5){
                    emb = emberLayer(uv, t, 7.0, 11.0, 0.55, 0.0,  aspc)
                        + emberLayer(uv, t, 11.0, 16.0, 0.90, 7.7, aspc) * 0.6;
                    emb = saturate(emb);
                }
                float3 embCol = lerp(float3(1.0, 0.28, 0.04), float3(1.0, 0.70, 0.22), emb); // orange-red

                // ---- Compose (additive; whole card) ----
                float3 col = base.rgb;
                col += sheenCol * prof  * _Intensity * 0.80;     // foil colour
                col += facet    * _Intensity * 0.25;             // bright cracked-facet glints
                col += embCol   * emb  * _EmberAmt  * 0.9;       // embers (red only; shader copy off)
                col = saturate(col);

                half4 outc = half4(col, base.a);
                outc.a *= roundedAlpha(uv);
                #ifdef UNITY_UI_CLIP_RECT
                outc.a *= UnityGet2DClipping(IN.worldPosition.xy, _ClipRect);
                #endif
                #ifdef UNITY_UI_ALPHACLIP
                clip(outc.a - 0.001);
                #endif
                return outc;
            }
            ENDCG
        }
    }
}
