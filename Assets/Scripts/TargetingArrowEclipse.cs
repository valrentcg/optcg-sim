using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives <c>Spellbind/ArrowEclipse</c>: a bounds quad plus the two endpoints, so the
/// shader can evaluate the same analytic field the browser prototype used.
///
/// Deliberately NOT a ribbon mesh. The prototype's look is a signed-distance
/// construction — eroded edge, corona rim, tip pinch, runout surge — and expressing it
/// as generated geometry produced something unrelated. This keeps the geometry trivial
/// and the field authoritative, which is the only way the two stay the same image.
///
/// Public API is source-compatible with TargetingArrowGraphic so it can be swapped in
/// without touching call sites.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class TargetingArrowEclipse : MaskableGraphic
{
    public enum ArrowState { Aim, Valid, Invalid }

    [System.Serializable]
    public struct Ramp
    {
        public Color fringe, hue, light, core;
        public static Ramp Lerp(Ramp a, Ramp b, float t) => new Ramp
        {
            fringe = Color.Lerp(a.fringe, b.fringe, t),
            hue = Color.Lerp(a.hue, b.hue, t),
            light = Color.Lerp(a.light, b.light, t),
            core = Color.Lerp(a.core, b.core, t),
        };
    }

    // Defaults are the exported prototype tuning, verbatim.
    [Header("Shape (prototype export)")]
    public float widthPx = 5.5f;
    public float headHalfWidth = 26f;
    public float headLength = 50f;
    [Range(0f, 1f)] public float barbWeight = 0.55f;
    [Range(-1f, 1.6f)] public float barbSweep = -1f;
    [Range(0f, 0.5f)] public float tipSharpness = 0f;
    [Range(0f, 6f)] public float tipPinch = 0f;
    [Tooltip("Bow driven by how far off the board's forward axis the target sits: dead "
           + "ahead is a straight line, and the arrow curves further, and to the matching "
           + "side, as the aim swings out. Negative flips which way it leans.")]
    [Range(-2f, 2f)] public float aimCurvature = 0.7f;
    [Tooltip("Higher = flatter near centre, so a forward aim reads dead straight and the "
           + "bow only builds as the aim swings wide. 1 = linear.")]
    [Range(1f, 4f)] public float curveResponse = 2.0f;
    [Range(0f, 1.2f)] public float fixedCurvature = 0.36f;

    [Header("Look (prototype export)")]
    [Range(0f, 2.2f)] public float edgeErosion = 0.30f;
    [Range(0f, 3f)] public float auraReach = 0.96f;
    [Range(0f, 2f)] public float fineDetail = 0.14f;
    [Range(0f, 2f)] public float progression = 0.72f;
    [Range(0.3f, 2.2f)] public float exposure = 1.14f;

    [Header("Timing (seconds)")]
    public float materializeTime = 0.13f;
    public float collapseTime = 0.15f;
    [Range(0f, 1f)] public float shift = 0.45f;
    [Range(0f, 1f)] public float springWeight = 0.46f;

    [Header("State colours")]
    public Ramp aim, valid, invalid;

    Vector2 origin, aimPoint, tip, vel;
    bool active, hasGeometry;
    float grow, collapseT = -1f;
    Vector2 collapseOrigin, collapseTip;
    ArrowState state = ArrowState.Aim;
    Ramp cur;
    Material mat;
    readonly Vector3[] quad = new Vector3[4];

    static readonly int ID_FRINGE = Shader.PropertyToID("_Fringe");
    static readonly int ID_HUE = Shader.PropertyToID("_Hue");
    static readonly int ID_LIGHT = Shader.PropertyToID("_Light");
    static readonly int ID_CORE = Shader.PropertyToID("_CoreCol");
    static readonly int ID_ENDS = Shader.PropertyToID("_EndA");
    static readonly int ID_WIDTH = Shader.PropertyToID("_Width");
    static readonly int ID_HEADW = Shader.PropertyToID("_HeadW");
    static readonly int ID_HEADL = Shader.PropertyToID("_HeadL");
    static readonly int ID_BARB = Shader.PropertyToID("_Barb");
    static readonly int ID_HEADCRV = Shader.PropertyToID("_HeadCrv");
    static readonly int ID_TIPSHARP = Shader.PropertyToID("_TipSharp");
    static readonly int ID_TIPPINCH = Shader.PropertyToID("_TipPinch");
    static readonly int ID_AUTO = Shader.PropertyToID("_Auto");
    static readonly int ID_CURVE = Shader.PropertyToID("_Curve");
    static readonly int ID_CURVESHAPE = Shader.PropertyToID("_CurveShape");
    static readonly int ID_WISP = Shader.PropertyToID("_Wisp");
    static readonly int ID_AURA = Shader.PropertyToID("_Aura");
    static readonly int ID_DETAIL = Shader.PropertyToID("_Detail");
    static readonly int ID_SURGE = Shader.PropertyToID("_Surge");
    static readonly int ID_EXPO = Shader.PropertyToID("_Expo");
    static readonly int ID_FROM = Shader.PropertyToID("_From");
    static readonly int ID_FADE = Shader.PropertyToID("_Fade");

    public override Texture mainTexture => s_WhiteTexture;
    public bool IsShowing => active || collapseT >= 0f;

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
        ApplyDefaultRampsIfUnset();
        cur = aim;
        Shader sh = Shader.Find("Spellbind/ArrowEclipse");
        if (sh == null)
        {
            // The material is built here at RUNTIME, so no asset ever references this shader and
            // Unity's build-time stripping drops it unless it is listed in Graphics settings ->
            // Always Included Shaders. That fails ONLY in a player build (the Editor always finds
            // it), and it silently killed every targeting arrow in the game in v1.0.30 - drag,
            // hover and the resolved-battle arrow alike - because this path just disables itself.
            // LogError, not LogWarning: this is never a cosmetic degrade, it is total loss of the
            // targeting UI, and it must be impossible to miss in a build log.
            Debug.LogError("[ArrowEclipse] Shader 'Spellbind/ArrowEclipse' not found - ALL targeting "
                         + "arrows are disabled. Add Assets/ArrowEclipse.shader to Project Settings -> "
                         + "Graphics -> Always Included Shaders (it is only ever loaded via Shader.Find).");
            enabled = false;
            return;
        }
        mat = new Material(sh) { hideFlags = HideFlags.DontSave };
        material = mat;
        canvasRenderer.cull = true;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (mat == null) return;
        if (Application.isPlaying) Destroy(mat); else DestroyImmediate(mat);
    }

    void ApplyDefaultRampsIfUnset()
    {
        if (aim.core.a > 0f) return;
        // Violet, matching the prototype's Celestial palette.
        aim.fringe = C(14, 20, 92); aim.hue = C(66, 62, 214);
        aim.light = C(150, 150, 250); aim.core = C(226, 224, 255);

        valid.fringe = C(4, 78, 34); valid.hue = C(22, 196, 96);
        valid.light = C(120, 240, 160); valid.core = C(214, 255, 228);

        invalid.fringe = C(104, 6, 22); invalid.hue = C(226, 40, 52);
        invalid.light = C(255, 118, 116); invalid.core = C(255, 210, 206);
    }
    static Color C(int r, int g, int b) => new Color32((byte)r, (byte)g, (byte)b, 255);

    public void Begin(Vector2 screenOrigin)
    {
        origin = ToLocal(screenOrigin);
        aimPoint = origin + Vector2.up * widthPx;
        tip = aimPoint; vel = Vector2.zero;
        grow = 0f; active = true; collapseT = -1f;
        enabled = true; canvasRenderer.cull = false;
    }

    public void Aim(Vector2 screenPoint, ArrowState newState)
    {
        aimPoint = ToLocal(screenPoint);
        state = newState;
    }

    public void Track(Vector2 screenOrigin, Vector2 screenTip, ArrowState newState)
    {
        if (!active) { Begin(screenOrigin); tip = ToLocal(screenTip); }
        origin = ToLocal(screenOrigin);
        Aim(screenTip, newState);
    }

    public void End()
    {
        if (!active) return;
        active = false; collapseOrigin = origin; collapseTip = tip; collapseT = 0f;
    }

    public void Clear()
    {
        active = false; collapseT = -1f; hasGeometry = false;
        canvasRenderer.cull = true; SetVerticesDirty();
    }

    void LateUpdate()
    {
        if (mat == null) return;
        float dt = Time.unscaledDeltaTime;
        Ramp target = state == ArrowState.Valid ? valid
                    : state == ArrowState.Invalid ? invalid : aim;
        cur = Ramp.Lerp(cur, target, Mathf.Min(1f, dt * (6f + shift * 34f)));

        if (active)
        {
            grow = Mathf.Min(1f, grow + dt / Mathf.Max(0.01f, materializeTime));
            float step = Mathf.Min(dt, 0.05f);
            float stiff = 340f - springWeight * 230f;
            float damp = 2f * Mathf.Sqrt(stiff) * 0.82f;
            vel += ((aimPoint - tip) * stiff - vel * damp) * step;
            tip += vel * step;
            if (Rebuild(origin, tip)) Push(1f - EaseOutQuint(grow), 1f);
            else { hasGeometry = false; canvasRenderer.cull = true; SetVerticesDirty(); }
        }
        else if (collapseT >= 0f)
        {
            collapseT += dt;
            float k = collapseT / Mathf.Max(0.01f, collapseTime);
            if (k >= 1f) { Clear(); return; }
            if (Rebuild(collapseOrigin, collapseTip)) Push(EaseOutQuint(k) * 0.96f, (1f - k) * (1f - k));
        }
    }

    static float EaseOutQuint(float x) { float u = 1f - x; return 1f - u * u * u * u * u; }

    /// <summary>Bounds quad with enough margin for the aura and the eroded edge.</summary>
    bool Rebuild(Vector2 a, Vector2 b)
    {
        if ((b - a).sqrMagnitude < widthPx * widthPx) return false;
        // Margin must cover: head span, aura reach, erosion amplitude and the bow.
        float pad = Mathf.Max(headHalfWidth, headLength)
                  + auraReach * widthPx * 2.4f
                  + edgeErosion * widthPx * 1.15f
                  + Mathf.Max(aimCurvature, fixedCurvature) * Mathf.Min((b - a).magnitude * 0.32f, 200f)
                  + 12f;
        Vector2 lo = Vector2.Min(a, b) - Vector2.one * pad;
        Vector2 hi = Vector2.Max(a, b) + Vector2.one * pad;
        quad[0] = new Vector3(lo.x, lo.y);
        quad[1] = new Vector3(lo.x, hi.y);
        quad[2] = new Vector3(hi.x, hi.y);
        quad[3] = new Vector3(hi.x, lo.y);
        hasGeometry = true;
        return true;
    }

    void Push(float from, float fade)
    {
        canvasRenderer.cull = false;
        mat.SetColor(ID_FRINGE, cur.fringe);
        mat.SetColor(ID_HUE, cur.hue);
        mat.SetColor(ID_LIGHT, cur.light);
        mat.SetColor(ID_CORE, cur.core);
        mat.SetVector(ID_ENDS, new Vector4(origin.x, origin.y, tip.x, tip.y));
        mat.SetFloat(ID_WIDTH, widthPx);
        mat.SetFloat(ID_HEADW, headHalfWidth);
        mat.SetFloat(ID_HEADL, headLength);
        mat.SetFloat(ID_BARB, barbWeight);
        mat.SetFloat(ID_HEADCRV, barbSweep);
        mat.SetFloat(ID_TIPSHARP, tipSharpness);
        mat.SetFloat(ID_TIPPINCH, tipPinch);
        mat.SetFloat(ID_AUTO, aimCurvature);
        mat.SetFloat(ID_CURVE, fixedCurvature);
        mat.SetFloat(ID_CURVESHAPE, curveResponse);
        mat.SetFloat(ID_WISP, edgeErosion);
        mat.SetFloat(ID_AURA, auraReach);
        mat.SetFloat(ID_DETAIL, fineDetail);
        mat.SetFloat(ID_SURGE, progression);
        mat.SetFloat(ID_EXPO, exposure);
        mat.SetFloat(ID_FROM, from);
        mat.SetFloat(ID_FADE, fade);
        SetVerticesDirty();
    }

    Vector2 ToLocal(Vector2 screenPoint)
    {
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform, screenPoint, cam, out Vector2 local);
        return local;
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (!hasGeometry) return;
        UIVertex v = UIVertex.simpleVert;
        v.color = Color.white;
        for (int i = 0; i < 4; i++) { v.position = quad[i]; v.uv0 = Vector2.zero; vh.AddVert(v); }
        vh.AddTriangle(0, 1, 2);
        vh.AddTriangle(2, 3, 0);
    }
}
