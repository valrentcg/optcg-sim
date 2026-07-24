using UnityEngine;

/// <summary>
/// Applies the custom golden-spearhead cursor globally. The source art lives at
/// Resources/cursor_pointer.png (drop the PNG in Assets/Resources/). It is trimmed to the opaque
/// spear, rotated so the tip points the way a normal OS cursor does, and scaled so the spear's
/// longest edge equals the size chosen in Settings, then handed to <see cref="Cursor.SetCursor"/>.
/// The click hotspot sits on the spear tip.
///
/// Design notes (fixing the first pass):
///  - We fit the *un-rotated, trimmed* content, and render into a texture sized to that content's
///    diagonal, so the spear stays a constant on-screen size at every rotation angle.
///  - We supersample with premultiplied-alpha bilinear sampling, so the result is crisp and free of
///    the dark fringe transparent PNG borders otherwise bleed in.
///  - The source is read through a RenderTexture, so it works regardless of the "Read/Write" import
///    flag, and only once (results are cached; only the final rasterise re-runs on size/rotation).
///  - Sizes above the ~32px hardware-cursor cap use ForceSoftware so scaling actually takes effect.
/// </summary>
public static class CursorManager
{
    // Where the tip sits in the source art, normalised (0..1), origin bottom-left. The spearhead's
    // point is at the top-left of the image. Tune if the click point feels off from the visual tip.
    private static readonly Vector2 SourceTipUV = new Vector2(0.05f, 0.95f);

    private const int WorkCap = 1024;   // readback resolution cap (source is 1024²)
    private const int Supersample = 3;  // NxN sub-samples per output pixel — anti-aliasing
    private const int MaxTexSide = 256; // hard cap on the produced cursor texture

    private static Color[] _pixels;      // cached readable copy of the source (straight RGBA)
    private static int _pw, _ph;
    private static RectInt _content;     // opaque bounding box within the readable copy (y-up)
    private static Texture2D _current;   // the live cursor texture (destroyed on rebuild)
    private static string _loadedResource;   // which colour's art is currently cached in _pixels
    private static bool _missingLogged;

    /// <summary>Call once at launch. Also installs a focus watcher so the cursor survives alt-tab.</summary>
    public static void Init()
    {
        Apply();
        CursorRunner.Ensure();
    }

    /// <summary>(Re)build the cursor from the current <see cref="CursorSettings"/> and apply it.</summary>
    public static void Apply()
    {
        string resource = CursorSettings.ColorResource;
        var source = Resources.Load<Texture2D>(resource);
        if (source == null)
        {
            if (!_missingLogged)
            {
                Debug.LogWarning($"[Cursor] No art at Resources/{resource}.png — using system cursor. " +
                                 "Drop the cursor PNG into Assets/Resources/ to enable the custom cursor.");
                _missingLogged = true;
            }
            return;
        }

        // Colour changed → drop the cached pixels so the new art is analysed.
        if (resource != _loadedResource) { _pixels = null; _loadedResource = resource; }
        if (_pixels == null) Analyze(source);

        int size = CursorSettings.SizePx;
        var tex = BuildCursor(size, CursorSettings.RotationDegrees, out Vector2 hotspot);

        // Unity's hardware cursor (Auto) is capped at 32×32, so anything bigger must go through
        // ForceSoftware to actually scale. The software cursor's "moving vertical lines" artifact comes
        // from a non-multiple-of-4 texture width (row-stride mismatch) — fixed in BuildCursor.
        var mode = size <= 32 ? CursorMode.Auto : CursorMode.ForceSoftware;
        Cursor.SetCursor(tex, hotspot, mode);

        if (_current != null) Object.Destroy(_current);
        _current = tex;
    }

