using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// A mesh-native, layered targeting arrow for an overlay uGUI canvas.
///
/// The shaft and both head barbs are real tapered ribbon geometry. All three
/// paths terminate at the exact same apex coordinate in every luminous layer.
/// Consequently the core, body and bloom share one silhouette: there is no
/// separate head sprite, endpoint cap, distance-field mask or carrier quad that
/// can reveal a gap, flat cutoff, zig-zag, or dark redraw trail.
/// </summary>
[RequireComponent(typeof(CanvasRenderer))]
public class TargetingArrowGraphic : MaskableGraphic
{
    public enum ArrowState { Aim, Valid, Invalid }

    [System.Serializable]
    public struct Ramp
    {
        public Color fringe;
        public Color hue;
        public Color light;
        public Color core;

        public static Ramp Lerp(Ramp a, Ramp b, float t)
        {
            return new Ramp
            {
                fringe = Color.Lerp(a.fringe, b.fringe, t),
                hue = Color.Lerp(a.hue, b.hue, t),
                light = Color.Lerp(a.light, b.light, t),
                core = Color.Lerp(a.core, b.core, t)
            };
        }
    }

    [Header("Shape")]
    [Range(0f, 1f)] public float splay = 0.55f;
    [Range(0f, 1f)] public float weight = 0.46f;
    [Range(0f, 1f)] public float width = 0.52f;
    [Range(0f, 1f)] public float head = 0.45f;
    [Range(0f, 1f)] public float sweep = 0.42f;
    public float coreHalfWidth = 6.5f;

    [Header("Look")]
    [Range(0f, 1f)] public float bloom = 0.52f;
    [Range(0f, 1f)] public float surge = 0.55f;
    [Range(0f, 1f)] public float shift = 0.45f;
    public float intensity = 1f;

    [Header("Eclipse")]
    [Tooltip("0 = glowing rod (original), 1 = dark body with a corona rim.")]
    [Range(0f, 1f)] public float eclipse = 1f;
    [Range(0.5f, 0.98f)] public float rimPosition = 0.86f;
    [Range(0.02f, 0.40f)] public float rimWidth = 0.11f;

    [Header("Head proportions")]
    [Tooltip("Barb length multiplier. Eclipse outlines every ribbon, and the barbs taper "
           + "to zero at BOTH ends, so a short fat barb outlines into a closed loop "
           + "instead of a clean V. Longer and thinner reads as a chevron.")]
    [Range(0.4f, 3f)] public float headLengthScale = 1.7f;
    [Tooltip("Barb ribbon thickness relative to the shaft. Lower is thinner.")]
    [Range(0.15f, 1.5f)] public float headThickness = 0.52f;

    [Header("Timing (seconds)")]
    public float materializeTime = 0.13f;
    public float collapseTime = 0.15f;

    [Header("State colours")]
    public Ramp aim;
    public Ramp valid;
    public Ramp invalid;

    const int DENSE = 160;
    const int SHAFT_POINTS = 35;
    const int ARM_POINTS = 13;
    const float ARM_SPAN = 0.16f;

    readonly Vector2[] dense = new Vector2[DENSE + 1];
    readonly float[] cum = new float[DENSE + 1];
    readonly Vector2[] shaftPoints = new Vector2[SHAFT_POINTS];
    readonly float[] shaftWidths = new float[SHAFT_POINTS];
    readonly float[] shaftFlow = new float[SHAFT_POINTS];
    readonly Vector2[] armA = new Vector2[ARM_POINTS];
    readonly Vector2[] armB = new Vector2[ARM_POINTS];
    readonly float[] armWidths = new float[ARM_POINTS];
    readonly float[] armFlow = new float[ARM_POINTS];
    readonly List<UIVertex> meshVertices = new List<UIVertex>(600);
    readonly List<int> meshIndices = new List<int>(900);

    Vector2 origin, aimPoint, tip, vel;
    bool active;
    bool hasGeometry;
    float grow;
    float collapseT = -1f;
    Vector2 collapseOrigin, collapseTip;
    ArrowState state = ArrowState.Aim;
    Ramp cur;
    Material mat;

