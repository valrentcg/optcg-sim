// Direct HLSL port of the browser prototype's Eclipse arrow.
//
// This is deliberately a FIELD shader on a bounds quad, not a ribbon mesh: the
// prototype is an analytic signed-distance construction and every part of its look
// (the eroded edge, the corona rim, the tip pinch, the runout surge) is expressed in
// terms of that field. Re-expressing it as mesh geometry produced something unrelated,
// which is what this file exists to correct. Keep the maths below numerically identical
// to arrow-mystic.html; if the two drift, this stops being a port.
Shader "Spellbind/ArrowEclipse"
{
    Properties
    {
        [HDR] _Fringe  ("Fringe (deep)", Color) = (0.04, 0.10, 0.34, 1)
        [HDR] _Hue     ("Hue (mid)",     Color) = (0.24, 0.34, 0.86, 1)
        [HDR] _Light   ("Light",         Color) = (0.62, 0.70, 0.98, 1)
        [HDR] _CoreCol ("Core (hot)",    Color) = (0.88, 0.92, 1.00, 1)

        _Width     ("Width",           Float) = 5.5
        _HeadW     ("Head half-width", Float) = 26
        _HeadL     ("Head length",     Float) = 50
        _Barb      ("Barb weight",     Range(0,1))    = 0.55
        _HeadCrv   ("Barb sweep",      Range(-1,1.6)) = -1
        _TipSharp  ("Tip sharpness",   Range(0,0.5))  = 0
        _TipPinch  ("Tip halo pinch",  Range(0,6))    = 0
        _Auto      ("Aim curvature",   Range(-2,2))   = 0.7
        _CurveShape("Curve response",  Range(1,4))    = 2.0
        _Curve     ("Fixed curvature", Range(0,1.2))  = 0.36
        _Wisp      ("Edge erosion",    Range(0,2.2))  = 0.30
        _Aura      ("Aura reach",      Range(0,3))    = 0.96
        _Detail    ("Fine detail",     Range(0,2))    = 0.14
        _Surge     ("Progression",     Range(0,2))    = 0.72
        _Expo      ("Exposure",        Range(0.3,2.2))= 1.14
        _From      ("Materialize",     Range(-0.2,1.2)) = 0
        _Fade      ("Master fade",     Range(0,1))    = 1
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "IgnoreProjector"="True" }
        Blend One OneMinusSrcAlpha        // premultiplied: the dark body must be able to occlude
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

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; float2 local : TEXCOORD0; };

            fixed4 _Fringe, _Hue, _Light, _CoreCol;
            float _Width, _HeadW, _HeadL, _Barb, _HeadCrv, _TipSharp, _TipPinch;
            float _Auto, _CurveShape, _Curve, _Wisp, _Aura, _Detail, _Surge, _Expo, _From, _Fade;
            float4 _EndA;   // xy = source, zw = tip, in the same local space as the quad

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.local = v.vertex.xy;
                return o;
            }

            // ---- noise (matches the prototype) --------------------------------
            float h21(float2 p)
            {
                float s = sin(dot(p, float2(127.1, 311.7))) * 43758.5453;
                return s - floor(s);
            }
            float vn(float2 p)
            {
                float2 i = floor(p), f = p - i;
                float2 u = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(h21(i), h21(i + float2(1, 0)), u.x),
                            lerp(h21(i + float2(0, 1)), h21(i + float2(1, 1)), u.x), u.y);
            }
            float fbm(float2 p)
            {
                float a = 0.0, m = 0.5;
                [unroll] for (int i = 0; i < 5; i++) { a += vn(p) * m; p *= 2.03; m *= 0.5; }
                return a;
            }

            static float2 A_, B_, C_;
            static float LEN;

            float2 bez(float t) { float u = 1.0 - t; return u*u*A_ + 2.0*u*t*C_ + t*t*B_; }

            void Setup()
            {
                A_ = _EndA.xy; B_ = _EndA.zw;
                float2 ab = B_ - A_;
                LEN = max(length(ab), 1e-3);
                float2 n = float2(-ab.y, ab.x) / LEN;
                float2 dir = ab / LEN;
                float lateral = -dir.x;                       // 0 dead ahead, +/-1 off to a side
                // Linear in lateral means a 15-degree drift already bends visibly. Raising
                // it to a power flattens the response near centre, so a forward aim reads
                // as a genuinely straight line and the bow only builds as the aim swings wide.
                float shaped = sign(lateral) * pow(abs(lateral), _CurveShape);
                float bow = (abs(_Auto) > 0.001) ? (_Auto * shaped) : _Curve;
                C_ = A_ + ab * 0.5 + n * bow * min(LEN * 0.32, 200.0);
            }

            float Spine(float2 p, out float tOut)
            {
                float best = 1e9; tOut = 0.0;
                float2 prev = A_;
                [unroll] for (int i = 1; i <= 18; i++)
                {
                    float t = (float)i / 18.0;
                    float2 cur = bez(t);
                    float2 ab = cur - prev;
                    float L2 = max(dot(ab, ab), 1e-6);
                    float on = saturate(dot(p - prev, ab) / L2);
                    float d = length(p - (prev + ab * on));
                    if (d < best) { best = d; tOut = ((float)(i - 1) + on) / 18.0; }
                    prev = cur;
                }
                return best;
            }

            float Taper(float t)
            {
                float tB = clamp(1.0 - _HeadL / max(LEN, 1e-3), 0.05, 0.95);
                float grow = _Width * (0.20 + 1.35 * pow(t, 0.70));
                return grow * (1.0 - smoothstep(tB * 0.86, 1.0, t));
            }

            float BarbW(float tt, float w0)
            {
                return w0 * (_TipSharp + (1.0 - _TipSharp) * pow(tt, 0.50)) * (1.0 - tt*tt*0.95);
            }
            float TaperSeg(float2 p, float2 a, float2 b, float ra, float rb)
            {
                float2 ab = b - a, ap = p - a;
                float t = saturate(dot(ap, ab) / max(dot(ab, ab), 1e-6));
                return length(ap - ab * t) - lerp(ra, rb, t);
            }
            float HeadD(float2 p, out float ht)
            {
                ht = 0.0;
                float2 dir = normalize(B_ - bez(0.86));
                float2 per = float2(-dir.y, dir.x);
                float2 basePt = B_ - dir * _HeadL;
                float w0 = _HeadW * (0.15 + 0.32 * _Barb);
                float d = 1e9;
                [unroll] for (int s = 0; s < 2; s++)
                {
                    float side = (s == 0) ? 1.0 : -1.0;
                    float2 endPt = basePt + per * side * _HeadW;
                    float2 ctrl = lerp(B_, endPt, 0.42)
                                + per * side * _HeadW * 0.42 * _HeadCrv
                                - dir * _HeadL * 0.10 * _HeadCrv;
                    float2 prev = B_; float rp = BarbW(0.0, w0);
                    [unroll] for (int i = 1; i <= 7; i++)
                    {
                        float tt = (float)i / 7.0, u = 1.0 - tt;
                        float2 cur = u*u*B_ + 2.0*u*tt*ctrl + tt*tt*endPt;
                        float r = BarbW(tt, w0);
                        float cand = TaperSeg(p, prev, cur, rp, r);
                        if (cand < d) { d = cand; ht = tt; }
                        prev = cur; rp = r;
                    }
                }
                return d;
            }

            float AA(float d) { return saturate(0.5 - d / max(fwidth(d), 1e-6)); }
            float Gauss(float d, float s) { float x = d / max(s, 1e-4); return exp(-x * x); }

            // RUNOUT: shaft, across the point, then out along both barbs, then rest.
            float Runout(float phase, float T, out float flare, out float wake)
            {
                float c = frac(T * 0.26);
                float span = 0.82;
                float env = smoothstep(0.0, 0.05 * span, c) * (1.0 - smoothstep(span * 0.86, span, c));
                float u = saturate(c / span);
                float sp = u * 2.0;
                float d = phase - sp;
                wake = exp(-max(0.0, sp - phase) / 0.50) * step(phase, sp) * env;
                flare = Gauss(sp - 1.0, 0.075) * env;
                return Gauss(d, (d > 0.0) ? 0.055 : 0.22) * env;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float2 p = i.local;
                Setup();

                float T = _Time.y;
                float t; float ds = Spine(p, t);
                float wid = Taper(t);

                float2 dirH = normalize(B_ - bez(0.86));
                float headT; float headDist = HeadD(p, headT);
                float shaftTube = ds - wid;
                float phase = (headDist < shaftTube) ? (1.0 + headT) : t;
                ds = min(shaftTube, headDist) + wid;
                ds += max(dot(p - B_, dirH), 0.0) * _TipPinch;

                float er = (fbm(p * 0.017 + float2(T * 0.20, -T * 0.13)) - 0.5) * _Wisp * _Width * 1.15;
                float d = ds - wid + er;

                float au = max(_Aura * _Width * 2.4, 0.001);
                float halo = pow(saturate(1.0 - max(ds - wid, 0.0) / au), 1.8);

                float3 deep = _Fringe.rgb, mid = _Hue.rgb, hot = _CoreCol.rgb;

                // ---- ECLIPSE ----
                float rim = abs(ds - wid * 0.85) - _Width * 0.16 + er * 0.4;
                float3 col = deep * halo * 1.4;
                col += hot * AA(rim) * 0.95;
                col = lerp(col, float3(0.006, 0.004, 0.012), AA(ds - wid * 0.80) * 0.96);
                col += mid * pow(saturate(1.0 - max(ds - wid * 0.85, 0.0) / (_Width * 3.0)), 2.0) * 0.55;

                if (_Detail > 0.01)
                {
                    float fine = fbm(p * 0.085 + float2(T * 0.05, -T * 0.035));
                    col *= 0.88 + 0.26 * fine * _Detail;
                }
                if (_Surge > 0.01)
                {
                    float flare, wake;
                    float sg = Runout(phase, T, flare, wake);
                    float onBody = AA(ds - wid * 0.60);
                    float landing = pow(saturate(1.0 - length(p - B_) / (_Width * 0.95)), 3.0);
                    col *= 0.80 + 0.45 * sg * _Surge;
                    col += hot * sg * onBody * 1.15 * _Surge;
                    col += deep * sg * halo * 0.80 * _Surge;
                    col += _Light.rgb * wake * onBody * 0.34 * _Surge;
                    col += hot * flare * landing * 0.80 * _Surge;
                }
                col *= 0.93 + 0.07 * sin(T * 0.55);
                col *= _Expo;

                // materialize gate, matching the driver's grow curve
                col *= saturate((t - _From) / 0.08);

                float alpha = saturate(max(max(col.r, col.g), col.b)) * _Fade;
                clip(alpha - 0.002);
                return fixed4(saturate(col) * _Fade, alpha);
            }
            ENDCG
        }
    }
}
