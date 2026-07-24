using UnityEngine;

/// <summary>
/// Persistent settings for the custom in-game cursor (the golden spearhead). Same "optcg.xxx"
/// PlayerPrefs convention as <see cref="DisplaySettings"/> and the audio options.
///
/// Size is a pixel edge length; the source art is scaled to fit a Size×Size square. Rotation lets
/// the spear be aligned to point the way a normal OS cursor does — it's exposed in Settings as
/// live ◄/► nudges because the exact angle is a visual judgement (the default is a best guess).
/// </summary>
public static class CursorSettings
{
    private const string KeySize  = "optcg.cursor.size";
    private const string KeyRot   = "optcg.cursor.rot";
    private const string KeyColor = "optcg.cursor.color";

    // Selectable cursor art (the ported metal variants). Each maps to a Resources/*.png (readable +
    // uncompressed .meta). "Gold" reuses the original cursor_pointer.png so existing installs are unchanged.
    public static readonly (string name, string resource)[] Colors =
    {
        ("Gold",      "cursor_pointer"),
        ("Bronze",    "cursor_bronze"),
        ("Silver",    "cursor_silver"),
        ("Gunmetal",  "cursor_gunmetal"),
        ("Platinum",  "cursor_platinum"),
        ("Rose Gold", "cursor_rose_gold"),
    };

    public static int ColorIndex
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(KeyColor, 0), 0, Colors.Length - 1);
        set
        {
            PlayerPrefs.SetInt(KeyColor, ((value % Colors.Length) + Colors.Length) % Colors.Length);
            PlayerPrefs.Save();
        }
    }

    public static string ColorName => Colors[ColorIndex].name;
    public static string ColorResource => Colors[ColorIndex].resource;
    public static void CycleColor() => ColorIndex = ColorIndex + 1;

    public const int MinSize = 16;
    public const int MaxSize = 96;
    public const int DefaultSize = 33;

    // Positive = counter-clockwise. The source art points up-left along the full diagonal (~45°);
    // a normal cursor is steeper, so the default rotates it clockwise a touch. Tuned in-game.
    public const float DefaultRotation = -13f;
    private const float RotStep = 5f;

    public static int SizePx
    {
        get => Mathf.Clamp(PlayerPrefs.GetInt(KeySize, DefaultSize), MinSize, MaxSize);
        set
        {
            PlayerPrefs.SetInt(KeySize, Mathf.Clamp(value, MinSize, MaxSize));
            PlayerPrefs.Save();
        }
    }

    public static float RotationDegrees
    {
        get => PlayerPrefs.GetFloat(KeyRot, DefaultRotation);
        set
        {
            // Keep it in a friendly range so repeated nudges wrap instead of drifting to huge numbers.
            float v = Mathf.Repeat(value + 180f, 360f) - 180f;
            PlayerPrefs.SetFloat(KeyRot, v);
            PlayerPrefs.Save();
        }
    }

    public static void NudgeRotation(int steps) => RotationDegrees += steps * RotStep;
}
