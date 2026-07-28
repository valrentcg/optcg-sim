Shader "Spellbind/ArrowBeam"
{
    Properties
    {
        [HDR] _Fringe  ("Fringe", Color) = (0.08, 0.18, 0.43, 1)
        [HDR] _Hue     ("Hue", Color) = (0.24, 0.43, 0.88, 1)
        [HDR] _Light   ("Light", Color) = (0.53, 0.70, 0.99, 1)
        [HDR] _CoreCol ("Core", Color) = (0.94, 0.97, 1.00, 1)
        _Intensity ("Intensity", Float) = 1
        _Bloom ("Bloom", Range(0,1)) = 0.52
        _Surge ("Surge", Range(0,1)) = 0.55
        _From ("Materialize From", Range(-0.2,1.2)) = 0
        _Fade ("Master Fade", Range(0,1)) = 1
        _HeadLengthLocal ("Head Length Local", Float) = 48
        _HeadMaxHalfLocal ("Head Max Half Local", Float) = 9
        _HeadLengthPx ("Head Length Pixels", Float) = 48
        _PixelToLocal ("Pixel To Local", Float) = 1
    }

    SubShader
    {
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent+10"
            "IgnoreProjector" = "True"
        }
        Blend One One
        ColorMask RGB
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

            #define SPINE_POINTS 33
            #define SPINE_SEGMENTS 32
            #define HEAD_SEGMENTS 16
            #define ARM_SPAN 0.16

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 localPos : TEXCOORD0;
            };

            fixed4 _Fringe, _Hue, _Light, _CoreCol;
            float _Intensity, _Bloom, _Surge, _From, _Fade;
            float4 _Spine[SPINE_POINTS];
            float4 _HeadAxisA, _HeadAxisB;
            float _HeadLengthLocal, _HeadMaxHalfLocal, _HeadLengthPx;
            float _PixelToLocal;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.localPos = v.vertex.xy;
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

            float3 OuterProfile(float d)
            {
                if (d < 0.16)
                    return _Hue.rgb * lerp(0.34, 0.28, d / 0.16);
                if (d < 0.48)
                    return lerp(_Hue.rgb * 0.28, _Fringe.rgb * 0.16,
                                (d - 0.16) / 0.32);
                return _Fringe.rgb * (0.16 * saturate((1.0 - d) / 0.52));
            }

            float3 MidProfile(float d)
            {
                if (d < 0.28)
                    return lerp(_Light.rgb * 0.68, _Hue.rgb * 0.50, d / 0.28);
                if (d < 0.60)
                    return lerp(_Hue.rgb * 0.50, _Fringe.rgb * 0.22,
                                (d - 0.28) / 0.32);
                return _Fringe.rgb * (0.22 * saturate((1.0 - d) / 0.40));
            }

            float3 CoreProfile(float d)
            {
                if (d < 0.16)
                    return lerp(_CoreCol.rgb, _Light.rgb * 0.90, d / 0.16);
                if (d < 0.42)
                    return lerp(_Light.rgb * 0.90, _Hue.rgb * 0.66,
                                (d - 0.16) / 0.26);
                if (d < 0.74)
                    return lerp(_Hue.rgb * 0.66, _Fringe.rgb * 0.34,
                                (d - 0.42) / 0.32);
                return _Fringe.rgb * (0.34 * saturate((1.0 - d) / 0.26));
            }

            float3 ReferenceToOutput(float3 srgb)
            {
                #if defined(UNITY_COLORSPACE_GAMMA)
                    return srgb;
                #else
                    float3 lo = srgb / 12.92;
                    float3 hi = pow((srgb + 0.055) / 1.055, 2.4);
                    return lerp(lo, hi, step(0.04045, srgb));
                #endif
            }

            void SampleArm(
                float2 localPos,
                float2 junction,
                float2 backDir,
                float2 endAxis,
                float headLength,
                float maxHalf,
                out float distanceToArm,
                out float armHalf,
                out float armT)
            {
                distanceToArm = 1e20;
                armHalf = 0.0;
                armT = 0.0;

                // Both barbs leave the apex along the shaft's backward tangent
                // before bending toward their separate endpoints. Their bodies
                // therefore occupy the same narrow region for the first few
                // pixels instead of three straight cones merely touching at one
                // coordinate. The quadratic separation is O(t^2), while width
                // grows linearly, creating a genuine three-body overlap.
                float2 control = junction + backDir * (headLength * 0.24);
                float2 armEnd = junction + endAxis * headLength;
                float2 sampleA = junction;

                [unroll]
                for (int segment = 0; segment < HEAD_SEGMENTS; segment++)
                {
                    float tB = (float)(segment + 1) / HEAD_SEGMENTS;
                    float oneMinusT = 1.0 - tB;
                    float2 sampleB =
                          oneMinusT * oneMinusT * junction
                        + 2.0 * oneMinusT * tB * control
                        + tB * tB * armEnd;
                    float2 ab = sampleB - sampleA;
                    float ab2 = max(dot(ab, ab), 1e-6);
                    float onSegment = saturate(
                        dot(localPos - sampleA, ab) / ab2);
                    float2 nearest = sampleA + ab * onSegment;
                    float candidate = length(localPos - nearest);

                    if (candidate < distanceToArm)
                    {
                        armT = ((float)segment + onSegment) / HEAD_SEGMENTS;
                        distanceToArm = candidate;

                        // The shared end sharpens linearly to zero; the free end
                        // keeps the longer v8-style taper. Because both curved
                        // centerlines initially coincide with the shaft, this
                        // can be narrow without reopening a seam.
                        // One continuous, two-ended envelope. Both ends use the
                        // same 0.55 taper character, but the shared end occupies
                        // 45% of the arm and the free end 70%. A power-mean union
                        // removes the shoulder made by multiplying a saturated
                        // entrance ramp by an unrelated exit curve.
                        if (armT <= 0.0 || armT >= 1.0)
                        {
                            armHalf = 0.0;
                        }
                        else
                        {
                            float joinEnvelope = pow(armT / 0.45, 0.55);
                            float freeEnvelope = pow(
                                (1.0 - armT) / 0.70, 0.55);
                            float inverseFourth =
                                  pow(max(joinEnvelope, 1e-5), -4.0)
                                + pow(max(freeEnvelope, 1e-5), -4.0);
                            float twoEndedEnvelope = pow(
                                inverseFourth, -0.25);
                            armHalf = maxHalf * twoEndedEnvelope;
                        }
                    }
                    sampleA = sampleB;
                }
            }

            float ApexEnvelope(
                float2 localPos,
                float2 apex,
                float2 backDir,
                float headLength,
                float pixelToLocal,
                float slope,
                float basePixels)
            {
                float2 fromApex = localPos - apex;
                float backward = dot(fromApex, backDir);
                float sideways = abs(
                    backDir.x * fromApex.y - backDir.y * fromApex.x);
                float aa = max(0.70 * pixelToLocal, 1e-5);

                // A subpixel base is coverage, not a geometric cap. It lets the
                // vertex occupy its final pixel while the linear sideways term
                // forces every following row to widen into a triangle.
                float halfAtRow = max(backward, 0.0) * slope
                                + basePixels * pixelToLocal;
                float sideCoverage = 1.0 - smoothstep(
                    halfAtRow - aa, halfAtRow + aa, sideways);
                float forwardCoverage = smoothstep(
                    -0.75 * pixelToLocal,
                     0.25 * pixelToLocal,
                    backward);

                // The envelope only governs the shared convergence. Blend fully
                // back to the native ribbon fields before the barbs separate.
                float convergence = 1.0 - smoothstep(
                    headLength * 0.13,
                    headLength * 0.24,
                    max(backward, 0.0));
                return lerp(
                    1.0,
                    sideCoverage * forwardCoverage,
                    convergence);
            }

            fixed4 frag(v2f i) : SV_Target
            {
                float shaftDistance = 1e20;
                float shaftHalf = 0.0;
                float shaftFlow = 0.0;

                // The curved shaft is sampled by arc length in C#. All samples,
                // including the zero-width point, participate in this one field.
                [unroll]
                for (int segment = 0; segment < SPINE_SEGMENTS; segment++)
                {
                    float4 sampleA = _Spine[segment];
                    float4 sampleB = _Spine[segment + 1];
                    float2 ab = sampleB.xy - sampleA.xy;
                    float ab2 = max(dot(ab, ab), 1e-6);
                    float along = saturate(dot(i.localPos - sampleA.xy, ab) / ab2);
                    float2 nearest = sampleA.xy + ab * along;
                    float distanceToSegment = length(i.localPos - nearest);
                    if (distanceToSegment < shaftDistance)
                    {
                        shaftDistance = distanceToSegment;
                        shaftHalf = lerp(sampleA.z, sampleB.z, along);
                        shaftFlow = lerp(sampleA.w, sampleB.w, along);
                    }
                }

                float2 junction = _Spine[SPINE_POINTS - 1].xy;
                float headLength = max(_HeadLengthLocal, 1e-4);
                float2 backDir = normalize(
                    _Spine[SPINE_POINTS - 2].xy - junction);
                float distanceA, distanceB;
                float armHalfA, armHalfB;
                float alongA, alongB;
                SampleArm(
                    i.localPos, junction, backDir, _HeadAxisA.xy,
                    headLength, _HeadMaxHalfLocal,
                    distanceA, armHalfA, alongA);
                SampleArm(
                    i.localPos, junction, backDir, _HeadAxisB.xy,
                    headLength, _HeadMaxHalfLocal,
                    distanceB, armHalfB, alongB);

                // Screen-space floors apply only to the increasingly soft
                // profiles. They make the blur wrap and extend beyond the exact
                // geometric point while the white core still converges there.
                float outerFloor = 3.25 * _PixelToLocal;
                float midFloor = 1.55 * _PixelToLocal;
                float coreFloor = 0.65 * _PixelToLocal;

                // The screen-space blur floors must taper with the geometry.
                // Leaving them at full radius when half-width reaches zero
                // creates a round, flat-ended bloom cap at the shared point.
                // Exponential activation has no derivative break. The previous
                // saturate at 1.4 pixels changed the longitudinal bloom slope
                // abruptly and drew a visible shoulder into every tapered end.
                float floorActivation = max(
                    0.75 * _PixelToLocal, 1e-5);
                float shaftFloorScale = 1.0
                    - exp(-shaftHalf / floorActivation);
                float armFloorScaleA = 1.0
                    - exp(-armHalfA / floorActivation);
                float armFloorScaleB = 1.0
                    - exp(-armHalfB / floorActivation);

                float shaftOuterD = shaftDistance / max(
                    shaftHalf * 3.4 + outerFloor * shaftFloorScale, 1e-5);
                float shaftMidD = shaftDistance / max(
                    shaftHalf * 1.7 + midFloor * shaftFloorScale, 1e-5);
                float shaftCoreD = shaftDistance / max(
                    shaftHalf + coreFloor * shaftFloorScale, 1e-5);
                float armOuterA = distanceA / max(
                    armHalfA * 3.4 + outerFloor * armFloorScaleA, 1e-5);
                float armOuterB = distanceB / max(
                    armHalfB * 3.4 + outerFloor * armFloorScaleB, 1e-5);
                float armMidA = distanceA / max(
                    armHalfA * 1.7 + midFloor * armFloorScaleA, 1e-5);
                float armMidB = distanceB / max(
                    armHalfB * 1.7 + midFloor * armFloorScaleB, 1e-5);
                float armCoreA = distanceA / max(
                    armHalfA + coreFloor * armFloorScaleA, 1e-5);
                float armCoreB = distanceB / max(
                    armHalfB + coreFloor * armFloorScaleB, 1e-5);

                bool armAOwns = armCoreA <= armCoreB;
                float armOuterD = min(armOuterA, armOuterB);
                float armMidD = min(armMidA, armMidB);
                float armCoreD = min(armCoreA, armCoreB);
                float armT = armAOwns ? alongA : alongB;
                float armFloorScale = armAOwns
                    ? armFloorScaleA : armFloorScaleB;

                // The curved bodies overlap structurally, so an exact union is
                // now sufficient. Smooth-min was compensating for disconnected
                // straight cones and inflated the shared end into a flat shelf.
                float outerD = min(shaftOuterD, armOuterD);
                float midD = min(shaftMidD, armMidD);
                float coreD = min(shaftCoreD, armCoreD);

                bool armOwns = armCoreD < shaftCoreD;
                float flow = armOwns ? 1.0 + armT * ARM_SPAN : shaftFlow;

                float pulse = _Surge > 0.02 ? SurgePulse(flow, _Time.y) : 0.0;
                float outerSwell = 1.0 + 0.22 * 0.35 * pulse;
                float midSwell = 1.0 + 0.22 * 0.80 * pulse;
                float coreSwell = 1.0 + 0.22 * 1.00 * pulse;

                // Nested convergence envelopes shape the already-unioned fields.
                // Each luminous layer reaches the same vertex, with the outer
                // bloom opening slightly faster than the hot core.
                float outerApex = ApexEnvelope(
                    i.localPos, junction, backDir, headLength,
                    _PixelToLocal, 1.05, 0.32);
                float midApex = ApexEnvelope(
                    i.localPos, junction, backDir, headLength,
                    _PixelToLocal, 0.78, 0.24);
                float coreApex = ApexEnvelope(
                    i.localPos, junction, backDir, headLength,
                    _PixelToLocal, 0.54, 0.16);

                float glowScale = 0.5 + _Bloom * 1.1;
                float3 color =
                    OuterProfile(outerD / outerSwell)
                    * (0.115 * glowScale) * outerApex;
                color +=
                    MidProfile(midD / midSwell)
                    * (0.26 * glowScale) * midApex;
                color +=
                    CoreProfile(coreD / coreSwell)
                    * 0.48 * coreApex;

                // The distance field already narrows every structural radius to
                // zero. Do not multiply endpoint energy down a second time: that
                // erased the only subpixel sample at the shared apex while the
                // independently sampled free tips remained visible.

                float amplitude = 0.25 + _Surge * 0.85;
                color *= 1.0 + amplitude * pulse;

                float gate = saturate((flow - _From) / 0.08);
                color = saturate(color * _Intensity * gate * _Fade);
                return fixed4(ReferenceToOutput(color), 0.0);
            }
            ENDCG
        }
    }
}
