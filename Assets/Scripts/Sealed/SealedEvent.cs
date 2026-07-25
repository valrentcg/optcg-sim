// One Piece TCG — Sealed / Pre-Release: the EVENT layer (build timer, Swiss rounds, standings).
//
// Pure C#, no UnityEngine — so pairings and standings can be unit-tested headlessly rather than
// debugged through the UI.
//
// Swiss is modelled the way a local actually runs: everyone opens, everyone builds against a clock,
// then N rounds pairing players on equal score, avoiding rematches where possible, with a bye when
// the field is odd. Deck edits are allowed BETWEEN rounds (the pool never changes, only the 40).

using System;
using System.Collections.Generic;
using System.Linq;

namespace OnePieceTcg.Sealed
{
    public enum EventPhase { Building, Round, RoundComplete, Finished }

    public sealed class SealedEntrant
    {
        public string Id;                 // "player" or "ai1"...
        public string DisplayName;
        public bool IsHuman;
        public SealedPool Pool;

        public int Wins, Losses, Draws, Byes;
        public int MatchPoints => Wins * 3 + Draws;
        /// <summary>Opponents faced, so Swiss can avoid rematches and compute tiebreaks.</summary>
        public List<string> Opponents = new List<string>();
    }

    public sealed class SealedPairing
    {
        public int Round;
        public string AId, BId;           // BId null => bye
        public string WinnerId;           // null until reported
        public bool IsBye => string.IsNullOrEmpty(BId);
        public bool Reported => !string.IsNullOrEmpty(WinnerId) || IsDraw;
        public bool IsDraw;
    }

    /// <summary>A full sealed event: field, rounds, standings.</summary>
    public sealed class SealedEvent
    {
        public string SetCode;
        public string Seed;
        public int PlayerCount = 8;
        public int TotalRounds = 3;
        public int BuildSeconds = 50 * 60;    // 50:00, as at a real prerelease
        public bool Timed = true;

        public EventPhase Phase = EventPhase.Building;
        public int CurrentRound;              // 1-based once play starts
        public List<SealedEntrant> Entrants = new List<SealedEntrant>();
        public List<SealedPairing> Pairings = new List<SealedPairing>();

        public SealedEntrant Human() => Entrants.FirstOrDefault(e => e.IsHuman);

        /// <summary>Swiss rounds for a field size, the way events actually scale (8 players → 3).</summary>
        public static int RecommendedRounds(int players) =>
            players <= 2 ? 1 : players <= 4 ? 2 : players <= 8 ? 3 : players <= 16 ? 4 : 5;

        /// <summary>Create an event: the human's pool plus N-1 A.I. entrants, each with their OWN
        /// seeded pool derived from the event seed, and each with a deck already built.</summary>
        public static SealedEvent Create(SealedProduct product, string seed, SealedPool humanPool,
            int playerCount = 8, bool timed = true)
        {
            seed = PackGenerator.NormalizeSeed(seed);
            var ev = new SealedEvent
            {
                SetCode = product.SetCode,
                Seed = seed,
                PlayerCount = Math.Max(2, playerCount),
                Timed = timed,
            };
            ev.TotalRounds = RecommendedRounds(ev.PlayerCount);

            ev.Entrants.Add(new SealedEntrant
            {
                Id = "player",
                DisplayName = "You",
                IsHuman = true,
                Pool = humanPool ?? SealedPool.Generate(product, seed),
            });

            for (int i = 1; i < ev.PlayerCount; i++)
            {
                var aiPool = SealedDeckAI.BuildOpponent(product, seed, i);
                ev.Entrants.Add(new SealedEntrant
                {
                    Id = "ai" + i,
                    DisplayName = AiName(seed, i),
                    IsHuman = false,
                    Pool = aiPool,
                });
            }
            return ev;
        }

        /// <summary>Deterministic opponent names, so the same seed always produces the same field.</summary>
        private static string AiName(string seed, int index)
        {
            string[] names =
            {
                "Koby", "Helmeppo", "Tashigi", "Smoker", "Bogard", "Hina", "Fullbody", "Momonga",
                "Dalmatian", "Onigumo", "Doberman", "Strawberry", "Yamakaji", "Comil", "Stainless",
            };
            var rng = new SealedRng($"{seed}|name{index}");
            return names[rng.NextInt(names.Length)] + " #" + index;
        }

        // ---- Rounds ---------------------------------------------------------------------------

        /// <summary>Close the build phase and pair round 1.</summary>
        public void StartPlay()
        {
            if (Phase != EventPhase.Building) return;
            CurrentRound = 0;
            NextRound();
        }

