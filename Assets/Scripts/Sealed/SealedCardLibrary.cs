// One Piece TCG — Sealed / Pre-Release: making sure CardData actually holds the full card library.
//
// TWO problems this exists to solve, both of which made Sealed silently do nothing:
//
//  1. THE MENU NEVER POPULATES CardData. MainMenuManager parses official-card-library.json into its
//     own menu-only structures (menuCardsById), and GameManager only loads CardData when a MATCH
//     starts. So from the main menu, CardData.Library holds just the handful of starter-deck cards
//     hardcoded in CardData.cs — no OP/EB sets at all. SealedCatalog.Available() therefore returned
//     an empty list, chosenProduct stayed null, and "OPEN PACKS" hit an early return: no crash, no
//     log line, nothing on screen.
//
//  2. THE MATCH LOADER DROPS RARITY. GameManager's OfficialCardRecord has no `rarity` field, so even
//     mid-match every CardDef.Rarity is empty. Sealed is entirely rarity-driven — pack slots, pull
//     rates, the SR/SEC celebrations — so it needs rarity present, not just card ids. (The headless
//     tests never caught this because Tools/Harness/CardLibraryLoader does read rarity, so the Sim
//     and the game were loading DIFFERENT views of the same file.)
//
// Loading here is idempotent and cheap to re-check, so callers can just await it on entry.

using System;
using System.Threading.Tasks;
using UnityEngine;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    public static class SealedCardLibrary
    {
        public static bool Loaded { get; private set; }
        private static Task loading;

        /// <summary>True when CardData looks like it holds the real library WITH rarity — the two
        /// things Sealed needs. Cheap enough to call before every entry into the mode.</summary>
        public static bool LooksLoaded()
        {
            int booster = 0;
            foreach (var kv in CardData.Library)
            {
                if (kv.Key.Length < 3) continue;
                if ((kv.Key.StartsWith("OP", StringComparison.OrdinalIgnoreCase)
                     || kv.Key.StartsWith("EB", StringComparison.OrdinalIgnoreCase))
                    && !string.IsNullOrEmpty(kv.Value?.Rarity))
                {
                    if (++booster >= 50) return true;   // a real set is present, with rarities
                }
            }
            return false;
        }

        /// <summary>Load the official library into CardData, including rarity. Safe to call repeatedly;
        /// concurrent callers share one load.</summary>
        public static Task EnsureLoadedAsync()
        {
            if (Loaded || LooksLoaded()) { Loaded = true; return Task.CompletedTask; }
            return loading ??= LoadAsync();
        }

        private static async Task LoadAsync()
        {
            try
            {
                while (!CardAssets.Ready) await Task.Yield();
                string json = await CardAssets.ReadTextAsync("official-card-library.json");
                if (string.IsNullOrEmpty(json))
                {
                    Debug.LogWarning("[SealedCardLibrary] official-card-library.json was empty or missing — "
                                   + "Sealed cannot build a card pool.");
                    return;
                }
                Parse(json);
                Loaded = true;
                Debug.Log($"[SealedCardLibrary] loaded {CardData.Library.Count} card definitions (with rarity).");
            }
            catch (Exception e)
            {
                Debug.LogError("[SealedCardLibrary] load failed: " + e.Message);
            }
            finally { loading = null; }
        }

        private static void Parse(string json)
        {
            var payload = JsonUtility.FromJson<Payload>("{\"cards\":" + json + "}");
            if (payload?.cards == null) return;

            foreach (var c in payload.cards)
            {
                if (c == null || string.IsNullOrEmpty(c.id)) continue;

                // The JSON carries one entry PER PRINT, so alt arts repeat an id. Keep the FIRST
                // (the base print), matching how the harness loader and the rest of the app behave —
                // otherwise a card's rarity would end up as whichever parallel happened to be last.
                var existing = CardData.GetCard(c.id);
                if (existing != null && existing.Type != "unknown" && !string.IsNullOrEmpty(existing.Rarity)) continue;

                string[] features = c.features != null && c.features.Length > 0
                    ? c.features
                    : (string.IsNullOrEmpty(c.feature) ? Array.Empty<string>() : new[] { c.feature });

                CardData.UpsertCard(
                    c.id, c.name, c.type, c.color, c.cost, c.power,
                    c.life > 0 ? c.life : (int?)null, c.counter,
                    c.keywords, c.effect, c.trigger, features,
                    c.rarity, c.attribute, c.block);
            }
        }

        [Serializable] private sealed class Payload { public Record[] cards = Array.Empty<Record>(); }

        [Serializable]
        private sealed class Record
        {
            public string id = "";
            public string type = "";
            public string name = "";
            public string color = "";
            public int cost, life, power, counter;
            public string effect = "";
            public string trigger = "";
            public string[] keywords = Array.Empty<string>();
            public string[] features = Array.Empty<string>();
            public string feature = "";
            public string attribute = "";
            public string block = "";
            /// <summary>The field GameManager's own record type omits — the whole reason this exists.</summary>
            public string rarity = "";
        }
    }
}
