using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Centralized display/resolution settings, persisted in PlayerPrefs (same "optcg.xxx" convention as the audio
/// options). The offered resolutions are 16:9 because the UI is authored for that aspect (the CanvasScaler on
/// every Canvas is ScaleWithScreenSize, so within 16:9 the whole UI scales cleanly to any of these). Windowed
/// resizing to arbitrary shapes still works — the managers re-render on a settled resize — but the picker keeps
/// the aspect consistent.
/// </summary>
public static class DisplaySettings
{
    private const string KeyFullscreen = "optcg.display.fullscreen";
    private const string KeyResW = "optcg.display.resW";
    private const string KeyResH = "optcg.display.resH";

    // Common 16:9 modes, smallest → largest.
    private static readonly (int w, int h)[] Sixteen9 =
    {
        (1280, 720), (1366, 768), (1600, 900), (1920, 1080), (2560, 1440), (3840, 2160),
    };

    /// <summary>The 16:9 resolutions that fit the current display (always at least one).</summary>
    public static List<(int w, int h)> Available()
    {
        int maxW = Mathf.Max(Screen.currentResolution.width, 1280);
        int maxH = Mathf.Max(Screen.currentResolution.height, 720);
        var list = new List<(int w, int h)>();
        foreach (var r in Sixteen9)
            if (r.w <= maxW && r.h <= maxH) list.Add(r);
        if (list.Count == 0) list.Add((1280, 720));
        return list;
    }

    public static bool Fullscreen =>
        PlayerPrefs.GetInt(KeyFullscreen, Screen.fullScreenMode != FullScreenMode.Windowed ? 1 : 0) != 0;

    private static FullScreenMode Mode(bool fullscreen) =>
        fullscreen ? FullScreenMode.FullScreenWindow : FullScreenMode.Windowed;

    /// <summary>The saved target resolution (falls back to the live window size if none saved yet).</summary>
    public static (int w, int h) Current()
    {
        int w = PlayerPrefs.GetInt(KeyResW, 0);
        int h = PlayerPrefs.GetInt(KeyResH, 0);
        return (w > 0 && h > 0) ? (w, h) : (Screen.width, Screen.height);
    }

    public static int CurrentIndex()
    {
        var (w, h) = Current();
        var list = Available();
        int best = 0; long bestDelta = long.MaxValue;
        for (int i = 0; i < list.Count; i++)
        {
            long d = System.Math.Abs((long)list[i].w - w) + System.Math.Abs((long)list[i].h - h);
            if (d < bestDelta) { bestDelta = d; best = i; }
        }
        return best;
    }

    public static void ApplyResolution(int w, int h)
    {
        PlayerPrefs.SetInt(KeyResW, w);
        PlayerPrefs.SetInt(KeyResH, h);
        PlayerPrefs.Save();
        Screen.SetResolution(w, h, Mode(Fullscreen));
    }

    public static void ApplyMode(bool fullscreen)
    {
        PlayerPrefs.SetInt(KeyFullscreen, fullscreen ? 1 : 0);
        PlayerPrefs.Save();
        var (w, h) = Current();
        Screen.SetResolution(w, h, Mode(fullscreen));
    }

    /// <summary>Re-apply the saved display mode + resolution at launch. Safe to call unconditionally.</summary>
    public static void RestoreSaved()
    {
        var mode = Mode(Fullscreen);
        int w = PlayerPrefs.GetInt(KeyResW, 0);
        int h = PlayerPrefs.GetInt(KeyResH, 0);
        if (w > 0 && h > 0) Screen.SetResolution(w, h, mode);
        else Screen.fullScreenMode = mode;
    }
}