    static readonly int ID_FRINGE = Shader.PropertyToID("_Fringe");
    static readonly int ID_HUE = Shader.PropertyToID("_Hue");
    static readonly int ID_LIGHT = Shader.PropertyToID("_Light");
    static readonly int ID_CORE = Shader.PropertyToID("_CoreCol");
    static readonly int ID_INT = Shader.PropertyToID("_Intensity");
    static readonly int ID_BLOOM = Shader.PropertyToID("_Bloom");
    static readonly int ID_SURGE = Shader.PropertyToID("_Surge");
    static readonly int ID_FROM = Shader.PropertyToID("_From");
    static readonly int ID_FADE = Shader.PropertyToID("_Fade");
    static readonly int ID_ECLIPSE = Shader.PropertyToID("_Eclipse");
    static readonly int ID_RIM_POS = Shader.PropertyToID("_RimPos");
    static readonly int ID_RIM_WIDTH = Shader.PropertyToID("_RimWidth");

    public override Texture mainTexture => s_WhiteTexture;
    public bool IsShowing => active || collapseT >= 0f;

    public void Begin(Vector2 screenOrigin)
    {
        origin = ToLocal(screenOrigin);
        aimPoint = origin + Vector2.up * coreHalfWidth;
        tip = aimPoint;
        vel = Vector2.zero;
        grow = 0f;
        active = true;
        collapseT = -1f;
        enabled = true;
        canvasRenderer.cull = false;
    }

    public void Aim(Vector2 screenPoint, ArrowState newState)
    {
        aimPoint = ToLocal(screenPoint);
        state = newState;
    }

    public void Track(Vector2 screenOrigin, Vector2 screenTip, ArrowState newState)
    {
        if (!active)
        {
            Begin(screenOrigin);
            tip = ToLocal(screenTip);
        }
        origin = ToLocal(screenOrigin);
        Aim(screenTip, newState);
    }

    public void End()
    {
        if (!active) return;
        active = false;
        collapseOrigin = origin;
        collapseTip = tip;
        collapseT = 0f;
    }

    public void Clear()
    {
        active = false;
        collapseT = -1f;
        hasGeometry = false;
        meshVertices.Clear();
        meshIndices.Clear();
        canvasRenderer.cull = true;
        SetVerticesDirty();
    }

    protected override void Awake()
    {
        base.Awake();
        raycastTarget = false;
        if (canvas != null)
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
        ApplyDefaultRampsIfUnset();
        cur = aim;
        Shader shader = Shader.Find("Spellbind/ArrowBeam");
        if (shader == null)
        {
            // See the note in TargetingArrowEclipse.Awake: runtime-only Shader.Find means build
            // stripping removes this unless it is in Always Included Shaders, and the failure is
            // invisible outside a player build.
            Debug.LogError("[TargetingArrow] Shader 'Spellbind/ArrowBeam' not found - ALL targeting "
                         + "arrows are disabled. Add Assets/ArrowBeam.shader to Project Settings -> "
                         + "Graphics -> Always Included Shaders (it is only ever loaded via Shader.Find).");
            enabled = false;
            return;
        }
        mat = new Material(shader) { hideFlags = HideFlags.DontSave };
        material = mat;
        canvasRenderer.cull = true;
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (mat == null) return;
        if (Application.isPlaying) Destroy(mat);
        else DestroyImmediate(mat);
    }

    void ApplyDefaultRampsIfUnset()
    {
        if (aim.core.a > 0f) return;

        // Darker blue now has the same visual authority as the red and green.
        aim.fringe = New32(5, 18, 86);
        aim.hue = New32(20, 70, 224);
        aim.light = New32(72, 132, 248);
        aim.core = New32(178, 212, 255);

        valid.fringe = New32(4, 92, 40);
        valid.hue = New32(14, 210, 88);
        valid.light = New32(76, 238, 132);
        valid.core = New32(196, 255, 216);

        invalid.fringe = New32(132, 8, 26);
        invalid.hue = New32(236, 32, 48);
        invalid.light = New32(255, 92, 92);
        invalid.core = New32(255, 202, 196);
    }

