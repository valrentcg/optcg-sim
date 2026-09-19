using System.Collections.Generic;
using System.Runtime.InteropServices;
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

#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int left, top, right, bottom; }

    private const uint SpiGetWorkArea = 0x0030;
    private const int SmCxSizeFrame = 32;
    private const int SmCySizeFrame = 33;
    private const int SmCyCaption = 4;
    private const int SmCxPaddedBorder = 92;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool SystemParametersInfo(uint action, uint param, out NativeRect rect, uint flags);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);
#endif

    // A requested Unity window size is the CLIENT area; Windows then adds the title bar and
    // resize frame. Fit the client to the desktop work area (which already excludes the taskbar)
    // so a 1920x1080 preference never creates an outer window taller than a 1080p desktop.
    private static (int w, int h) FitWindowed(int requestedW, int requestedH)
    {
        int maxClientW = Mathf.Max(640, Screen.currentResolution.width);
        int maxClientH = Mathf.Max(360, Screen.currentResolution.height - 48);
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
        try
        {
            if (SystemParametersInfo(SpiGetWorkArea, 0, out var work, 0))
            {
                int frameX = Mathf.Max(0, GetSystemMetrics(SmCxSizeFrame)) +
                             Mathf.Max(0, GetSystemMetrics(SmCxPaddedBorder));
                int frameY = Mathf.Max(0, GetSystemMetrics(SmCySizeFrame)) +
                             Mathf.Max(0, GetSystemMetrics(SmCxPaddedBorder));
                int caption = Mathf.Max(0, GetSystemMetrics(SmCyCaption));
                maxClientW = Mathf.Max(640, work.right - work.left - frameX * 2 - 2);
                maxClientH = Mathf.Max(360, work.bottom - work.top - frameY * 2 - caption - 2);
            }
        }
        catch { /* Conservative cross-platform fallback above remains valid. */ }
#endif
        requestedW = Mathf.Max(640, requestedW);
        requestedH = Mathf.Max(360, requestedH);
        float scale = Mathf.Min(1f, Mathf.Min(maxClientW / (float)requestedW,
                                              maxClientH / (float)requestedH));
        int fittedW = Mathf.Max(640, Mathf.FloorToInt(requestedW * scale / 2f) * 2);
        int fittedH = Mathf.Max(360, Mathf.FloorToInt(requestedH * scale / 2f) * 2);
        return (fittedW, fittedH);
    }

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
        if (Fullscreen)
            Screen.SetResolution(w, h, Mode(true));
        else
        {
            var fit = FitWindowed(w, h);
            Screen.SetResolution(fit.w, fit.h, Mode(false));
        }
    }

    /// <summary>The resolution to go fullscreen AT.
    ///
    /// Deliberately NOT Current(): with no saved pick, Current() falls back to the live window
    /// size, so dragging the window small and then clicking fullscreen rendered the game at that
    /// tiny size and let the display upscale it — everything came out extremely blurry. An
    /// explicit pick is still honoured (choosing 1280x720 on a 4K screen is a real choice); it is
    /// only the "never picked one" fallback that must be the display's own resolution.</summary>
    private static (int w, int h) FullscreenTarget()
    {
        int w = PlayerPrefs.GetInt(KeyResW, 0);
        int h = PlayerPrefs.GetInt(KeyResH, 0);
        if (w > 0 && h > 0) return (w, h);
        return (Mathf.Max(Screen.currentResolution.width, 1280),
                Mathf.Max(Screen.currentResolution.height, 720));
    }

    public static void ApplyMode(bool fullscreen)
    {
        PlayerPrefs.SetInt(KeyFullscreen, fullscreen ? 1 : 0);
        PlayerPrefs.Save();
        var (w, h) = fullscreen ? FullscreenTarget() : FitWindowed(Current().w, Current().h);
        Screen.SetResolution(w, h, Mode(fullscreen));
    }

    /// <summary>Re-apply the saved display mode + resolution at launch. Safe to call unconditionally.</summary>
    public static void RestoreSaved()
    {
        var mode = Mode(Fullscreen);
        int w = PlayerPrefs.GetInt(KeyResW, 0);
        int h = PlayerPrefs.GetInt(KeyResH, 0);
        if (Fullscreen)
        {
            if (w > 0 && h > 0) Screen.SetResolution(w, h, mode);
            else Screen.fullScreenMode = mode;
        }
        else
        {
            int requestedW = w > 0 ? w : Mathf.Max(Screen.width, 1280);
            int requestedH = h > 0 ? h : Mathf.Max(Screen.height, 720);
            var fit = FitWindowed(requestedW, requestedH);
            Screen.SetResolution(fit.w, fit.h, mode);
        }
    }
}
