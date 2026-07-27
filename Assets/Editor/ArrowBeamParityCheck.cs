using System.IO;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// The travelling surge is implemented twice — once in C# (TargetingArrowGraphic.Surge,
/// which swells the ribbon geometry) and once in HLSL (ArrowBeam.shader SurgePulse,
/// which brightens it). They must agree exactly or the bright band and the fat band
/// separate and the beam looks like it has a bead sliding inside it.
///
/// "Must match exactly" as a comment is what produced the bug the design doc's §6 is
/// about, so this checks it instead of asking. It parses the numbers out of both files
/// and reports on any drift. Runs on load and on any reimport of either file, and can
/// be run on demand from Tools ▸ Arrow Beam ▸ Check surge parity.
/// </summary>
[InitializeOnLoad]
public static class ArrowBeamParityCheck
{
    const string CsPath     = "Assets/Scripts/TargetingArrowGraphic.cs";
    const string ShaderPath = "Assets/ArrowBeam.shader";

    static ArrowBeamParityCheck() { EditorApplication.delayCall += () => Verify(false); }

    [MenuItem("Tools/Arrow Beam/Check surge parity")]
    static void Menu() { if (Verify(true)) Debug.Log("[ArrowBeam] surge parity OK — C# and HLSL agree."); }

    /// <summary>True when the two implementations agree (or a file is missing, which is
    /// not this check's business to complain about).</summary>
    public static bool Verify(bool verbose)
    {
        string cs = Read(CsPath), sh = Read(ShaderPath);
        if (cs == null || sh == null) return true;

        // The five numbers that define the pulse. Named so a failure says which one moved.
        var checks = new (string name, string csPattern, string shPattern)[]
        {
            ("rate",        @"t \* (0\.\d+)f \+ i",            @"t \* (0\.\d+) \+ i"),
            ("span",        @"1f \+ ARM_SPAN \+ (0\.\d+)f",    @"1\.0 \+ ARM_SPAN \+ (0\.\d+)"),
            ("offset",      @"\) - (0\.\d+)f;",                @"\) - (0\.\d+);"),
            ("lead sigma",  @"d > 0f \? (0\.\d+)f",            @"d > 0\.0 \? (0\.\d+)"),
            ("tail sigma",  @"d > 0f \? 0\.\d+f : (0\.\d+)f",  @"d > 0\.0 \? 0\.\d+ : (0\.\d+)"),
        };

        bool ok = true;
        foreach (var (name, csPat, shPat) in checks)
        {
            string a = First(cs, csPat), b = First(sh, shPat);
            if (a == null || b == null)
            {
                Debug.LogWarning($"[ArrowBeam] could not read '{name}' from " +
                                 (a == null ? "TargetingArrowGraphic.cs" : "ArrowBeam.shader") +
                                 " — the surge parity check needs updating alongside the code.");
                ok = false;
                continue;
            }
            if (!Approximately(a, b))
            {
                Debug.LogError($"[ArrowBeam] SURGE DRIFT — '{name}' is {a} in C# but {b} in the shader. " +
                               "The geometry swell and the bright pulse will separate. " +
                               "Fix one to match the other (ArrowBeam.shader / TargetingArrowGraphic.Surge).");
                ok = false;
            }
        }

        // ARM_SPAN is shared by the mesh builder and both shader passes.
        string csArm = First(cs, @"ARM_SPAN\s*=\s*(0\.\d+)f");
        foreach (Match m in Regex.Matches(sh, @"#define ARM_SPAN (0\.\d+)"))
        {
            if (csArm != null && !Approximately(csArm, m.Groups[1].Value))
            {
                Debug.LogError($"[ArrowBeam] ARM_SPAN is {csArm} in C# but {m.Groups[1].Value} in the shader — " +
                               "the pulse will not reach the ends of the head arms.");
                ok = false;
            }
        }

        if (verbose && !ok) Debug.LogError("[ArrowBeam] surge parity FAILED — see the errors above.");
        return ok;
    }

    static string Read(string p)
    {
        string full = Path.Combine(Directory.GetCurrentDirectory(), p);
        return File.Exists(full) ? File.ReadAllText(full) : null;
    }

    static string First(string text, string pattern)
    {
        var m = Regex.Match(text, pattern);
        return m.Success ? m.Groups[1].Value : null;
    }

    static bool Approximately(string a, string b) =>
        float.TryParse(a, System.Globalization.NumberStyles.Float,
                       System.Globalization.CultureInfo.InvariantCulture, out var x)
        && float.TryParse(b, System.Globalization.NumberStyles.Float,
                          System.Globalization.CultureInfo.InvariantCulture, out var y)
        && Mathf.Abs(x - y) < 1e-6f;

    /// <summary>Re-check whenever either file is touched.</summary>
    class Watcher : AssetPostprocessor
    {
        static void OnPostprocessAllAssets(string[] imported, string[] deleted,
                                           string[] moved, string[] movedFrom)
        {
            foreach (var p in imported)
                if (p == CsPath || p == ShaderPath) { Verify(false); return; }
        }
    }
}
