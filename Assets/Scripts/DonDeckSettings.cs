// The player's personal DON!! deck: which art each of their DON cards wears.
//
// Three modes, matching how people actually want to customise these:
//   Default  — stock art on every DON.
//   Uniform  — one chosen art on every DON.
//   Custom   — per-slot, so a 10-DON deck can be ten different arts.
//
// Persisted in PlayerPrefs alongside the other "optcg.*" settings. Also serialises to a compact
// string for the network, because the opponent renders YOUR DON with YOUR art (see
// MatchStartPayload.southDon / northDon). Anything unrecognised on the far side resolves to the
// stock art via DonArtCatalog, so a player never sees a blank card because they lack a file.

using System.Collections.Generic;
using System.Linq;
using UnityEngine;

public static class DonDeckSettings
{
    public enum Mode { Default = 0, Uniform = 1, Custom = 2 }

    /// <summary>Largest DON deck any leader uses. Slots beyond a leader's real size are simply
    /// unused, so switching leaders never truncates a saved layout.</summary>
    public const int MaxSlots = 10;

    // SCOPED PER ACCOUNT. These keys used to be global ("optcg.don.mode"), so on a shared machine
    // the DON art one account chose showed up on every other account on the same PC — reported as
    // "looks like my don deck saved across accounts for some reason". The scope is
    // CurrentIdentityKey (a UGS PlayerId / "guest_…" / "local"), the same identity DeckStore and
    // ReplayStore partition by — NOT the display name, which the player can change at will.
    private static string Scope => AccountManager.CurrentIdentityKey;
    private static string KeyMode => "optcg.don.mode." + Scope;
    private static string KeyUniform => "optcg.don.uniform." + Scope;
    private static string KeySlots => "optcg.don.slots." + Scope;

    private const string LegacyKeyMode = "optcg.don.mode";
    private const string LegacyKeyUniform = "optcg.don.uniform";
    private const string LegacyKeySlots = "optcg.don.slots";
    private static bool legacyChecked;

    /// <summary>One-time adoption of the pre-scoping global keys, so a DON deck the player already
    /// built follows them into their own scope instead of silently resetting to stock. It lands in
    /// whichever account reads first after the update — there is no record of who authored it — and
    /// the globals are then removed, which is what stops the leak recurring.</summary>
    private static void EnsureMigrated()
    {
        if (legacyChecked) return;
        legacyChecked = true;
        bool hasLegacy = PlayerPrefs.HasKey(LegacyKeyMode)
                      || PlayerPrefs.HasKey(LegacyKeyUniform)
                      || PlayerPrefs.HasKey(LegacyKeySlots);
        if (!hasLegacy) return;
        if (!PlayerPrefs.HasKey(KeyMode))   // never overwrite a scoped config that already exists
        {
            if (PlayerPrefs.HasKey(LegacyKeyMode))
                PlayerPrefs.SetInt(KeyMode, PlayerPrefs.GetInt(LegacyKeyMode, 0));
            if (PlayerPrefs.HasKey(LegacyKeyUniform))
                PlayerPrefs.SetString(KeyUniform, PlayerPrefs.GetString(LegacyKeyUniform, ""));
            if (PlayerPrefs.HasKey(LegacyKeySlots))
                PlayerPrefs.SetString(KeySlots, PlayerPrefs.GetString(LegacyKeySlots, ""));
        }
        PlayerPrefs.DeleteKey(LegacyKeyMode);
        PlayerPrefs.DeleteKey(LegacyKeyUniform);
        PlayerPrefs.DeleteKey(LegacyKeySlots);
        PlayerPrefs.Save();
    }

    public static Mode CurrentMode
    {
        get { EnsureMigrated(); return (Mode)Mathf.Clamp(PlayerPrefs.GetInt(KeyMode, 0), 0, 2); }
        set { EnsureMigrated(); PlayerPrefs.SetInt(KeyMode, (int)value); PlayerPrefs.Save(); }
    }

