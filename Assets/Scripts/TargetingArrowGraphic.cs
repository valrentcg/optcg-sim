using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Procedural targeting arrow rendered as one analytic distance field. The
/// sampled curved shaft, its zero-width point, both barbs, and their shared
/// blue convergence are unioned before shading, so no separately rendered
/// shaft/head boundary can clip, gap, or add twice.
///
/// PORT NOTE — why this is a uGUI Graphic and not a MeshFilter/MeshRenderer.
/// Every canvas in this project is ScreenSpaceOverlay. A world-space MeshRenderer
/// draws BEHIND the whole UI there, silently — the same reason ParticleSystem was
/// unusable for the card-burn VFX. Rendering through a CanvasRenderer is the only
/// way the beam appears above the board. Nothing the design depends on is lost:
/// it is still ONE mesh in ONE draw call, the flow remains continuous across
/// the branch so a single pulse splits at the point, and the shader still does
/// the entire cross-section. Two things did have to change:
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
    /// <summary>Master alpha for the v8 ribbon passes. At one, the shader uses
    /// the reference's native 0.115 / 0.26 / 0.48 pass opacities.</summary>
    public float intensity = 1f;

    [Header("Timing (seconds)")]
    public float materializeTime = 0.13f;
    public float collapseTime    = 0.15f;

    [Header("State colours")]
    public Ramp aim;
    public Ramp valid;
    public Ramp invalid;

    // ------------------------------------------------------------- constants
    const int   DENSE     = 160;   // dense arc-length lookup for the strongly bowed quadratic
    const int   FIELD_SPINE_POINTS = 33;
    const float ARM_SPAN  = 0.16f; // flow parameter length of each arm

    // ----------------------------------------------------------------- state
    readonly Vector2[] dense = new Vector2[DENSE + 1];
    readonly float[]   cum   = new float[DENSE + 1];
    readonly Vector4[] fieldSpine = new Vector4[FIELD_SPINE_POINTS];

    // One carrier quad contains one analytic field for shaft, point, and barbs.
    // There are no overlapping shaft/head triangles and therefore no seam.
    readonly Vector2[] outVerts = new Vector2[4];
    bool hasGeometry;
    Vector2 headAxisA;
    Vector2 headAxisB;
    float headLengthLocal;
    float headMaxHalfLocal;
    float headLengthPixels;
    float pixelToLocal;

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
    static readonly int ID_SPINE       = Shader.PropertyToID("_Spine");
    static readonly int ID_HEAD_AXIS_A = Shader.PropertyToID("_HeadAxisA");
    static readonly int ID_HEAD_AXIS_B = Shader.PropertyToID("_HeadAxisB");
    static readonly int ID_HEAD_LENGTH_LOCAL = Shader.PropertyToID("_HeadLengthLocal");
    static readonly int ID_HEAD_MAX_HALF = Shader.PropertyToID("_HeadMaxHalfLocal");
    static readonly int ID_HEAD_LENGTH = Shader.PropertyToID("_HeadLengthPx");
    static readonly int ID_PIXEL_TO_LOCAL = Shader.PropertyToID("_PixelToLocal");

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

        aim.fringe     = New32(  8,  30, 116);  // #081E74
        aim.hue        = New32( 26,  86, 238);  // #1A56EE
        aim.light      = New32( 82, 146, 250);  // #5292FA
        aim.core       = New32(178, 212, 255);  // #B2D4FF

        valid.fringe   = New32(  4,  92,  40);  // #045C28
        valid.hue      = New32( 14, 210,  88);  // #0ED258
        valid.light    = New32( 76, 238, 132);  // #4CEE84
        valid.core     = New32(196, 255, 216);  // #C4FFD8

        invalid.fringe = New32(132,   8,  26);  // #84081A
        invalid.hue    = New32(236,  32,  48);  // #EC2030
        invalid.light  = New32(255,  92,  92);  // #FF5C5C
        invalid.core   = New32(255, 202, 196);  // #FFCAC4
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

            // Exact v8 spring. ColorMask RGB in the beam shader prevents this
            // intentional positional weight from contaminating the canvas alpha.
            float step = Mathf.Min(dt, 0.05f);
            float stiff = 340f - weight * 230f;
            float damp = 2f * Mathf.Sqrt(stiff) * 0.82f;
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
        mat.SetVectorArray(ID_SPINE, fieldSpine);
        mat.SetVector(ID_HEAD_AXIS_A, headAxisA);
        mat.SetVector(ID_HEAD_AXIS_B, headAxisB);
        mat.SetFloat(ID_HEAD_LENGTH_LOCAL, headLengthLocal);
        mat.SetFloat(ID_HEAD_MAX_HALF, headMaxHalfLocal);
        mat.SetFloat(ID_HEAD_LENGTH, headLengthPixels);
        mat.SetFloat(ID_PIXEL_TO_LOCAL, pixelToLocal);
        SetVerticesDirty();     // the tip spring and collapse animation move the mesh
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
            // v8 uses 0.50. The requested half-speed pulse is therefore 0.25.
            float headPos = Mathf.Repeat(t * 0.25f + i * 0.5f, 1f) * (1f + ARM_SPAN + 0.30f) - 0.18f;
            float d   = u - headPos;
            float sig = d > 0f ? 0.055f : 0.20f;
            float x   = d / sig;
            v += Mathf.Exp(-x * x) * (i == 0 ? 1f : 0.5f);
        }
        return Mathf.Min(v, 1.05f);
    }

    // ------------------------------------------------------------ mesh build
    /// <summary>
    /// Builds a single analytic distance field. Each sample stores local x/y,
    /// the visible core half-width, and continuous flow. The carrier itself is
    /// one padded quad; the shader unions the sampled curve and both barbs before
    /// shading, so there is no shaft/head boundary to clip or double.
    /// </summary>
    bool Rebuild(Vector2 p0, Vector2 tipPos)
    {
        SampleSpine(p0, tipPos);
        float L = cum[DENSE];
        if (L < coreHalfWidth * 0.5f) return false;

        float canvasMin = Mathf.Min(
            Mathf.Abs(rectTransform.rect.width),
            Mathf.Abs(rectTransform.rect.height));
        float unitScale = canvasMin > 1f ? canvasMin * 0.0105f : coreHalfWidth;
        float scale = unitScale * (0.45f + width * 1.1f);
        float thicknessScale = scale * 1.08f;
        float armLen = Mathf.Min(L * 0.34f, scale * (2.6f + head * 5.2f));
        float taperLength = armLen * 0.55f;
        float canvasScale = canvas != null ? Mathf.Max(0.001f, canvas.scaleFactor) : 1f;
        pixelToLocal = 1f / canvasScale;

        Vector2 boundsMin = new Vector2(float.PositiveInfinity, float.PositiveInfinity);
        Vector2 boundsMax = new Vector2(float.NegativeInfinity, float.NegativeInfinity);
        for (int i = 0; i < FIELD_SPINE_POINTS; i++)
        {
            float s = (float)i / (FIELD_SPINE_POINTS - 1) * L;
            Vector2 p = PointAt(s, L);
            float endEnvelope = Mathf.Pow(
                Mathf.Clamp01((L - s)
                              / Mathf.Max(0.001f, taperLength)), 0.55f);
            float half = WidthAtS(s, L) * thicknessScale * endEnvelope;
            fieldSpine[i] = new Vector4(p.x, p.y, half, s / L);
            boundsMin = Vector2.Min(boundsMin, p);
            boundsMax = Vector2.Max(boundsMax, p);
        }

        Vector2 tipP = dense[DENSE];
        Vector2 tipDir = (tipP - PointAt(Mathf.Max(0f, L - 8f), L)).normalized;
        float spread = 0.42f + sweep * 0.52f;
        float wMax = thicknessScale * 0.55f * 1.85f;
        headLengthLocal = armLen;
        headMaxHalfLocal = wMax;
        headLengthPixels = armLen * canvasScale;

        headAxisA = Rotate(-tipDir,  spread);
        headAxisB = Rotate(-tipDir, -spread);
        Vector2 armEndA = tipP + headAxisA * armLen;
        Vector2 armEndB = tipP + headAxisB * armLen;
        boundsMin = Vector2.Min(boundsMin, Vector2.Min(armEndA, armEndB));
        boundsMax = Vector2.Max(boundsMax, Vector2.Max(armEndA, armEndB));

        // v8's widest pass is 3.4x. Add surge room around every structural edge.
        float pad = Mathf.Max(
            thicknessScale * 3.4f * 1.10f, 7f * pixelToLocal);
        boundsMin -= Vector2.one * pad;
        boundsMax += Vector2.one * pad;
        outVerts[0] = new Vector2(boundsMin.x, boundsMin.y);
        outVerts[1] = new Vector2(boundsMin.x, boundsMax.y);
        outVerts[2] = new Vector2(boundsMax.x, boundsMin.y);
        outVerts[3] = new Vector2(boundsMax.x, boundsMax.y);

        hasGeometry = true;
        return true;
    }

    static Vector2 Rotate(Vector2 v, float radians)
    {
        float c = Mathf.Cos(radians), s = Mathf.Sin(radians);
        return new Vector2(v.x * c - v.y * s, v.x * s + v.y * c);
    }

    void SampleSpine(Vector2 p0, Vector2 p3)
    {
        float dx = p3.x - p0.x;
        Vector2 p1 = (p0 + p3) * 0.5f;
        p1.x += dx * 0.62f * splay;
        // Browser canvas Y grows downward; uGUI Y grows upward, so the artifact's
        // positive sag becomes a negative local-space offset here.
        p1.y -= Mathf.Abs(dx) * 0.09f * splay;
        for (int i = 0; i <= DENSE; i++)
        {
            float t = (float)i / DENSE, u = 1f - t;
            dense[i] = u * u * p0 + 2f * u * t * p1 + t * t * p3;
        }
        cum[0] = 0f;
        for (int i = 1; i <= DENSE; i++)
            cum[i] = cum[i - 1] + Vector2.Distance(dense[i], dense[i - 1]);
    }

    float WidthAtS(float s, float L)
    {
        return 0.32f + 0.68f
            * Mathf.Pow(Mathf.Min(s / Mathf.Max(0.001f, L * 0.55f), 1f), 0.8f);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (!hasGeometry) return;
        var vert = UIVertex.simpleVert;
        vert.color = Color.white;
        for (int i = 0; i < outVerts.Length; i++)
        {
            vert.position = outVerts[i];
            vert.uv0 = new Vector2((i & 2) != 0 ? 1f : 0f,
                                   (i & 1) != 0 ? 1f : 0f);
            // SCREEN pixels, not canvas units. The CanvasScaler is ScaleWithScreenSize against a
            // 1600x900 reference, so a canvas unit is not a pixel — feeding the shader canvas units
            // made its pixel floor under-correct by exactly that factor, which is why thin ribbons
            // still aliased after the floor was added.
            vh.AddVert(vert);
        }

        EmitQuad(vh, 0);
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

    static float EaseOutQuint(float t) { float u = 1f - t; return 1f - u * u * u * u * u; }
}
