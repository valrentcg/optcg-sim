// CardEmbers — real per-particle falling embers for a holo card, built from lightweight
// UGUI Image quads (a soft additive glow sprite) so they integrate with the Canvas, clip
// to the card via RectMask2D, and look like the gallery's falling embers (independent
// particles with sway, flicker, varied size) — which a fragment shader can't do well.
using UnityEngine;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class CardEmbers : MonoBehaviour
{
    static Sprite _glow;
    static Material _add;

    RectTransform area;
    Image[] imgs;
    float[] fx, fy, vy, sway, phase, size, hue, tw;
    int N;

    public void Init(RectTransform clipRoot, int count = 22)
    {
        N = count;
        EnsureAssets();

        var go = new GameObject("Embers", typeof(RectTransform));
        area = go.GetComponent<RectTransform>();
        area.SetParent(clipRoot, false);
        area.anchorMin = Vector2.zero; area.anchorMax = Vector2.one;
        area.offsetMin = Vector2.zero; area.offsetMax = Vector2.zero;
        area.localScale = Vector3.one;
        go.AddComponent<RectMask2D>();        // clip embers to the card rect
        area.SetAsLastSibling();              // render above the card art

        imgs  = new Image[N];
        fx = new float[N]; fy = new float[N]; vy = new float[N]; sway = new float[N];
        phase = new float[N]; size = new float[N]; hue = new float[N]; tw = new float[N];

        for (int i = 0; i < N; i++)
        {
            var e = new GameObject("ember", typeof(RectTransform));
            var rt = e.GetComponent<RectTransform>();
            rt.SetParent(area, false);
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            var im = e.AddComponent<Image>();
            im.sprite = _glow;
            im.material = _add;
            im.raycastTarget = false;
            imgs[i] = im;
            Spawn(i, true);
        }
    }

    void Spawn(int i, bool anywhere)
    {
        fx[i]    = Random.Range(-0.5f, 0.5f);
        fy[i]    = anywhere ? Random.Range(-0.5f, 0.6f) : Random.Range(0.5f, 0.65f); // start at/above top
        vy[i]    = Random.Range(0.09f, 0.26f);     // fraction of card height per second (slow)
        sway[i]  = Random.Range(0.012f, 0.05f);
        phase[i] = Random.Range(0f, 6.2831f);
        size[i]  = Random.Range(7f, 16f);
        hue[i]   = Random.Range(14f, 42f) / 360f;  // warm orange-red
        tw[i]    = Random.Range(1.0f, 3.0f);
    }

    void Update()
    {
        if (area == null) return;
        Vector2 s = area.rect.size;
        float W = s.x, H = s.y;
        if (W < 2f || H < 2f) return;
        float dt = Time.deltaTime, t = Time.time;

        for (int i = 0; i < N; i++)
        {
            fy[i] -= vy[i] * dt;                                   // fall
            if (fy[i] < -0.65f) Spawn(i, false);
            float x = fx[i] + Mathf.Sin(t * tw[i] + phase[i]) * sway[i];
            float flick = 0.55f + 0.45f * Mathf.Sin(t * tw[i] * 2f + phase[i]);
            float edge = Mathf.Clamp01((0.65f - Mathf.Abs(fy[i])) / 0.18f); // fade in/out at edges

            var rt = imgs[i].rectTransform;
            rt.anchoredPosition = new Vector2(x * W, fy[i] * H);
            rt.sizeDelta = new Vector2(size[i], size[i]);

            Color c = Color.HSVToRGB(hue[i], 0.85f, 1f);
            c.a = Mathf.Clamp01(flick) * edge * 0.9f;
            imgs[i].color = c;
        }
    }

    // ---- shared soft-glow sprite + additive material ----
    static void EnsureAssets()
    {
        if (_glow == null)
        {
            int s = 64; var tex = new Texture2D(s, s, TextureFormat.RGBA32, false);
            tex.wrapMode = TextureWrapMode.Clamp;
            var px = new Color32[s * s]; float c = (s - 1) * 0.5f;
            for (int y = 0; y < s; y++)
              for (int x = 0; x < s; x++)
              {
                  float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                  float a = Mathf.Clamp01(1f - d); a = a * a * a;           // soft round falloff
                  px[y * s + x] = new Color(1f, 1f, 1f, a);
              }
            tex.SetPixels32(px); tex.Apply();
            _glow = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        }
        if (_add == null)
        {
            var sh = Shader.Find("UI/CardEmberAdditive");
            _add = new Material(sh != null ? sh : Shader.Find("UI/Default"));
        }
    }
}