    static Color New32(int r, int g, int b) =>
        new Color32((byte)r, (byte)g, (byte)b, 255);

    void LateUpdate()
    {
        if (mat == null) return;
        float dt = Time.unscaledDeltaTime;
        FadeColour(dt);

        if (active)
        {
            grow = Mathf.Min(1f, grow + dt / Mathf.Max(0.01f, materializeTime));
            float step = Mathf.Min(dt, 0.05f);
            float stiff = 340f - weight * 230f;
            float damp = 2f * Mathf.Sqrt(stiff) * 0.82f;
            Vector2 acceleration = (aimPoint - tip) * stiff - vel * damp;
            vel += acceleration * step;
            tip += vel * step;

            if (Rebuild(origin, tip))
                Push(1f - EaseOutQuint(grow), 1f);
            else
                ClearGeometryOnly();
        }
        else if (collapseT >= 0f)
        {
            collapseT += dt;
            float k = collapseT / Mathf.Max(0.01f, collapseTime);
            if (k >= 1f)
            {
                Clear();
                return;
            }
            if (Rebuild(collapseOrigin, collapseTip))
                Push(EaseOutQuint(k) * 0.96f, (1f - k) * (1f - k));
        }
    }

    void ClearGeometryOnly()
    {
        hasGeometry = false;
        canvasRenderer.cull = true;
        SetVerticesDirty();
    }

    void Push(float from, float fade)
    {
        canvasRenderer.cull = false;
        mat.SetColor(ID_FRINGE, cur.fringe);
        mat.SetColor(ID_HUE, cur.hue);
        mat.SetColor(ID_LIGHT, cur.light);
        mat.SetColor(ID_CORE, cur.core);
        mat.SetFloat(ID_INT, intensity);
        mat.SetFloat(ID_BLOOM, bloom);
        mat.SetFloat(ID_SURGE, surge);
        mat.SetFloat(ID_FROM, from);
        mat.SetFloat(ID_FADE, fade);
        mat.SetFloat(ID_ECLIPSE, eclipse);
        mat.SetFloat(ID_RIM_POS, rimPosition);
        mat.SetFloat(ID_RIM_WIDTH, rimWidth);
        SetVerticesDirty();
    }

    void FadeColour(float dt)
    {
        Ramp target = state == ArrowState.Valid ? valid
                    : state == ArrowState.Invalid ? invalid
                    : aim;
        cur = Ramp.Lerp(cur, target, Mathf.Min(1f, dt * (6f + shift * 34f)));
    }

    Vector2 ToLocal(Vector2 screenPoint)
    {
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay
            ? canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(
            rectTransform, screenPoint, cam, out Vector2 local);
        return local;
    }

    /// <summary>
    /// Asymmetric Gaussian travelling toward the target. Keep this numerically
    /// identical to SurgePulse in ArrowBeam.shader.
    /// </summary>
    public static float Surge(float u, float t, float amount)
    {
        if (amount <= 0.02f) return 0f;
        float value = 0f;
        for (int i = 0; i < 2; i++)
        {
            float headPos = Mathf.Repeat(t * 0.25f + i * 0.5f, 1f)
                          * (1f + ARM_SPAN + 0.30f) - 0.18f;
            float d = u - headPos;
            float sig = d > 0f ? 0.055f : 0.20f;
            float x = d / sig;
            value += Mathf.Exp(-x * x) * (i == 0 ? 1f : 0.5f);
        }
        return Mathf.Min(value, 1.05f);
    }