        /// <summary>Pair the next Swiss round. Players are bucketed by match points, shuffled inside
        /// their bucket deterministically, then paired down the list while avoiding rematches.</summary>
        public void NextRound()
        {
            if (CurrentRound >= TotalRounds) { Phase = EventPhase.Finished; return; }
            CurrentRound++;
            Phase = EventPhase.Round;

            var field = Entrants
                .OrderByDescending(e => e.MatchPoints)
                .ThenBy(e => DeterministicKey(e.Id))
                .ToList();

            var unpaired = new List<SealedEntrant>(field);
            while (unpaired.Count > 0)
            {
                var a = unpaired[0];
                unpaired.RemoveAt(0);
                if (unpaired.Count == 0)
                {
                    // Odd field: bye. A bye counts as a win, as at a real event.
                    Pairings.Add(new SealedPairing { Round = CurrentRound, AId = a.Id, BId = null, WinnerId = a.Id });
                    a.Wins++; a.Byes++;
                    break;
                }

                // Prefer the nearest opponent this player has NOT already faced.
                int pick = unpaired.FindIndex(x => !a.Opponents.Contains(x.Id));
                if (pick < 0) pick = 0;                 // small field: rematch is unavoidable
                var b = unpaired[pick];
                unpaired.RemoveAt(pick);

                a.Opponents.Add(b.Id);
                b.Opponents.Add(a.Id);
                Pairings.Add(new SealedPairing { Round = CurrentRound, AId = a.Id, BId = b.Id });
            }
        }

        private int DeterministicKey(string id) => (int)(SealedRng.HashString(Seed + "|" + id + "|" + CurrentRound) % 100000);

        public SealedPairing CurrentPairingFor(string entrantId) =>
            Pairings.FirstOrDefault(p => p.Round == CurrentRound && (p.AId == entrantId || p.BId == entrantId));

        public SealedEntrant Get(string id) => Entrants.FirstOrDefault(e => e.Id == id);

        /// <summary>Record a result. Passing null <paramref name="winnerId"/> records a draw.</summary>
        public void Report(SealedPairing pairing, string winnerId)
        {
            if (pairing == null || pairing.Reported) return;
            var a = Get(pairing.AId);
            var b = Get(pairing.BId);

            if (string.IsNullOrEmpty(winnerId))
            {
                pairing.IsDraw = true;
                if (a != null) a.Draws++;
                if (b != null) b.Draws++;
            }
            else
            {
                pairing.WinnerId = winnerId;
                var w = Get(winnerId);
                var l = winnerId == pairing.AId ? b : a;
                if (w != null) w.Wins++;
                if (l != null) l.Losses++;
            }

            // Once every pairing this round is in, the round is done.
            if (Pairings.Where(p => p.Round == CurrentRound).All(p => p.Reported))
                Phase = CurrentRound >= TotalRounds ? EventPhase.Finished : EventPhase.RoundComplete;
        }

        /// <summary>Resolve every A.I.-vs-A.I. pairing this round so the standings stay believable
        /// without simulating full matches. Deterministic from the seed, and weighted by how strong
        /// each bot's deck actually is, so a good pool tends to do well.</summary>
        public void ResolveAiPairings()
        {
            foreach (var p in Pairings.Where(x => x.Round == CurrentRound && !x.Reported && !x.IsBye).ToList())
            {
                var a = Get(p.AId);
                var b = Get(p.BId);
                if (a == null || b == null || a.IsHuman || b.IsHuman) continue;

                double sa = DeckStrength(a), sb = DeckStrength(b);
                double pA = sa + sb <= 0 ? 0.5 : sa / (sa + sb);
                var rng = new SealedRng($"{Seed}|r{CurrentRound}|{p.AId}v{p.BId}");
                Report(p, rng.NextDouble() < pA ? p.AId : p.BId);
            }
        }

        /// <summary>A crude but honest proxy for how good a built sealed deck is — used ONLY to resolve
        /// A.I.-vs-A.I. results, never to decide a game the human plays.</summary>
        private static double DeckStrength(SealedEntrant e)
        {
            if (e?.Pool == null) return 1;
            var s = e.Pool.DeckStats();
            return 1
                + s.Blockers * 0.30
                + s.Triggers * 0.10
                + (s.Counter1000 + s.Counter2000) * 0.06
                + Math.Max(0, 4.0 - Math.Abs(s.AverageCost - 3.2)) * 0.25;
        }

        // ---- Standings ------------------------------------------------------------------------

        public sealed class Standing
        {
            public int Rank;
            public SealedEntrant Entrant;
            public double OpponentWinPct;      // the usual Swiss tiebreak
        }

        /// <summary>Standings by match points, then opponents' win percentage (OMW) — the tiebreak a
        /// real Swiss event uses, so the final table matches what players expect.</summary>
        public List<Standing> Standings()
        {
            double WinPct(SealedEntrant e)
            {
                int played = e.Wins + e.Losses + e.Draws;
                return played == 0 ? 0.5 : Math.Max(0.33, (e.Wins + 0.5 * e.Draws) / played);
            }

            var list = Entrants.Select(e => new Standing
            {
                Entrant = e,
                OpponentWinPct = e.Opponents.Count == 0
                    ? 0
                    : e.Opponents.Select(Get).Where(o => o != null).Select(WinPct).DefaultIfEmpty(0).Average(),
            })
            .OrderByDescending(s => s.Entrant.MatchPoints)
            .ThenByDescending(s => s.OpponentWinPct)
            .ThenBy(s => s.Entrant.DisplayName, StringComparer.Ordinal)
            .ToList();

            for (int i = 0; i < list.Count; i++) list[i].Rank = i + 1;
            return list;
        }

        public string RecordText(SealedEntrant e) =>
            e == null ? "" : $"{e.Wins}-{e.Losses}" + (e.Draws > 0 ? $"-{e.Draws}" : "");
    }
}
