Shader "UI/CircleRimGlow"
{
    // The play-mode gold "ethereal mist" rim glow (UI/CardHoverGlow), adapted to
    // hug a CIRCLE instead of a rounded-rect card. Used by the profile icon
    // picker to mark the selected avatar. Circle twin of UI/HexRimGlow.
    //
    // Works in pixel space: the C# driver feeds _GlowSize (this quad's pixel size)
    // and _CardSize (the hex cell's pixel size: width = 2*S, height = sqrt(3)*S)
    // every frame, so the gold band always lands exactly on the hex edge.
    //
    // Premultiplied alpha so it reads as light on dark mats AND composites over light ones.
    Properties
    {
        _GlowSize     ("Glow quad size (px)", Vector) = (200, 173, 0, 0)
        _CardSize     ("Circle size (px)", Vector) = (96, 96, 0, 0)
        _BleedPx      ("Inward bleed (px)", Float) = 6
        _GlowWidthPx  ("Glow reach (px)", Float) = 16
        _CoreWidthPx  ("Hot edge width (px)", Float) = 4
        [HDR]_GlowColor ("Glow color - mid (orange)", Color) = (1.45, 0.65, 0.14, 1)
        [HDR]_CoreColor ("Hot center color (gold)", Color) = (1.3, 1.07, 0.55, 1)
        [HDR]_OuterColor ("Outer fringe color (deep red)", Color) = (0.96, 0.17, 0.04, 1)
        _Speed        ("Flow speed", Float) = 0.55
        _NoiseScale   ("Noise scale (wispiness)", Float) = 3.0
        _WispPx       ("Tendril length (px)", Float) = 24
        _Pulse        ("Pulse amount", Range(0,1)) = 0.22
        _Intensity    ("Intensity (0..1)", Range(0,1)) = 1
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" "IgnoreProjector"="True" "PreviewType"="Plane" }
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
            float  _BleedPx, _GlowWidthPx, _CoreWidthPx;
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
            // Signed distance to a circle of radius r centred at the origin.
            float sdCircle (float2 p, float r)
            {
                return length(p) - r;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 p = (i.uv - 0.5) * _GlowSize.xy;      // pixels from center
                float  radius = _CardSize.y * 0.5;           // circle diameter = _CardSize.y
                float  d = sdCircle(p, radius);              // px; <0 inside circle

                float refPx = max(_CardSize.y, 1.0);
                float2 np = (p / refPx) * _NoiseScale;
                float  t  = _Time.y * _Speed;
                float  n1 = fbm(np + float2(t, t * 0.7));
                float  n2 = fbm(np * 1.9 - float2(t * 0.6, t * 0.95));
                float  wisp = saturate(n1 * 0.7 + n2 * 0.3);

                // push glow outward where noise is high -> licking tendrils
                float dd    = d - wisp * _WispPx;
                // FINITE-support falloff: exactly 0 beyond _GlowWidthPx.
                float outer = saturate(1.0 - max(dd, 0.0) / max(_GlowWidthPx, 1.0));
                outer = outer * outer;
                float inner = smoothstep(-_BleedPx, 0.0, d);  // keep off the art
                float glow  = outer * inner;

                glow *= lerp(1.0 - _Pulse, 1.0 + _Pulse, wisp);
                glow *= 1.0 + _Pulse * 0.5 * sin(_Time.y * 3.0);

                float core = exp(-abs(d) / max(_CoreWidthPx, 1.0)) * inner;

                glow *= _Intensity;
                core *= _Intensity;

                // Deep red fringe -> rich orange mid -> gold core.
                float3 bandCol = lerp(_OuterColor.rgb, _GlowColor.rgb, saturate(outer));
                float3 col = bandCol * glow + _CoreColor.rgb * core;
                float a = saturate(glow + core);
                return fixed4(col, a);
            }
            ENDCG
        }
    }
}