    bool Rebuild(Vector2 p0, Vector2 apex)
    {
        // Canvas strips secondary UVs unless explicitly requested. Without
        // TexCoord1 every pass masquerades as the dark outer-halo layer.
        if (canvas != null)
            canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1;
        SampleSpine(p0, apex);
        float length = cum[DENSE];
        if (length < coreHalfWidth * 0.5f) return false;

        float canvasMin = Mathf.Min(
            Mathf.Abs(rectTransform.rect.width),
            Mathf.Abs(rectTransform.rect.height));
        float unitScale = canvasMin > 1f ? canvasMin * 0.0105f : coreHalfWidth;
        float baseHalf = unitScale * (0.45f + width * 1.1f) * 1.18f;
        float armLength = Mathf.Min(length * 0.34f,
            baseHalf * (2.6f + head * 5.2f) * headLengthScale);

        for (int i = 0; i < SHAFT_POINTS; i++)
        {
            float t = (float)i / (SHAFT_POINTS - 1);
            float s = t * length;
            shaftPoints[i] = PointAt(s, length);
            shaftFlow[i] = t;

            float developed = 0.32f + 0.68f
                * Mathf.Pow(Mathf.Min(s / Mathf.Max(0.001f, length * 0.55f), 1f), 0.8f);
            float tailTaper = Mathf.SmoothStep(0.12f, 1f, Mathf.Clamp01(t / 0.10f));
            // The shaft is allowed to stay broad until the head, then it
            // converges continuously to the exact shared point.
            float tipTaper = Mathf.Pow(Mathf.Clamp01(
                (length - s) / Mathf.Max(0.001f, armLength * 0.34f)), 0.62f);
            shaftWidths[i] = baseHalf * developed * tailTaper * tipTaper;
        }
        shaftWidths[SHAFT_POINTS - 1] = 0f;
        shaftPoints[SHAFT_POINTS - 1] = apex;

        Vector2 forward = (apex - PointAt(Mathf.Max(0f, length - 8f), length)).normalized;
        float spread = 0.42f + sweep * 0.52f;
        BuildArm(armA, Rotate(-forward, spread), apex, armLength);
        BuildArm(armB, Rotate(-forward, -spread), apex, armLength);

        float armMaxHalf = baseHalf * 1.05f * headThickness;
        for (int i = 0; i < ARM_POINTS; i++)
        {
            float t = (float)i / (ARM_POINTS - 1); // free end -> shared apex
            armFlow[i] = 1f + (1f - t) * ARM_SPAN;
            float freeTaper = Mathf.Pow(Mathf.Clamp01(t / 0.58f), 0.55f);
            // A deliberately short convergence taper. Unlike the old bloom
            // floor, every layer follows this same geometric taper all the way
            // to zero, so it cannot terminate as a thick flat shelf.
            float apexTaper = Mathf.Pow(Mathf.Clamp01((1f - t) / 0.22f), 0.72f);
            armWidths[i] = armMaxHalf * SmoothMinimum(freeTaper, apexTaper, 0.16f);
        }
        armWidths[0] = 0f;
        armWidths[ARM_POINTS - 1] = 0f;
        armA[ARM_POINTS - 1] = apex;
        armB[ARM_POINTS - 1] = apex;

        meshVertices.Clear();
        meshIndices.Clear();
        // Back-to-front layer order. Each layer contains the shaft and both
        // barbs, and each of those three ribbons uses the same apex vertex.
        EmitLayer(0, 3.4f);
        EmitLayer(1, 1.7f);
        EmitLayer(2, 1.0f);
        hasGeometry = meshVertices.Count > 0;
        return hasGeometry;
    }

    void BuildArm(Vector2[] output, Vector2 endDirection, Vector2 apex, float armLength)
    {
        Vector2 freeEnd = apex + endDirection * armLength;
        // The arms diverge immediately and meet the shaft only at the apex.
        // Making their first section follow the shaft stacked three translucent
        // ribbons into a thick pre-tip shelf. The slight outward bow preserves
        // the elegant V8 chevron without creating a second junction region.
        Vector2 control = Vector2.Lerp(freeEnd, apex, 0.48f);
        for (int i = 0; i < output.Length; i++)
        {
            // Evaluate from free end to apex so the final sample for all three
            // structures is bit-identical rather than merely visually close.
            float t = (float)i / (output.Length - 1);
            float u = 1f - t;
            output[i] = u * u * freeEnd + 2f * u * t * control + t * t * apex;
        }
    }

