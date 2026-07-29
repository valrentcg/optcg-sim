Shader "Spellbind/ArrowBeam"
{
    Properties
    {
        [HDR] _Fringe  ("Fringe", Color) = (0.03, 0.12, 0.46, 1)
        [HDR] _Hue     ("Hue", Color) = (0.10, 0.34, 0.93, 1)
        [HDR] _Light   ("Light", Color) = (0.32, 0.57, 0.98, 1)
        [HDR] _CoreCol ("Core", Color) = (0.70, 0.83, 1.00, 1)
        _Intensity ("Intensity", Float) = 1
        _Bloom ("Bloom", Range(0,1)) = 0.52
        _Surge ("Surge", Range(0,1)) = 0.55
        _From ("Materialize From", Range(-0.2,1.2)) = 0
        _Fade ("Master Fade", Range(0,1)) = 1
        _Eclipse ("Eclipse (dark body, corona rim)", Range(0,1)) = 1
        _RimPos ("Rim position", Range(0.5,0.98)) = 0.86
        _RimWidth ("Rim width", Range(0.02,0.40)) = 0.11
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "IgnoreProjector" = "True"
            "CanUseSpriteAtlas" = "False"
        }
        // Premultiplied source-over is self-contained. It cannot subtract the
        // board colour or leave the dark "void" that the former additive,
        // full-screen carrier quad produced while it moved.
        Blend One OneMinusSrcAlpha
        ColorMask RGBA
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma target 3.0
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            #define ARM_SPAN 0.16

            struct appdata
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;   // x: signed cross-section, y: flow
                float2 data : TEXCOORD1; // x: layer (0 outer, 1 mid, 2 core)
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float layer : TEXCOORD1;
            };

            fixed4 _Fringe, _Hue, _Light, _CoreCol;
            float _Intensity, _Bloom, _Surge, _From, _Fade;
            float _Eclipse, _RimPos, _RimWidth;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.color = v.color;
                o.uv = v.uv;
                o.layer = v.data.x;
                return o;
            }

            float SurgePulse(float u, float t)
            {
                float value = 0.0;
                [unroll]
                for (int i = 0; i < 2; i++)
                {
                    float headPos = frac(t * 0.25 + i * 0.5)
                                  * (1.0 + ARM_SPAN + 0.30) - 0.18;
                    float d = u - headPos;
                    float sig = d > 0.0 ? 0.055 : 0.20;
                    float x = d / sig;
                    value += exp(-x * x) * (i == 0 ? 1.0 : 0.5);
                }
                return min(value, 1.05);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float x = abs(i.uv.x);
                // Every ribbon has a genuine zero-width terminal vertex. This
                // antialiases its actual silhouette; there is no independent
                // circular cap or apex mask that can stop on a flat plane.
                float aa = max(fwidth(x), 0.006);
                float coverage = 1.0 - smoothstep(1.0 - aa, 1.0 + aa, x);

                float3 rgb;
                float alpha;
                if (i.layer < 0.5)
                {
                    rgb = lerp(_Hue.rgb * 0.34, _Fringe.rgb * 0.18,
                               smoothstep(0.18, 1.0, x));
                    alpha = 0.18 * (0.5 + _Bloom * 1.1);
                }
                else if (i.layer < 1.5)
                {
                    rgb = lerp(_Light.rgb * 0.72, _Hue.rgb * 0.38,
                               smoothstep(0.22, 1.0, x));
                    alpha = 0.40 * (0.5 + _Bloom * 1.1);
                }
                else
                {
                    rgb = x < 0.18
                        ? lerp(_CoreCol.rgb, _Light.rgb * 0.92, x / 0.18)
                        : lerp(_Light.rgb * 0.92, _Hue.rgb * 0.56,
                               smoothstep(0.18, 1.0, x));
                    alpha = 0.78;
                }

                // ---- ECLIPSE ----------------------------------------------
                // The profile above is brightest at the centreline. Eclipse
                // inverts it: the interior falls to near-black and the light
                // collects in a band just inside the silhouette edge, so the
                // arrow reads as an occluding body wearing a corona rather than
                // a glowing rod. Premultiplied blending is what makes the dark
                // interior possible at all - it can occlude, which pure additive
                // never could.
                if (_Eclipse > 0.001)
                {
                    float rim = exp(-pow((x - _RimPos) / max(_RimWidth, 0.01), 2.0));
                    float3 eDark = _Fringe.rgb * 0.06;
                    float3 eCol;
                    float eAlpha;
                    if (i.layer < 0.5)
                    {
                        eCol = lerp(eDark, _Hue.rgb * 0.30, rim);
                        eAlpha = 0.16 * (0.5 + _Bloom * 1.1);
                    }
                    else if (i.layer < 1.5)
                    {
                        eCol = lerp(eDark, _Light.rgb * 0.55, rim);
                        eAlpha = lerp(0.18, 0.46, rim) * (0.5 + _Bloom * 1.1);
                    }
                    else
                    {
                        eCol = lerp(eDark, lerp(_CoreCol.rgb, _Light.rgb, 0.30), rim);
                        eAlpha = lerp(0.40, 0.92, rim);
                    }
                    rgb = lerp(rgb, eCol, _Eclipse);
                    alpha = lerp(alpha, eAlpha, _Eclipse);
                }

                float pulse = _Surge > 0.02 ? SurgePulse(i.uv.y, _Time.y) : 0.0;
                float amplitude = 0.25 + _Surge * 0.85;
                float gate = saturate((i.uv.y - _From) / 0.08);
                alpha *= coverage * i.color.a * gate * _Fade;
                alpha = saturate(alpha * _Intensity * (1.0 + amplitude * pulse));
                rgb *= 1.0 + amplitude * pulse;

                // Premultiplied output keeps the luminous colour while avoiding
                // both black fringes and framebuffer-dependent additive trails.
                // Material Color values already arrive in the active project
                // colour space. Converting them here a second time made the
                // linear project capture almost black.
                rgb = saturate(rgb * (1.18 + 0.42 * _Bloom)) * alpha;
                clip(alpha - 0.001);
                return fixed4(rgb, alpha);
            }
            ENDCG
        }
    }
}