    /// <summary>Grab the source's CPU pixels, then find the opaque bounding box.</summary>
    private static void Analyze(Texture2D src)
    {
        if (src.isReadable)
        {
            // Direct read — the true sRGB texels, no colour-space round-trip (keeps the gold gold).
            _pixels = src.GetPixels();
            _pw = src.width; _ph = src.height;
        }
        else
        {
            // Fallback if the import isn't marked Read/Write yet: a RenderTexture read-back works with
            // any import flags, but in a Linear project it can tint the result until the reimport lands.
            int longest = Mathf.Max(src.width, src.height);
            int scaleDown = Mathf.Max(1, Mathf.CeilToInt((float)longest / WorkCap));
            int w = Mathf.Max(1, src.width / scaleDown);
            int h = Mathf.Max(1, src.height / scaleDown);

            var prevActive = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            Graphics.Blit(src, rt);
            RenderTexture.active = rt;
            var readable = new Texture2D(w, h, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            readable.Apply();
            RenderTexture.active = prevActive;
            RenderTexture.ReleaseTemporary(rt);

            _pixels = readable.GetPixels();
            _pw = w; _ph = h;
            Object.Destroy(readable);
            Debug.LogWarning("[Cursor] Source texture isn't Read/Write enabled — using a fallback that may " +
                             "tint colours until Unity reimports Assets/Resources/cursor_pointer.png.");
        }

        // Opaque bounding box (y-up; row 0 is the bottom, matching GetPixels order).
        int minX = _pw, minY = _ph, maxX = -1, maxY = -1;
        for (int y = 0; y < _ph; y++)
        {
            int row = y * _pw;
            for (int x = 0; x < _pw; x++)
            {
                if (_pixels[row + x].a > 0.04f)
                {
                    if (x < minX) minX = x; if (x > maxX) maxX = x;
                    if (y < minY) minY = y; if (y > maxY) maxY = y;
                }
            }
        }
        _content = (maxX < 0) ? new RectInt(0, 0, _pw, _ph)
                              : new RectInt(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    /// <summary>
    /// Rasterise the trimmed spear at <paramref name="size"/> px (longest edge), rotated by
    /// <paramref name="angleDeg"/> (positive = CCW). The output texture is sized to the content's
    /// diagonal so nothing clips and the visual size is constant across angles. Hotspot is returned
    /// in Cursor.SetCursor's top-left / y-down pixels, on the spear tip.
    /// </summary>
    private static Texture2D BuildCursor(int size, float angleDeg, out Vector2 hotspot)
    {
        float contentW = _content.width, contentH = _content.height;
        float cxs = _content.x + contentW / 2f;   // content centre in source px (y-up)
        float cys = _content.y + contentH / 2f;
        float scale = size / Mathf.Max(contentW, contentH);   // output px per source px

        float cw = contentW * scale, ch = contentH * scale;
        // Round the side up to a multiple of 4 — the software cursor mis-renders (thin vertical lines)
        // when the row pitch isn't 4-byte aligned.
        int raw = (Mathf.CeilToInt(Mathf.Sqrt(cw * cw + ch * ch)) + 2 + 3) & ~3;
        int texSide = Mathf.Clamp(raw, 4, MaxTexSide);
        float cxo = texSide / 2f, cyo = texSide / 2f;

        float a = angleDeg * Mathf.Deg2Rad;
        float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
        int ss = Supersample;
        float invN = 1f / (ss * ss);

        var outPx = new Color[texSide * texSide];
        for (int oy = 0; oy < texSide; oy++)
        {
            for (int ox = 0; ox < texSide; ox++)
            {
                float accA = 0f, accR = 0f, accG = 0f, accB = 0f;
                for (int sy = 0; sy < ss; sy++)
                {
                    for (int sx = 0; sx < ss; sx++)
                    {
                        float subX = ox + (sx + 0.5f) / ss;
                        float subY = oy + (sy + 0.5f) / ss;
                        float dx = (subX - cxo) / scale;
                        float dy = (subY - cyo) / scale;
                        // Inverse-rotate output → source (R(-a)).
                        float u = cxs + (dx * ca + dy * sa);
                        float v = cys + (-dx * sa + dy * ca);
                        Color c = Sample(u, v);
                        accA += c.a; accR += c.r * c.a; accG += c.g * c.a; accB += c.b * c.a;
                    }
                }
                float oa = accA * invN;
                outPx[oy * texSide + ox] = accA > 1e-5f
                    ? new Color(accR / accA, accG / accA, accB / accA, oa)
                    : new Color(0, 0, 0, 0);
            }
        }

        // linear:true is deliberate. We already hold the true sRGB gold bytes; flagging the texture
        // sRGB would make Unity apply an extra sRGB decode when it hands the cursor to the OS (in this
        // Linear-color-space project), which is what tinted the gold orange. Linear = pass the bytes through.
        var tex = new Texture2D(texSide, texSide, TextureFormat.RGBA32, false, true)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp,   // no edge-sample wrap → no thin lines at the border
        };
        tex.SetPixels(outPx);
        tex.Apply();

        // Map the tip: source (y-up) → forward rotate R(a) → output px → flip to top-left / y-down.
        float tsx = SourceTipUV.x * _pw, tsy = SourceTipUV.y * _ph;
        float ex = (tsx - cxs) * ca - (tsy - cys) * sa;
        float ey = (tsx - cxs) * sa + (tsy - cys) * ca;
        float tipX = cxo + ex * scale;
        float tipYUp = cyo + ey * scale;
        hotspot = new Vector2(Mathf.Clamp(tipX, 0f, texSide - 1f),
                              Mathf.Clamp(texSide - tipYUp, 0f, texSide - 1f));
        return tex;
    }

    /// <summary>Premultiplied-alpha bilinear sample of the readable source. Off-texture = transparent.</summary>
    private static Color Sample(float x, float y)
    {
        x -= 0.5f; y -= 0.5f;
        int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
        float fx = x - x0, fy = y - y0;

        Color c00 = At(x0,     y0);
        Color c10 = At(x0 + 1, y0);
        Color c01 = At(x0,     y0 + 1);
        Color c11 = At(x0 + 1, y0 + 1);

        float w00 = (1 - fx) * (1 - fy), w10 = fx * (1 - fy), w01 = (1 - fx) * fy, w11 = fx * fy;
        float a = c00.a * w00 + c10.a * w10 + c01.a * w01 + c11.a * w11;
        if (a <= 1e-5f) return new Color(0, 0, 0, 0);
        // Weight colour by alpha so transparent (often black) texels don't darken the edge.
        float r = (c00.r * c00.a * w00 + c10.r * c10.a * w10 + c01.r * c01.a * w01 + c11.r * c11.a * w11) / a;
        float g = (c00.g * c00.a * w00 + c10.g * c10.a * w10 + c01.g * c01.a * w01 + c11.g * c11.a * w11) / a;
        float b = (c00.b * c00.a * w00 + c10.b * c10.a * w10 + c01.b * c01.a * w01 + c11.b * c11.a * w11) / a;
        return new Color(r, g, b, a);
    }

    private static Color At(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _pw || y >= _ph) return new Color(0, 0, 0, 0);
        return _pixels[y * _pw + x];
    }
}

/// <summary>Hidden, persistent helper that re-applies the cursor when the app regains focus.</summary>
public class CursorRunner : MonoBehaviour
{
    private static CursorRunner _instance;

    public static void Ensure()
    {
        if (_instance != null) return;
        var go = new GameObject("~CursorRunner") { hideFlags = HideFlags.HideAndDontSave };
        Object.DontDestroyOnLoad(go);
        _instance = go.AddComponent<CursorRunner>();
    }

    private void OnApplicationFocus(bool focus)
    {
        if (focus) CursorManager.Apply();
    }
}