    void EmitLayer(int layer, float multiplier)
    {
        EmitRibbon(shaftPoints, shaftWidths, shaftFlow, layer, multiplier);
        EmitRibbon(armA, armWidths, armFlow, layer, multiplier);
        EmitRibbon(armB, armWidths, armFlow, layer, multiplier);
    }

    void EmitRibbon(Vector2[] points, float[] halfWidths, float[] flow,
                    int layer, float multiplier)
    {
        int start = meshVertices.Count;
        for (int i = 0; i < points.Length; i++)
        {
            Vector2 tangent;
            if (i == 0) tangent = points[1] - points[0];
            else if (i == points.Length - 1) tangent = points[i] - points[i - 1];
            else tangent = points[i + 1] - points[i - 1];
            tangent.Normalize();
            Vector2 normal = new Vector2(-tangent.y, tangent.x);
            float hw = halfWidths[i] * multiplier;

            AddRibbonVertex(points[i] + normal * hw, -1f, flow[i], layer);
            AddRibbonVertex(points[i] - normal * hw, 1f, flow[i], layer);
        }

        for (int i = 0; i < points.Length - 1; i++)
        {
            int v = start + i * 2;
            meshIndices.Add(v);
            meshIndices.Add(v + 2);
            meshIndices.Add(v + 3);
            meshIndices.Add(v);
            meshIndices.Add(v + 3);
            meshIndices.Add(v + 1);
        }
    }

    void AddRibbonVertex(Vector2 position, float cross, float flow, int layer)
    {
        UIVertex vertex = UIVertex.simpleVert;
        vertex.position = position;
        vertex.color = Color.white;
        vertex.uv0 = new Vector2(cross, flow);
        vertex.uv1 = new Vector2(layer, 0f);
        meshVertices.Add(vertex);
    }

    protected override void OnPopulateMesh(VertexHelper vh)
    {
        vh.Clear();
        if (!hasGeometry) return;
        for (int i = 0; i < meshVertices.Count; i++)
            vh.AddVert(meshVertices[i]);
        for (int i = 0; i < meshIndices.Count; i += 3)
            vh.AddTriangle(meshIndices[i], meshIndices[i + 1], meshIndices[i + 2]);
    }

    void SampleSpine(Vector2 p0, Vector2 p2)
    {
        float dx = p2.x - p0.x;
        Vector2 p1 = (p0 + p2) * 0.5f;
        p1.x += dx * 0.62f * splay;
        p1.y -= Mathf.Abs(dx) * 0.09f * splay;
        for (int i = 0; i <= DENSE; i++)
        {
            float t = (float)i / DENSE;
            float u = 1f - t;
            dense[i] = u * u * p0 + 2f * u * t * p1 + t * t * p2;
        }
        cum[0] = 0f;
        for (int i = 1; i <= DENSE; i++)
            cum[i] = cum[i - 1] + Vector2.Distance(dense[i], dense[i - 1]);
    }

    Vector2 PointAt(float s, float length)
    {
        if (s <= 0f) return dense[0];
        if (s >= length) return dense[DENSE];
        int lo = 0, hi = DENSE;
        while (hi - lo > 1)
        {
            int middle = (lo + hi) >> 1;
            if (cum[middle] <= s) lo = middle;
            else hi = middle;
        }
        float f = (s - cum[lo]) / Mathf.Max(1e-5f, cum[hi] - cum[lo]);
        return Vector2.Lerp(dense[lo], dense[hi], f);
    }

    static float SmoothMinimum(float a, float b, float radius)
    {
        float h = Mathf.Max(radius - Mathf.Abs(a - b), 0f) / radius;
        return Mathf.Min(a, b) - h * h * radius * 0.25f;
    }

    static Vector2 Rotate(Vector2 value, float radians)
    {
        float c = Mathf.Cos(radians);
        float s = Mathf.Sin(radians);
        return new Vector2(value.x * c - value.y * s,
                           value.x * s + value.y * c);
    }

    static float EaseOutQuint(float t)
    {
        float u = 1f - t;
        return 1f - u * u * u * u * u;
    }
}
