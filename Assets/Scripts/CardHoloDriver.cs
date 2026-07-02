// CardHoloDriver — drives the "UI/CardHolo" material on a single card's Art Image.
// Replaces RoundedCardClip for SR/SEC cards: it makes the per-card material instance,
// feeds _Size/_Radius every frame (rounded corners), AND animates _Pointer from the
// cursor while hovered and from the card's own motion while dragged — so the foil
// "waves" exactly when you pick a card up or move it, like the standalone viewer.
//
// Drop this file anywhere under Assets/. It needs no other new types.
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CardHoloDriver : MonoBehaviour
{
    static readonly int SizeID      = Shader.PropertyToID("_Size");
    static readonly int RadiusID    = Shader.PropertyToID("_Radius");
    static readonly int PointerID   = Shader.PropertyToID("_Pointer");
    static readonly int PatternID   = Shader.PropertyToID("_Pattern");
    static readonly int IntensityID = Shader.PropertyToID("_Intensity");
    static readonly int EmbersID    = Shader.PropertyToID("_Embers");
    static readonly int EmberAmtID  = Shader.PropertyToID("_EmberAmt");
    static readonly int FoilTexID   = Shader.PropertyToID("_FoilTex");

    // Shared cracked-ice / prismatic foil pattern: R = per-cell id (-> hue), G = facet edge.
    static Texture2D _foil;
    static Texture2D FoilTex()
    {
        if (_foil != null) return _foil;
        int S = 256, NP = 46;
        var pts = new Vector2[NP];
        var seed = new System.Random(12345);
        for (int i = 0; i < NP; i++) pts[i] = new Vector2((float)seed.NextDouble()*S, (float)seed.NextDouble()*S);
        var px = new Color32[S*S];
        for (int y = 0; y < S; y++)
          for (int x = 0; x < S; x++)
          {
              float d1 = 1e9f, d2 = 1e9f; int c1 = 0;
              for (int i = 0; i < NP; i++)
              {
                  // toroidal distance so the texture tiles seamlessly
                  float dx = Mathf.Abs(x - pts[i].x); dx = Mathf.Min(dx, S - dx);
                  float dy = Mathf.Abs(y - pts[i].y); dy = Mathf.Min(dy, S - dy);
                  float dd = dx*dx + dy*dy;
                  if (dd < d1) { d2 = d1; d1 = dd; c1 = i; }
                  else if (dd < d2) d2 = dd;
              }
              float edge = Mathf.Sqrt(d2) - Mathf.Sqrt(d1);            // small near facet boundaries
              byte cell = (byte)((c1 * 53 + 17) % 256);                // spread cell ids across hues
              byte fac  = (byte)Mathf.RoundToInt(Mathf.Clamp01(1f - edge/5f) * 255f); // bright cracked edges
              px[y*S + x] = new Color32(cell, fac, 0, 255);
          }
        _foil = new Texture2D(S, S, TextureFormat.RGBA32, true) { wrapMode = TextureWrapMode.Repeat };
        _foil.SetPixels32(px); _foil.Apply();
        return _foil;
    }

    Material mat;
    RectTransform rt;
    Canvas canvas;
    float fraction;

    Vector2 pointer = new Vector2(0.5f, 0.5f);   // smoothed, in 0..1 UV
    Vector3 lastWorld;
    bool haveLast;

    /// <param name="pattern">0 = SR (white/iridescent glints), 1 = SEC (gold glints).</param>
    /// <param name="embers">1 = enable falling embers (use for red cards).</param>
    public void Init(Graphic graphic, float radiusFraction, int pattern,
                     float intensity = 0.45f, int embers = 0, float emberAmt = 0.85f)
    {
        rt = graphic.rectTransform;
        mat = Instantiate(graphic.material);   // per-card instance (base = UI/CardHolo)
        graphic.material = mat;
        canvas = graphic.canvas;
        fraction = radiusFraction;

        mat.SetFloat(PatternID, pattern);
        mat.SetFloat(IntensityID, intensity);
        mat.SetFloat(EmbersID, embers);
        mat.SetFloat(EmberAmtID, emberAmt);
        mat.SetTexture(FoilTexID, FoilTex());
        mat.SetVector(PointerID, new Vector4(0.5f, 0.5f, 0f, 0f));
    }

    void Update()
    {
        if (mat == null || rt == null) return;

        // Rounded-corner feed (identical contract to RoundedCardClip)
        Vector2 s = rt.rect.size;
        if (s.x <= 1f || s.y <= 1f) return;
        mat.SetVector(SizeID, new Vector4(s.x, s.y, 0f, 0f));
        mat.SetFloat(RadiusID, Mathf.Min(s.x, s.y) * fraction);

        // ----- Target pointer -----
        Vector2 target = new Vector2(0.5f, 0.5f);

        // (a) cursor over the card -> pointer follows the cursor in UV space
        Camera cam = (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            ? canvas.worldCamera : null;
        Vector2 mouse = GetPointerScreen();
        if (RectTransformUtility.RectangleContainsScreenPoint(rt, mouse, cam))
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(rt, mouse, cam, out var local))
            {
                Rect r = rt.rect;
                target = new Vector2(
                    Mathf.Clamp01((local.x - r.x) / r.width),
                    Mathf.Clamp01((local.y - r.y) / r.height));
            }
        }

        // (b) card motion (being dragged/animated) -> push the sweep so it shimmers while moving
        Vector3 world = rt.position;
        if (haveLast)
        {
            Vector3 d = world - lastWorld;
            float speed = d.magnitude / Mathf.Max(Time.deltaTime, 1e-4f);
            if (speed > 1f)
            {
                Vector2 dir = new Vector2(d.x, d.y).normalized;
                target += dir * Mathf.Clamp01(speed / 4000f) * 0.5f;
            }
        }
        lastWorld = world; haveLast = true;

        // Ease toward target so it feels fluid, not jittery
        pointer = Vector2.Lerp(pointer, target, 1f - Mathf.Exp(-12f * Time.deltaTime));
        mat.SetVector(PointerID, new Vector4(Mathf.Clamp01(pointer.x), Mathf.Clamp01(pointer.y), 0f, 0f));
    }

    // Cursor position, tolerant of legacy Input vs new Input System.
    static Vector2 GetPointerScreen()
    {
#if ENABLE_INPUT_SYSTEM && !ENABLE_LEGACY_INPUT_MANAGER
        var p = UnityEngine.InputSystem.Pointer.current;
        return p != null ? p.position.ReadValue() : Vector2.zero;
#else
        return (Vector2)Input.mousePosition;
#endif
    }

    void OnDestroy()
    {
        if (mat != null) Destroy(mat);
    }
}
