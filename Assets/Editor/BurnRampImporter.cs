// Forces the import settings the burn heat-ramps need. These are 256x8 gradient strips
// sampled by UI/CardDissolve at a single v, and read back on the CPU to tint the ember
// burst, so they must be clamped, unfiltered by mips, uncompressed and readable.
//
// Doing this from a postprocessor rather than checked-in .meta files means the settings
// survive a re-import and apply to any ramp added later, with no GUID to keep in sync.
using UnityEditor;
using UnityEngine;

public sealed class BurnRampImporter : AssetPostprocessor
{
    private void OnPreprocessTexture()
    {
        if (assetPath == null) return;
        if (assetPath.IndexOf("/Resources/ramp_", System.StringComparison.OrdinalIgnoreCase) < 0) return;

        var ti = (TextureImporter)assetImporter;
        ti.textureType         = TextureImporterType.Default;
        ti.wrapMode            = TextureWrapMode.Clamp;   // no bleed from u=1 back to u=0
        ti.filterMode          = FilterMode.Bilinear;
        ti.mipmapEnabled       = false;                   // sampled at one v; mips would smear it
        ti.sRGBTexture         = true;
        ti.alphaSource         = TextureImporterAlphaSource.None;
        ti.isReadable          = true;                    // CPU reads the hot end for ember tint
        ti.npotScale           = TextureImporterNPOTScale.None;
        ti.textureCompression  = TextureImporterCompression.Uncompressed;
    }
}
