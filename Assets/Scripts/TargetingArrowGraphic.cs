using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedural targeting arrow. Builds a single quad-strip mesh (shaft + two head
/// arms) whose UVs carry a continuous flow parameter, so one additive material
/// draws the whole thing in one draw call.
///
/// UV layout:
///   uv.x = flow parameter. 0 at the origin, 1 at the tip, 1..1.16 out each arm.
///   uv.y = 0 at one edge, 1 at the other. The shader builds the cross-section
///          from this, so the mesh is a flat ribbon with no internal detail.
///
/// PORT NOTE — why this is a uGUI Graphic and not a MeshFilter/MeshRenderer.
/// Every canvas in this project is ScreenSpaceOverlay. A world-space MeshRenderer
/// draws BEHIND the whole UI there, silently — the same reason ParticleSystem was
/// unusable for the card-burn VFX. Rendering through a CanvasRenderer is the only
/// way the beam appears above the board. Nothing the design depends on is lost:
/// it is still ONE mesh in ONE draw call, uv.x still runs continuously across the
/// branch so a single pulse splits at the point, and the shader still does the
/// entire cross-section. Two things did have to change:
///   • units are canvas pixels, not world units (see coreHalfWidth);
///   • a CanvasRenderer ignores MaterialPropertyBlock, so each arrow owns a
///     material instance and the ramp is pushed onto that.
///
/// Coordinates: callers pass SCREEN points. Under an overlay canvas those are the
/// canvas's own space, but the conversion goes through RectTransformUtility so a
/// scaled canvas or a future ScreenSpaceCamera setup stays correct.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class TargetingArrowGraphic : MaskableGraphic
{
    public enum ArrowState { Aim, Valid, Invalid }

    [System.Serializable]
    public struct Ramp
    {
        public Color fringe;   // darkest, outer haze
        public Color hue;      // saturated body
        public Color light;    // inner
        public Color core;     // near-white centre. NEVER pure white.

        public static Ramp Lerp(Ramp a, Ramp b, float t)
        {
            Ramp r;
            r.fringe = Color.Lerp(a.fringe, b.fringe, t);
            r.hue    = Color.Lerp(a.hue,    b.hue,    t);
            r.light  = Color.Lerp(a.light,  b.light,  t);
            r.core   = Color.Lerp(a.core,   b.core,   t);
            return r;
        }
    }

    // ---------------------------------------------------------------- tuning
    [Header("Shape")]
    [Range(0f, 1f)] public float splay  = 0.55f;  // lateral curvature
    [Range(0f, 1f)] public float weight = 0.46f;  // spring lag at the tip
    [Range(0f, 1f)] public float width  = 0.52f;  // shaft thickness
    [Range(0f, 1f)] public float head   = 0.45f;  // arrowhead length
    [Range(0f, 1f)] public float sweep  = 0.42f;  // how wide the head opens

    /// <summary>Half-width of the hot core at full thickness, in CANVAS PIXELS.
    /// The spec's unitScale is in world units; this board is a pixel-space overlay
    /// canvas, so it is re-expressed here. 6.5 keeps the core visually equal to the
    /// old arrow, whose full body width was 12-14 px.</summary>
    public float coreHalfWidth = 6.5f;

    [Header("Look")]
    [Range(0f, 1f)] public float bloom = 0.52f;
    [Range(0f, 1f)] public float surge = 0.55f;
    [Range(0f, 1f)] public float shift = 0.45f;   // state cross-fade speed
    public float intensity = 1.6f;                // HDR multiplier, feeds Bloom

    [Header("Timing (seconds)")]
    public float materializeTime = 0.13f;
    public float collapseTime    = 0.15f;

    [Header("State colours")]
    public Ramp aim;
    public Ramp valid;
    public Ramp invalid;

    // ------------------------------------------------------------- constants
    const int   DENSE     = 80;    // bezier samples used for arc-length lookup
    const int   SHAFT     = 34;    // shaft quads
    // 16, not the spec's 9. The arms taper on a (1-f)^0.55 curve, which bends hardest near the
    // outer ends; at 9 segments that reads as visible faceting on a head this large on screen.
    const int   ARM       = 16;    // quads per head arm
    const float ARM_SPAN  = 0.16f; // flow parameter length of each arm
    const float MESH_MUL  = 3.4f;  // mesh half-width vs. core half-width
    const float SWELL     = 0.16f; // width gain at the crest of the surge

    // ----------------------------------------------------------------- state
    readonly Vector2[] dense = new Vector2[DENSE + 1];
    readonly float[]   cum   = new float[DENSE + 1];
    readonly Vector2[] sPos  = new Vector2[SHAFT + 1];
    readonly float[]   sHalf = new float[SHAFT + 1];
    readonly float[]   sU    = new float[SHAFT + 1];

    // Local-space (canvas) geometry, produced by Rebuild and consumed by OnPopulateMesh.
    readonly Vector2[] outVerts = new Vector2[(SHAFT + 1) * 2 + (ARM + 1) * 2 * 2];
    readonly Vector2[] outUvs   = new Vector2[(SHAFT + 1) * 2 + (ARM + 1) * 2 * 2];
    bool hasGeometry;

    Vector2 origin, aimPoint, tip, vel;
    bool active;
    float grow;
    float collapseT = -1f;
    Vector2 collapseOrigin, collapseTip;
    ArrowState state = ArrowState.Aim;
    Ramp cur;
    Material mat;

    static readonly int ID_FRINGE = Shader.PropertyToID("_Fringe");
    static readonly int ID_HUE    = Shader.PropertyToID("_Hue");
    static readonly int ID_LIGHT  = Shader.PropertyToID("_Light");
    static readonly int ID_CORE   = Shader.PropertyToID("_CoreCol");
    static readonly int ID_INT    = Shader.PropertyToID("_Intensity");
    static readonly int ID_BLOOM  = Shader.PropertyToID("_Bloom");
    static readonly int ID_SURGE  = Shader.PropertyToID("_Surge");
    static readonly int ID_FROM   = Shader.PropertyToID("_From");
    static readonly int ID_FADE   = Shader.PropertyToID("_Fade");

    /// <summary>No texture — the shader is entirely procedural.</summary>
    public override Texture mainTexture => s_WhiteTexture;

    // ------------------------------------------------------------ public API
    public void Begin(Vector2 screenOrigin)
    {
        origin   = ToLocal(screenOrigin);
        aimPoint = origin + Vector2.up * coreHalfWidth;
        tip      = aimPoint;
        vel      = Vector2.zero;
        grow     = 0f;
        active   = true;
        collapseT = -1f;
        enabled  = true;
        canvasRenderer.cull = false;
    }

    /// <summary>Call every frame while dragging. Screen-space point.</summary>
    public void Aim(Vector2 screenPoint, ArrowState s)
    {
        aimPoint = ToLocal(screenPoint);
        state    = s;
    }

    /// <summary>Per-frame drive for callers that recompute BOTH ends each frame — the board's
    /// arrows anchor their origin just outside the source card, so it slides as the aim swings
    /// round. Starts the arrow on first call, so callers do not have to track that themselves.
    /// The spring state survives because GameManager keeps these beams PERSISTENT and parented to
    /// the canvas — the arrow roots themselves are rebuilt every frame, and a beam living on one
    /// was destroyed and re-created constantly, which reset the materialize sweep every frame and
    /// threw away the spring velocity that gives the arrow its weight.</summary>
    public void Track(Vector2 screenOrigin, Vector2 screenTip, ArrowState s)
    {
        if (!active)
        {
            Begin(screenOrigin);
            // Begin seeds the tip at the origin; without this the first frame of every
            // arrow springs out from the source instead of appearing along its length.
            tip = ToLocal(screenTip);
        }
        origin = ToLocal(screenOrigin);
        Aim(screenTip, s);
    }

    /// <summary>Release. The arrow collapses into the tip rather than vanishing.</summary>
    public void End()
    {
        if (!active) return;
        active = false;
        collapseOrigin = origin;
        collapseTip    = tip;
        collapseT      = 0f;
    }

    /// <summary>Hard stop with no collapse animation (leaving the screen, match end).</summary>
    public void Clear()
    {
        active = false;
        collapseT = -1f;
        hasGeometry = false;
        canvasRenderer.cull = true;
        SetVerticesDirty();
    }

    public bool IsShowing => active || collapseT >= 0f;

    // --------------------------------------------------------------- unity
    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
        ApplyDefaultRampsIfUnset();
        cur = aim;

        var sh = Shader.Find("Spellbind/ArrowBeam");
        if (sh != null)
        {
            mat = new Material(sh) { hideFlags = HideFlags.DontSave };
            material = mat;
        }
        else
        {
            // Without the shader the additive ribbon would render as flat white over
            // the board — worse than nothing. Disable rather than misdraw.
            Debug.LogWarning("[TargetingArrow] Spellbind/ArrowBeam not found — arrow disabled.");
            enabled = false;
        }
        canvasRenderer.cull = true;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (mat != null) DestroyImmediate(mat);
    }

    /// <summary>Reset() only runs in the editor, so the runtime initialiser lives here.
    /// Values lifted straight from the prototype; sRGB 0-255 in the comments.</summary>
    void ApplyDefaultRampsIfUnset()
    {
        if (aim.core.a > 0f) return;   // already configured in the inspector

        aim.fringe     = New32( 20,  46, 110);  // #142E6E
        aim.hue        = New32( 62, 110, 224);  // #3E6EE0
        aim.light      = New32(136, 178, 252);  // #88B2FC
        aim.core       = New32(240, 247, 255);  // #F0F7FF

        valid.fringe   = New32( 10,  74,  48);  // #0A4A30
        valid.hue      = New32( 36, 170, 104);  // #24AA68
        valid.light    = New32(126, 224, 168);  // #7EE0A8
        valid.core     = New32(238, 254, 246);  // #EEFEF6

        invalid.fringe = New32(104,  16,  30);  // #68101E
        invalid.hue    = New32(206,  48,  62);  // #CE303E
        invalid.light  = New32(248, 132, 124);  // #F8847C
        invalid.core   = New32(255, 238, 232);  // #FFEEE8
    }

    static Color New32(int r, int g, int b) { return new Color32((byte)r, (byte)g, (byte)b, 255); }

    void LateUpdate()
    {
        if (mat == null) return;
        float dt = Time.unscaledDeltaTime;   // the board pauses; the arrow should not
        FadeColour(dt);

        if (active)
        {
            grow = Mathf.Min(1f, grow + dt / Mathf.Max(0.01f, materializeTime));

            // Critically-ish damped spring. This lag is what gives the arrow weight.
            // Stiffness is in canvas pixels here, so dt is clamped: a hitch big enough
            // to make the explicit integrator overshoot would fling the tip.
            float step = Mathf.Min(dt, 0.033f);
            float stiff = 340f - weight * 230f;
            float damp  = 2f * Mathf.Sqrt(stiff) * 0.82f;
            Vector2 acc = (aimPoint - tip) * stiff - vel * damp;
            vel += acc * step;
            tip += vel * step;

            float from = 1f - EaseOutQuint(grow);
            if (Rebuild(origin, tip)) Push(from, 1f);
            else { canvasRenderer.cull = true; hasGeometry = false; SetVerticesDirty(); }
        }
        else if (collapseT >= 0f)
        {
            collapseT += dt;
            float k = collapseT / Mathf.Max(0.01f, collapseTime);
            if (k >= 1f) { Clear(); return; }
            if (Rebuild(collapseOrigin, collapseTip)) Push(EaseOutQuint(k) * 0.96f, (1f - k) * (1f - k));
        }
    }

    void Push(float from, float fade)
    {
        canvasRenderer.cull = false;
        mat.SetColor(ID_FRINGE, cur.fringe);
        mat.SetColor(ID_HUE,    cur.hue);
        mat.SetColor(ID_LIGHT,  cur.light);
        mat.SetColor(ID_CORE,   cur.core);
        mat.SetFloat(ID_INT,    intensity);
        mat.SetFloat(ID_BLOOM,  bloom);
        mat.SetFloat(ID_SURGE,  surge);
        mat.SetFloat(ID_FROM,   from);
        mat.SetFloat(ID_FADE,   fade);
        SetVerticesDirty();     // geometry animates (the surge swells the ribbon)
    }

    void FadeColour(float dt)
    {
        Ramp target = state == ArrowState.Valid ? valid
                    : state == ArrowState.Invalid ? invalid
                    : aim;
        // Cross-fade, don't swap. Swapping pops and costs you a draw call per state.
        float f = Mathf.Min(1f, dt * (6f + shift * 34f));
        cur = Ramp.Lerp(cur, target, f);
    }

    Vector2 ToLocal(Vector2 screenPoint)
    {
        var cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform, screenPoint, cam, out var local);
        return local;
    }

    // ---------------------------------------------------------------- surge
    /// <summary>
    /// Asymmetric Gaussian travelling along the flow parameter. Sharp leading
    /// edge, long trailing tail, so it reads as moving toward the target.
    /// MUST match SurgePulse() in ArrowBeam.shader exactly — ArrowBeamParityCheck
    /// (Editor) fails the build if the two drift apart.
    /// </summary>
    public static float Surge(float u, float t, float amount)
    {
        if (amount <= 0.02f) return 0f;
        float v = 0f;
        for (int i = 0; i < 2; i++)
        {
            float headPos = Mathf.Repeat(t * 0.50f + i * 0.5f, 1f) * (1f + ARM_SPAN + 0.30f) - 0.18f;
            float d   = u - headPos;
            float sig = d > 0f ? 0.055f : 0.20f;
            float x   = d / sig;
            v += Mathf.Exp(-x * x) * (i == 0 ? 1f : 0.5f);
        }
        return Mathf.Min(v, 1.05f);
    }

    // ------------------------------------------------------------ mesh build
    bool Rebuild(Vector2 p0, Vector2 tipPos)
    {
        // --- control point: horizontal offset ONLY. dx == 0 means dead straight.
        float dx = tipPos.x - p0.x;
        Vector2 p1 = (p0 + tipPos) * 0.5f;
        p1.x += dx * 0.62f * splay;
        // sag: MINUS y. uGUI local space is Y-UP like the rest of Unity, so this keeps
        // the spec's sign. (The browser prototype's canvas is Y-down — porting the
        // sign across without flipping it is what bows the arrow the wrong way.)
        p1.y -= Mathf.Abs(dx) * 0.09f * splay;

        for (int i = 0; i <= DENSE; i++)
        {
            float t = (float)i / DENSE, u = 1f - t;
            dense[i] = u * u * p0 + 2f * u * t * p1 + t * t * tipPos;
        }
        cum[0] = 0f;
        for (int i = 1; i <= DENSE; i++)
            cum[i] = cum[i - 1] + Vector2.Distance(dense[i], dense[i - 1]);

        float L = cum[DENSE];
        if (L < coreHalfWidth * 0.5f) return false;

        float scale   = coreHalfWidth * (0.45f + width * 1.1f);
        // THE MISSING TIP. The spec derives the head length from `scale` alone, which works in world
        // units where the arrow is only a couple of dozen core-widths long. In canvas pixels a board
        // arrow runs ~500 px against a 6.5 px core — roughly twice as slender — so that formula gave
        // a head ~33 px long and ~46 px wide: wider than it was long, a bowtie with no barbs and no
        // apex. Tying it to arrow LENGTH keeps the head in proportion at any range, with the spec's
        // formula as the floor for very short arrows and its 0.34L as the ceiling for very long ones.
        float armLen  = Mathf.Min(L * 0.34f, Mathf.Max(L * 0.16f, scale * (2.6f + head * 5.2f)));
        float time    = Time.unscaledTime;

        // --- shaft, resampled by ARC LENGTH so the head keeps its proportions
        for (int i = 0; i <= SHAFT; i++)
        {
            float s = (float)i / SHAFT * L;
            sPos[i]  = PointAt(s, L);
            sU[i]    = s / L;
            sHalf[i] = WidthAtS(s, L, armLen) * scale * MESH_MUL
                     * (1f + SWELL * Surge(sU[i], time, surge));
        }

        int v = 0;
        for (int i = 0; i <= SHAFT; i++)
        {
            Vector2 tan = (i == 0)      ? sPos[1] - sPos[0]
                        : (i == SHAFT)  ? sPos[SHAFT] - sPos[SHAFT - 1]
                                        : sPos[i + 1] - sPos[i - 1];
            Vector2 n = new Vector2(-tan.y, tan.x).normalized;
            outVerts[v] = sPos[i] + n * sHalf[i]; outUvs[v] = new Vector2(sU[i], 1f); v++;
            outVerts[v] = sPos[i] - n * sHalf[i]; outUvs[v] = new Vector2(sU[i], 0f); v++;
        }

        // --- head: two arms starting AT the tip, sweeping back.
        // Anchoring them at the tip is what stops the shaft protruding through.
        Vector2 back   = PointAt(Mathf.Max(0f, L - scale * 0.8f), L);
        Vector2 tipDir = (dense[DENSE] - back).normalized;
        float tipAng   = Mathf.Atan2(tipDir.y, tipDir.x);
        float spread   = 0.42f + sweep * 0.52f;
        float wMax     = WidthAtS(L, L, armLen) * scale * 1.85f * MESH_MUL;
        Vector2 tipP   = dense[DENSE];

        for (int side = 0; side < 2; side++)
        {
            float a = tipAng + Mathf.PI + (side == 0 ? -spread : spread);
            Vector2 dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
            Vector2 nrm = new Vector2(-dir.y, dir.x);
            for (int k = 0; k <= ARM; k++)
            {
                float f  = (float)k / ARM;
                float u  = 1f + f * ARM_SPAN;
                // 0.45 damps the swell on the head so it conducts rather than inflates
                // Full width AT the tip, exactly as specified. I briefly ramped this from zero to
                // "make a point" and it did the opposite: the arms at full width are what COVERS the
                // shaft's blunt end cap, and thinning them exposed it. The missing tip was never this
                // curve — it was armLen (below).
                float hw = wMax * Mathf.Pow(1f - f, 0.55f)
                         * (1f + SWELL * 0.45f * Surge(u, time, surge));
                Vector2 p = tipP + dir * (f * armLen);
                outVerts[v] = p + nrm * hw; outUvs[v] = new Vector2(u, 1f); v++;
                outVerts[v] = p - nrm * hw; outUvs[v] = new Vector2(u, 0f); v++;
            }
        }

        hasGeometry = true;
        return true;
    }

    /// <summary>Emits the strip built by Rebuild. One mesh, one draw call — the shaft
    /// and both arms share it so the flow parameter can cross the branch.</summary>
    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (!hasGeometry) return;

        var vert = UIVertex.simpleVert;
        vert.color = Color.white;          // the ramp lives in the material, not here
        for (int i = 0; i < outVerts.Length; i++)
        {
            vert.position = outVerts[i];
            vert.uv0 = outUvs[i];
            vh.AddVert(vert);
        }

        int baseV = 0;
        for (int i = 0; i < SHAFT; i++) EmitQuad(vh, baseV + i * 2);
        baseV = (SHAFT + 1) * 2;
        for (int a = 0; a < 2; a++)
        {
            for (int i = 0; i < ARM; i++) EmitQuad(vh, baseV + i * 2);
            baseV += (ARM + 1) * 2;
        }
    }

    static void EmitQuad(VertexHelper vh, int v)
    {
        vh.AddTriangle(v, v + 2, v + 3);
        vh.AddTriangle(v, v + 3, v + 1);
    }

    Vector2 PointAt(float s, float L)
    {
        if (s <= 0f) return dense[0];
        if (s >= L)  return dense[DENSE];
        int lo = 0, hi = DENSE;
        while (hi - lo > 1) { int m = (lo + hi) >> 1; if (cum[m] <= s) lo = m; else hi = m; }
        float f = (s - cum[lo]) / Mathf.Max(1e-5f, cum[hi] - cum[lo]);
        return Vector2.Lerp(dense[lo], dense[hi], f);
    }

    /// <summary>Width curve in arc-length space, normalized to the core half-width.</summary>
    float WidthAtS(float s, float L, float armLen)
    {
        float w = 0.32f + 0.68f * Mathf.Pow(Mathf.Min(s / (L * 0.55f), 1f), 0.8f);
        float dEnd = L - s, blend = armLen * 0.55f;
        if (dEnd < blend) w *= 0.55f + 0.45f * (dEnd / blend);
        return w;
    }

    static float EaseOutQuint(float t) { float u = 1f - t; return 1f - u * u * u * u * u; }
}
