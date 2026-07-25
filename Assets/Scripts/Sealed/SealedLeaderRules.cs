// One Piece TCG — Sealed / Pre-Release: which Leader you may play.
//
// Pure C#, no UnityEngine. There are two real variants of the sealed format, and events run both:
//
//   RAINBOW LUFFY  — everyone plays the prerelease-exclusive "Rainbow Luffy" Leader. It is printed to
//                    work with ANY colour, activate ANY type-based effect, and count as EVERY
//                    character name, which is precisely why it is legal only at prereleases. It makes
//                    every pool playable, since no card in your pool can be off-colour or off-type.
//
//   FREE SELECT    — you may bring any Leader in the game, not just one you opened. Banned Leaders are
//                    still banned; the shared ban list is the ONLY constructed restriction that
//                    survives into this variant.
//
// (A third, stricter house variant — Leader must come from your own pool — is kept as an option
// because a self-contained digital run can always satisfy it.)
//
// Rainbow Luffy is NOT in the scraped card library: it is an event-exclusive promo that never
// appeared on a set page. It is registered here at load time as a mode-local card, and its wildcard
// behaviour rides on CardDef.WildcardIdentity, which the engine honours at its two identity
// chokepoints (CardDef.HasFeature for {type} checks, GameEngine.NameMatches for [Name] checks).

using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sealed
{
    public enum SealedLeaderMode
    {
        /// <summary>Everyone plays Rainbow Luffy (the classic prerelease experience).</summary>
        RainbowLuffy = 0,
        /// <summary>Any Leader in the game, minus the shared ban list.</summary>
        FreeSelect = 1,
        /// <summary>Only Leaders you actually opened.</summary>
        PoolOnly = 2,
    }

    public static class SealedLeaderRules
    {
        /// <summary>Synthetic id for the prerelease Rainbow Luffy Leader. Deliberately not an
        /// "OPxx-nnn"/"P-nnn" shape so it can never collide with a real card id if the scrape ever
        /// starts carrying event promos.</summary>
        public const string RainbowLuffyId = "SEALED-RAINBOW-LUFFY";

        /// <summary>Every colour, so any pool card is on-colour under this Leader.</summary>
        private const string AllColours = "Red/Green/Blue/Purple/Black/Yellow";

        private static bool registered;

        /// <summary>Register Rainbow Luffy into the card library. Idempotent, and safe to call before
        /// or after the library loads — Sealed mode calls it when the mode opens.</summary>
        public static void EnsureRegistered()
        {
            if (registered && CardData.Library.ContainsKey(RainbowLuffyId)) return;

            CardData.UpsertCard(
                RainbowLuffyId,
                "Monkey.D.Luffy",
                "leader",
                AllColours,
                cost: 0,
                power: 5000,
                life: 5,
                counter: 0,
                keywords: null,
                effect: "Under the rules of this game, this Leader is every colour, counts as every type, "
                      + "and counts as every card name.\n(Sealed / Pre-Release only.)",
                trigger: "",
                features: new[] { "Straw Hat Crew" },
                rarity: "L",
                attribute: "Strike",
                block: "-");

            // The wildcard behaviour itself. HasFeature and NameMatches both honour this flag, so any
            // "{type}" or "[Name]" requirement in the pool is satisfied by this Leader.
            var def = CardData.GetCard(RainbowLuffyId);
            if (def != null) def.WildcardIdentity = true;
            registered = true;
        }

        public static bool IsRainbowLuffy(string cardId) =>
            string.Equals(cardId, RainbowLuffyId, StringComparison.OrdinalIgnoreCase);

        /// <summary>The Leaders a player may choose under <paramref name="mode"/>.</summary>
        public static List<string> LegalLeaders(SealedLeaderMode mode, SealedPool pool)
        {
            switch (mode)
            {
                case SealedLeaderMode.RainbowLuffy:
                    EnsureRegistered();
                    return new List<string> { RainbowLuffyId };

                case SealedLeaderMode.FreeSelect:
                    // Any Leader in the game except banned ones. The ban list is the only constructed
                    // restriction that survives into sealed.
                    return CardData.Library
                        .Where(kv => kv.Value?.Type == "leader" && !IsBannedLeader(kv.Key))
                        .Select(kv => kv.Key)
                        .OrderBy(id => id, StringComparer.Ordinal)
                        .ToList();

                default:
                    return pool?.AvailableLeaders() ?? new List<string>();
            }
        }

        /// <summary>Banned Leaders, via the shared format ban list the rest of the app already uses, so
        /// sealed can never drift from it.</summary>
        public static bool IsBannedLeader(string cardId)
        {
            try { return FormatLegality.IsBanned(cardId); }
            catch { return false; }   // ban list unavailable — fail open rather than block every Leader
        }

        /// <summary>Default Leader for a mode, used when a run is created or reset.</summary>
        public static string DefaultLeader(SealedLeaderMode mode, SealedPool pool)
        {
            if (mode == SealedLeaderMode.RainbowLuffy) { EnsureRegistered(); return RainbowLuffyId; }
            return null;   // Free-select and pool-only both require a deliberate choice
        }

        public static string ModeName(SealedLeaderMode mode) => mode switch
        {
            SealedLeaderMode.RainbowLuffy => "Rainbow Luffy",
            SealedLeaderMode.FreeSelect => "Free Select",
            _ => "From Your Pool",
        };

        public static string ModeBlurb(SealedLeaderMode mode) => mode switch
        {
            SealedLeaderMode.RainbowLuffy =>
                "Everyone plays the prerelease Rainbow Luffy — every colour, every type, every name. "
                + "Your whole pool is playable.",
            SealedLeaderMode.FreeSelect =>
                "Bring any Leader in the game. Banned Leaders are still banned.",
            _ => "Play only a Leader you actually opened.",
        };
    }
}
