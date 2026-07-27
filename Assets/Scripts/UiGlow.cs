// Shared soft-glow sprite + additive material for uGUI particle effects.
//
// Lifted out of CardEmbers so the burn embers use the exact same round falloff and the
// exact same material instance — one draw-call batch for every glow particle on screen,
// and one place to change how a glow reads.
using UnityEngine;

public static class UiGlow
{
    private static Sprite _sprite;
    private static Material _additive;

    /// <summary>A 64px soft round glow with an (1-d)^3 falloff, white so it can be tinted.</summary>
    public static Sprite Sprite { get { Ensure(); return _sprite; } }

    /// <summary>UI/CardEmberAdditive (Blend One One), falling back to UI/Default.</summary>
    public static Material Additive { get { Ensure(); return _additive; } }

    private static void Ensure()
    {
        if (_sprite == null)
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, false) { wrapMode = TextureWrapMode.Clamp };
            var px = new Color32[s * s];
            float c = (s - 1) * 0.5f;
            for (int y = 0; y < s; y++)
                for (int x = 0; x < s; x++)
                {
                    float d = Mathf.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
                    float a = Mathf.Clamp01(1f - d);
                    a = a * a * a;                                  // soft round falloff
                    px[y * s + x] = new Color(1f, 1f, 1f, a);
                }
            tex.SetPixels32(px);
            tex.Apply();
            _sprite = Sprite.Create(tex, new Rect(0, 0, s, s), new Vector2(0.5f, 0.5f), 100f);
        }
        if (_additive == null)
        {
            var sh = Shader.Find("UI/CardEmberAdditive");
            _additive = new Material(sh != null ? sh : Shader.Find("UI/Default"));
        }
    }
}
