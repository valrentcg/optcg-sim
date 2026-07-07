// Keeps StreamingAssets/Cards (2.5 GB of card art) OUT of WebGL builds — the
// web version streams all of it from the R2 CDN via CardAssets instead.
// Desktop builds are untouched and still bundle the folder.
//
// How: before a WebGL build, Assets/StreamingAssets/Cards (+ .meta) is moved to
// Temp/ExcludedFromBuild_Cards at the project root; after the build (success OR
// failure) it's moved back. If Unity crashes mid-build and the folder is left
// out, use the menu item:  Tools > OPTCG > Restore StreamingAssets Cards.

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

public class WebGLBuildProcessor : IPreprocessBuildWithReport, IPostprocessBuildWithReport
{
    public int callbackOrder => 0;

    static string CardsPath   => Path.Combine(Application.dataPath, "StreamingAssets", "Cards");
    static string CardsMeta   => CardsPath + ".meta";
    static string StashDir    => Path.Combine(Directory.GetParent(Application.dataPath).FullName, "Temp", "ExcludedFromBuild_Cards");
    static string StashCards  => Path.Combine(StashDir, "Cards");
    static string StashMeta   => Path.Combine(StashDir, "Cards.meta");

    public void OnPreprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.WebGL) return;
        if (!Directory.Exists(CardsPath)) return;

        Debug.Log("[WebGLBuildProcessor] Moving StreamingAssets/Cards out of the build (streamed from CDN on WebGL).");
        if (Directory.Exists(StashCards)) Directory.Delete(StashCards, true);
        Directory.CreateDirectory(StashDir);
        Directory.Move(CardsPath, StashCards);
        if (File.Exists(CardsMeta))
        {
            if (File.Exists(StashMeta)) File.Delete(StashMeta);
            File.Move(CardsMeta, StashMeta);
        }
        AssetDatabase.Refresh();
    }

    public void OnPostprocessBuild(BuildReport report)
    {
        if (report.summary.platform != BuildTarget.WebGL) return;
        Restore();
    }

    [MenuItem("Tools/OPTCG/Restore StreamingAssets Cards")]
    public static void Restore()
    {
        if (!Directory.Exists(StashCards))
        {
            Debug.Log("[WebGLBuildProcessor] Nothing to restore.");
            return;
        }
        if (Directory.Exists(CardsPath))
        {
            Debug.LogWarning("[WebGLBuildProcessor] Cards folder already exists in StreamingAssets; leaving stash in Temp/ExcludedFromBuild_Cards.");
            return;
        }
        Debug.Log("[WebGLBuildProcessor] Restoring StreamingAssets/Cards.");
        Directory.Move(StashCards, CardsPath);
        if (File.Exists(StashMeta) && !File.Exists(CardsMeta)) File.Move(StashMeta, CardsMeta);
        AssetDatabase.Refresh();
    }
}
#endif