    public static string UniformArt
    {
        get
        {
            EnsureMigrated();
            var id = PlayerPrefs.GetString(KeyUniform, DonArtCatalog.DefaultId);
            return string.IsNullOrEmpty(id) ? DonArtCatalog.DefaultId : id;
        }
        set { EnsureMigrated(); PlayerPrefs.SetString(KeyUniform, value ?? DonArtCatalog.DefaultId); PlayerPrefs.Save(); }
    }

    /// <summary>Per-slot art ids, always MaxSlots long.</summary>
    public static string[] Slots
    {
        get
        {
            EnsureMigrated();
            var raw = PlayerPrefs.GetString(KeySlots, "");
            var parts = string.IsNullOrEmpty(raw)
                ? new string[0]
                : raw.Split('|');
            var outIds = new string[MaxSlots];
            for (int i = 0; i < MaxSlots; i++)
                outIds[i] = i < parts.Length && !string.IsNullOrWhiteSpace(parts[i])
                    ? parts[i] : DonArtCatalog.DefaultId;
            return outIds;
        }
        set
        {
            EnsureMigrated();
            var ids = new string[MaxSlots];
            for (int i = 0; i < MaxSlots; i++)
                ids[i] = value != null && i < value.Length && !string.IsNullOrWhiteSpace(value[i])
                    ? value[i] : DonArtCatalog.DefaultId;
            PlayerPrefs.SetString(KeySlots, string.Join("|", ids));
            PlayerPrefs.Save();
        }
    }

    public static void SetSlot(int index, string artId)
    {
        if (index < 0 || index >= MaxSlots) return;
        var s = Slots;
        s[index] = artId ?? DonArtCatalog.DefaultId;
        Slots = s;
    }

    /// <summary>Art id for the DON in this slot under the CURRENT local settings.</summary>
    public static string ArtForSlot(int index)
    {
        switch (CurrentMode)
        {
            case Mode.Uniform: return UniformArt;
            case Mode.Custom:
                var s = Slots;
                return index >= 0 && index < s.Length ? s[index] : DonArtCatalog.DefaultId;
            default: return DonArtCatalog.DefaultId;
        }
    }

    // ---- wire format ------------------------------------------------------------------------
    // "mode:art0,art1,…". Compact, human-readable in a log, and forward-safe: an older client
    // that doesn't know the field just sends null and everyone renders stock art.

    public static string Serialize()
    {
        switch (CurrentMode)
        {
            case Mode.Uniform: return "u:" + UniformArt;
            case Mode.Custom: return "c:" + string.Join(",", Slots);
            default: return "d:";
        }
    }

    /// <summary>Resolve a serialised config (possibly from the opponent) to a per-slot lookup.
    /// Unknown art ids fall through to the stock art rather than rendering nothing.</summary>
    public static System.Func<int, string> Resolver(string serialized)
    {
        if (string.IsNullOrWhiteSpace(serialized) || serialized.Length < 2)
            return _ => DonArtCatalog.DefaultId;

        char mode = serialized[0];
        string body = serialized.Substring(2);

        if (mode == 'u')
        {
            string id = DonArtCatalog.Has(body) ? body : DonArtCatalog.DefaultId;
            return _ => id;
        }
        if (mode == 'c')
        {
            var parts = body.Split(',');
            var ids = new string[MaxSlots];
            for (int i = 0; i < MaxSlots; i++)
            {
                var raw = i < parts.Length ? parts[i] : null;
                ids[i] = DonArtCatalog.Has(raw) ? raw : DonArtCatalog.DefaultId;
            }
            return i => i >= 0 && i < ids.Length ? ids[i] : DonArtCatalog.DefaultId;
        }
        return _ => DonArtCatalog.DefaultId;
    }

    /// <summary>True when this config would look any different from stock — lets callers skip
    /// sending or resolving anything in the overwhelmingly common default case.</summary>
    public static bool IsCustomised =>
        CurrentMode != Mode.Default &&
        (CurrentMode == Mode.Uniform
            ? UniformArt != DonArtCatalog.DefaultId
            : Slots.Any(s => s != DonArtCatalog.DefaultId));
}
