Shader "Spellbind/ArrowBeam"
{
    // Additive unlit ribbon. The mesh is a flat quad strip with no internal
    // detail; the entire cross-section and the travelling pulse are computed
    // here from UVs. That is why there are no seams, no beads and no floating
    // shapes inside the beam.
    //
    //   uv.x = flow parameter (0 origin, 1 tip, 1..1.16 out each head arm)
    //   uv.y = 0..1 across the ribbon width
    //
    // Colours are HDR and are pushed from the C# side, so one material serves
    // every state.
    //
    // PROJECT NOTE: this game's canvases are ScreenSpaceOverlay, so the beam is
    // drawn by a uGUI Graphic through a CanvasRenderer rather than a
    // MeshRenderer (a world-space mesh renders BEHIND the entire UI here). A
    // CanvasRenderer ignores MaterialPropertyBlock, so TargetingArrowGraphic
    // owns a material instance per arrow and sets these properties on it. The
    // maths below is untouched by that.

    Properties
    {
        [HDR] _Fringe   ("Fringe (outer haze)", Color) = (0.08, 0.18, 0.43, 1)
        [HDR] _Hue      ("Hue (body)",          Color) = (0.24, 0.43, 0.88, 1)
        [HDR] _Light    ("Light (inner)",       Color) = (0.53, 0.70, 0.99, 1)
        [HDR] _CoreCol  ("Core (near-white)",   Color) = (0.94, 0.97, 1.00, 1)

        _Intensity ("Intensity",        Float)        = 1.6
        _Bloom     ("Bloom",            Range(0,1))   = 0.52
        _Surge     ("Surge",            Range(0,1))   = 0.55
        _From      ("Materialize From", Range(-0.2,1.2)) = 0
        _Fade      ("Master Fade",      Range(0,1))   = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent+10"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Blend One One          // additive: overlap saturates toward white on its own
        ZWrite Off
        ZTest LEqual
        Cull Off

        Pass
        {
            Name "ArrowBeam"
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; float2 hw : TEXCOORD1; };
            struct Varyings   { float4 positionHCS : SV_POSITION; float2 uv : TEXCOORD0; float hw : TEXCOORD1; };

            CBUFFER_START(UnityPerMaterial)
                float4 _Fringe, _Hue, _Light, _CoreCol;
                float  _Intensity, _Bloom, _Surge, _From, _Fade;
            CBUFFER_END

            #define ARM_SPAN 0.16

            // Asymmetric Gaussian travelling along the flow parameter.
            // Sharp leading edge (0.055), long trailing tail (0.20).
            // Must match TargetingArrowGraphic.Surge() in C# exactly.
            float SurgePulse(float u, float t)
            {
                float v = 0.0;
                [unroll]
                for (int i = 0; i < 2; i++)
                {
                    float headPos = frac(t * 0.50 + i * 0.5) * (1.0 + ARM_SPAN + 0.30) - 0.18;
                    float d   = u - headPos;
                    float sig = d > 0.0 ? 0.055 : 0.20;
                    float x   = d / sig;
                    // note: x*x, NOT pow(x,2) — pow() with a negative base is undefined in HLSL
                    v += exp(-x * x) * (i == 0 ? 1.0 : 0.5);
                }
                return min(v, 1.05);
            }

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                OUT.hw = IN.hw.x;      // local ribbon half-width in canvas px
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                // --- cross-section. v = 0 at the centreline, 1 at the ribbon edge.
                float v = abs(IN.uv.y * 2.0 - 1.0);

                // Three nested Gaussians replace the three draw passes of the
                // 2D prototype. Widths are fractions of the mesh half-width,
                // which is 3.4x the core half-width.
                // Tightened from the spec's 0.30 / 0.62, which put the haze out at ~4.4x the core
                // radius and read as a wide wash rather than a beam with an edge.
                //
                // The sigmas are fractions of the LOCAL half-width, which alone makes a point
                // impossible: thinning a ribbon to converge at the tip drives its core below a
                // pixel, and a sub-pixel bright line does not render thin, it renders RAGGED. That
                // is the "gnawed" arrowhead. So each sigma is also floored in PIXELS, using the
                // half-width the mesh writes into TEXCOORD1. A wide ribbon is unaffected (the
                // fractional term wins); a thin one keeps a crisp ~1 px core instead of dissolving.
                float hwPx = max(IN.hw, 0.5);
                float sCore = max(0.14, 0.90 / hwPx);
                float sMid  = max(0.22, 1.60 / hwPx);
                float sHaze = max(0.34, 2.60 / hwPx);
                float core = exp(-(v / sCore) * (v / sCore));
                float mid  = exp(-(v / sMid)  * (v / sMid));
                float haze = exp(-(v / sHaze) * (v / sHaze));

                float b = 0.5 + _Bloom * 1.1;

                // Value hierarchy: the core is a small fraction of the area and
                // the only bright element. Keep the fringe weights low.
                float3 c = _CoreCol.rgb * core
                         + _Light.rgb   * mid  * 0.55 * b
                         + _Hue.rgb     * haze * 0.22 * b
                         + _Fringe.rgb  * haze * 0.12 * b;

                // --- travelling surge: brightness only. Geometry swell is done
                // on the CPU in the mesh so it can follow the branch into the arms.
                float amp = 0.25 + _Surge * 0.85;
                float p   = _Surge > 0.02 ? SurgePulse(IN.uv.x, _Time.y) : 0.0;
                c *= (1.0 + amp * p);

                // --- materialize sweep, origin toward tip
                float gate = smoothstep(_From, _From + 0.08, IN.uv.x);

                return half4(c * _Intensity * gate * _Fade, 1.0);
            }
            ENDHLSL
        }
    }

    // ---------------------------------------------------------------------
    // Built-in Render Pipeline fallback. Identical maths; only the include
    // and the clip-space transform differ. THIS is the subshader this project
    // actually uses — GraphicsSettings has no scriptable pipeline set.
    // ---------------------------------------------------------------------
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "IgnoreProjector"="True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; float2 hw : TEXCOORD1; };
            struct v2f     { float4 pos : SV_POSITION;  float2 uv : TEXCOORD0; float hw : TEXCOORD1; };

            float4 _Fringe, _Hue, _Light, _CoreCol;
            float  _Intensity, _Bloom, _Surge, _From, _Fade;

            #define ARM_SPAN 0.16

            float SurgePulse(float u, float t)
            {
                float v = 0.0;
                [unroll]
                for (int i = 0; i < 2; i++)
                {
                    float headPos = frac(t * 0.50 + i * 0.5) * (1.0 + ARM_SPAN + 0.30) - 0.18;
                    float d   = u - headPos;
                    float sig = d > 0.0 ? 0.055 : 0.20;
                    float x   = d / sig;
                    v += exp(-x * x) * (i == 0 ? 1.0 : 0.5);
                }
                return min(v, 1.05);
            }

            v2f vert(appdata i)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(i.vertex);
                o.uv  = i.uv;
                o.hw  = i.hw.x;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float v = abs(i.uv.y * 2.0 - 1.0);
                // Tightened from the spec's 0.30 / 0.62, which put the haze out at ~4.4x the core
                // radius and read as a wide wash rather than a beam with an edge.
                //
                // The sigmas are fractions of the LOCAL half-width, which alone makes a point
                // impossible: thinning a ribbon to converge at the tip drives its core below a
                // pixel, and a sub-pixel bright line does not render thin, it renders RAGGED. That
                // is the "gnawed" arrowhead. So each sigma is also floored in PIXELS, using the
                // half-width the mesh writes into TEXCOORD1. A wide ribbon is unaffected (the
                // fractional term wins); a thin one keeps a crisp ~1 px core instead of dissolving.
                float hwPx = max(i.hw, 0.5);
                float sCore = max(0.14, 0.90 / hwPx);
                float sMid  = max(0.22, 1.60 / hwPx);
                float sHaze = max(0.34, 2.60 / hwPx);
                float core = exp(-(v / sCore) * (v / sCore));
                float mid  = exp(-(v / sMid)  * (v / sMid));
                float haze = exp(-(v / sHaze) * (v / sHaze));
                float b = 0.5 + _Bloom * 1.1;

                float3 c = _CoreCol.rgb * core
                         + _Light.rgb   * mid  * 0.55 * b
                         + _Hue.rgb     * haze * 0.22 * b
                         + _Fringe.rgb  * haze * 0.12 * b;

                float amp = 0.25 + _Surge * 0.85;
                float p   = _Surge > 0.02 ? SurgePulse(i.uv.x, _Time.y) : 0.0;
                c *= (1.0 + amp * p);

                float gate = smoothstep(_From, _From + 0.08, i.uv.x);
                return fixed4(c * _Intensity * gate * _Fade, 1.0);
            }
            ENDCG
        }
    }
}
