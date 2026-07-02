Shader "UI/CardHoverGlow"
{
    // Animated golden "ethereal mist" rim glow that hugs a rounded-rect card,
    // in the spirit of Hearthstone's playable-card highlight.
    //
    // Works in pixel space: the C# driver feeds _GlowSize (this quad's pixel size)
    // and _CardSize (the card's pixel size) every frame, so the gold band always
    // lands exactly on the card edge regardless of card dimensions or aspect.
    //
    // Premultiplied alpha so it reads as light on dark mats AND composites over light ones.
    Properties
    {
        _GlowSize     ("Glow quad size (px)", Vector) = (300, 420, 0, 0)
        _CardSize     ("Card size (px)", Vector) = (234, 327, 0, 0)
        _CornerPx     ("Corner radius (px)", Float) = 16
        _BleedPx      ("Inward bleed (px)", Float) = 8
        _GlowWidthPx  ("Glow reach (px)", Float) = 22
        _CoreWidthPx  ("Hot edge width (px)", Float) = 5
        [HDR]_GlowColor ("Glow color - mid (orange)", Color) = (1.45, 0.65, 0.14, 1)
        [HDR]_CoreColor ("Hot center color (gold)", Color) = (1.3, 1.07, 0.55, 1)
        [HDR]_OuterColor ("Outer fringe color (deep red)", Color) = (0.96, 0.17, 0.04, 1)
        _Speed        ("Flow speed", Float) = 0.55
        _NoiseScale   ("Noise scale (wispiness)", Float) = 3.0
        _WispPx       ("Tendril length (px)", Float) = 34
        _Pulse        ("Pulse amount", Range(0,1)) = 0.22
        _Intensity    ("Intensity (hover 0..1)", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
        // Premultiplied alpha (rgb already carries the emitted light; alpha is coverage). Behaves like
        // additive over a DARK mat, but also composites over a LIGHT mat - so the glow reads richly on
        // bright zones (leader / life / deck backs) instead of washing out. No dark backing required.
        Blend One OneMinusSrcAlpha
        Cull Off  ZWrite Off  Lighting Off  ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };
            struct v2f     { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float4 color : COLOR; };

            float4 _GlowSize, _CardSize, _GlowColor, _CoreColor, _OuterColor;
            float  _CornerPx, _BleedPx, _GlowWidthPx, _CoreWidthPx;
            float  _Speed, _NoiseScale, _WispPx, _Pulse, _Intensity;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            float hash (float2 p)
            {
                p = frac(p * float2(123.34, 345.45));
                p += dot(p, p + 34.345);
                return frac(p.x * p.y);
            }
            float vnoise (float2 p)
            {
                float2 i = floor(p); float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash(i),            b = hash(i + float2(1,0));
                float c = hash(i + float2(0,1)), d = hash(i + float2(1,1));
                return lerp(lerp(a,b,f.x), lerp(c,d,f.x), f.y);
            }
            float fbm (float2 p)
            {
                float v = 0.0, amp = 0.5;
                for (int k = 0; k < 4; k++) { v += amp * vnoise(p); p *= 2.02; amp *= 0.5; }
                return v;
            }
            // signed distance to a rounded box (pixel units)
            float sdRoundBox (float2 p, float2 b, float r)
            {
                float2 q = abs(p) - b + r;
                return min(max(q.x, q.y), 0.0) + length(max(q, 0.0)) - r;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * _GlowSize.xy;     // pixels from center
                float2 b = _CardSize.xy * 0.5;
                float  d = sdRoundBox(p, b, _CornerPx);      // px; <0 inside card

                float refPx = max(_CardSize.y, 1.0);
                float2 np = (p / refPx) * _NoiseScale;
                float  t  = _Time.y * _Speed;
                float  n1 = fbm(np + float2(t, t * 0.7));
                float  n2 = fbm(np * 1.9 - float2(t * 0.6, t * 0.95));
                float  wisp = saturate(n1 * 0.7 + n2 * 0.3);

                // push glow outward where noise is high -> licking tendrils
                float dd    = d - wisp * _WispPx;
                // FINITE-support falloff: glow is exactly 0 beyond _GlowWidthPx, so it can
                // never reach the quad edge and clip into a rectangle.
                float outer = saturate(1.0 - max(dd, 0.0) / max(_GlowWidthPx, 1.0));
                outer = outer * outer;                        // soft shoulder
                float inner = smoothstep(-_BleedPx, 0.0, d);  // keep off the art
                float glow  = outer * inner;

                glow *= lerp(1.0 - _Pulse, 1.0 + _Pulse, wisp);
                glow *= 1.0 + _Pulse * 0.5 * sin(_Time.y * 3.0);

                float core = exp(-abs(d) / max(_CoreWidthPx, 1.0)) * inner;

                glow *= _Intensity;
                core *= _Intensity;

                // Warm gradient: deep red at the outer fringe -> rich orange mid -> gold core.
                // 'outer' is 0 far from the card and 1 right at the edge.
                float3 bandCol = lerp(_OuterColor.rgb, _GlowColor.rgb, saturate(outer));
                float3 col = bandCol * glow + _CoreColor.rgb * core;
                // Coverage = how much glow is here. With premultiplied blending this attenuates the
                // background by (1 - a) and adds 'col' on top, so a bright zone gets replaced by gold
                // (pops) while empty areas stay fully transparent (a -> 0, no halo/shadow). The HDR
                // colours keep 'col' >= a, so the result only ever brightens toward gold, never darkens.
                float a = saturate(glow + core);
                return fixed4(col, a);
            }
            ENDCG
        }
    }
}
