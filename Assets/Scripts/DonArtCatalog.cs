// Catalogue of selectable DON!! front arts.
//
// Deliberately DISCOVERED, not hardcoded: any .png/.jpg dropped into
// StreamingAssets/Cards/Don/ becomes a selectable DON with no code change. The stock art
// (donCardAltArt.png, which lives one level up) is always present as "default" and is always
// the fallback, so a config referencing art the player no longer has still renders.
//
// Ids are the bare filename without extension, so they survive being sent to an opponent who
// may or may not have that file — see DonDeckSettings for the wire format.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

public static class DonArtCatalog
{
    public const string DefaultId = "default";
    private const string DefaultFile = "donCardAltArt.png";
    private const string Folder = "Don";

    public struct Entry
    {
        public string Id;          // "default", "bellamy", …
        public string Display;     // "Default", "Bellamy"
        public string RelPath;     // path under the Cards root, forward-slashed
    }

    private static List<Entry> _entries;

    /// <summary>Every DON art available on this install. "default" is always first.</summary>
    public static IReadOnlyList<Entry> All
    {
        get { if (_entries == null) Rescan(); return _entries; }
    }

    public static int Count => All.Count;

    /// <summary>Forget the scan so newly added files are picked up (called by the picker UI).</summary>
    public static void Rescan()
    {
        _entries = new List<Entry>
        {
            new Entry { Id = DefaultId, Display = "Default", RelPath = DefaultFile },
        };

        string dir = CardAssets.LocalPath(Folder);
        if (!Directory.Exists(dir)) return;

        foreach (var path in Directory.GetFiles(dir)
                                      .Where(IsImage)
                                      .OrderBy(p => Path.GetFileName(p)))
        {
            string id = Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(id)) continue;
            if (_entries.Any(e => e.Id.Equals(id, System.StringComparison.OrdinalIgnoreCase))) continue;
            _entries.Add(new Entry
            {
                Id = id,
                Display = Prettify(id),
                RelPath = Folder + "/" + Path.GetFileName(path),
            });
        }
    }

    private static bool IsImage(string path)
    {
        string e = Path.GetExtension(path).ToLowerInvariant();
        return e == ".png" || e == ".jpg" || e == ".jpeg";
    }

    // "prb01_big_mom_gold" -> "Big Mom Gold".
    //
    // The id keeps its set prefix so two sets can both ship a "Luffy Gold" without colliding —
    // ids are what travel to an opponent, so they have to be unique — but the set code is
    // meaningless to a player and is dropped for display. A leading token of letters-then-digits
    // (prb01, op11, st03, eb01) is a set code; anything else is part of the name.
    private static readonly System.Text.RegularExpressions.Regex SetCode =
        new System.Text.RegularExpressions.Regex(@"^[a-z]{1,4}\d{1,3}$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);

    private static string Prettify(string id)
    {
        var words = id.Replace('_', ' ').Replace('-', ' ').Split(' ')
                      .Where(w => w.Length > 0)
                      .ToList();
        if (words.Count > 1 && SetCode.IsMatch(words[0])) words.RemoveAt(0);
        return string.Join(" ", words.Select(w => char.ToUpperInvariant(w[0]) + w.Substring(1)));
    }

    public static bool Has(string id) =>
        !string.IsNullOrEmpty(id) && All.Any(e => e.Id.Equals(id, System.StringComparison.OrdinalIgnoreCase));

    /// <summary>The file for this id, or the stock art when the id is unknown — which is what
    /// happens when an opponent sends art this install doesn't have.</summary>
    public static string RelPathFor(string id)
    {
        foreach (var e in All)
            if (e.Id.Equals(id ?? "", System.StringComparison.OrdinalIgnoreCase)) return e.RelPath;
        return DefaultFile;
    }

    public static string DisplayFor(string id)
    {
        foreach (var e in All)
            if (e.Id.Equals(id ?? "", System.StringComparison.OrdinalIgnoreCase)) return e.Display;
        return "Default";
    }
}
