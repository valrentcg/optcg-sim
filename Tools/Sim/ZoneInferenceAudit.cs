using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Proves that a ". Then, …" rider can no longer decide which zone an effect asks the player
    /// to pick from. OP15-020 Fire Fist ends in "trash 2 cards from your hand", which used to make
    /// the WHOLE effect target the hand — so the UI demanded a discard before the board effect that
    /// comes first. InferTargetZone now reads only the first clause.
    ///
    /// This walks every printed effect through the REAL engine functions (not a reimplementation)
    /// and reports any card where the full text and its first clause still disagree.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- zoneaudit
    /// </summary>
    public static class ZoneInferenceAudit
    {
        public static int Run()
        {
            var cards = CardData.Library.Values.Where(c => !string.IsNullOrWhiteSpace(c.Effect)).ToList();
            Console.WriteLine($"=== Target-zone inference audit — {cards.Count} cards with effect text ===");

            var riderDecided = new List<(string id, string name, string whole, string first)>();
            int withRider = 0;

            foreach (var def in cards)
            {
                string text = def.Effect;
                int split = GameEngine.AuditThenSplit(text);
                if (split <= 0 || split > text.Length) continue;   // no rider (or deliberately kept joined)
                withRider++;

                string first = GameEngine.AuditInferZone(text.Substring(0, split));
                string whole = GameEngine.AuditInferZone(text);
                // AuditInferZone already splits internally, so `whole` SHOULD equal `first`.
                if (whole != first) riderDecided.Add((def.Id, def.Name, whole, first));
            }

            Console.WriteLine($"  cards whose text has a resolvable \". Then,\" rider: {withRider}");
            Console.WriteLine($"  still decided by the rider: {riderDecided.Count}");
            foreach (var r in riderDecided.Take(20))
                Console.WriteLine($"    {r.id}  {r.name}  whole={r.whole} first={r.first}");

            // Second measure: how many of those riders name a zone the first clause does NOT,
            // i.e. how many cards the fix actually changed.
            int changed = 0;
            foreach (var def in cards)
            {
                int split = GameEngine.AuditThenSplit(def.Effect);
                if (split <= 0 || split > def.Effect.Length) continue;
                string firstOnly = GameEngine.AuditInferZone(def.Effect.Substring(0, split));
                string riderOnly = GameEngine.AuditInferZone(def.Effect.Substring(split));
                if (firstOnly != riderOnly) changed++;
            }
            Console.WriteLine($"  cards where the rider's zone differs from the first clause's (fix applies): {changed}");

            // ── Locked: card names carry periods ────────────────────────────────────────────
            // 73 card names contain a '.' ("Monkey.D.Luffy", "Portgas.D.Ace", "Emporio.Ivankov"),
            // referenced by 58 cards. Regexes bounded with [^.] silently failed to match every one
            // of them and the effect fell through to NotAutomated. Any new [^.] class in a
            // name-bearing pattern will trip this.
            int dotFails = 0;
            foreach (var body in new[]{
                "play up to 1 Character card other than [Monkey.D.Luffy] from your hand.",
                "play up to 1 Character card other than [Emporio.Ivankov] from your hand.",
                "Play up to 1 [Portgas.D.Ace] with a cost of 2 from your hand.",
            })
            {
                var outcome = GameEngine.AuditResolverRecognizes("OP05-004", "activateMain", body).ToString();
                if (outcome != "Recognized")
                {
                    dotFails++;
                    Console.WriteLine($"    DOTTED-NAME REGRESSION [{outcome}] {body}");
                }
            }
            Console.WriteLine($"  dotted card names resolving correctly: {(dotFails == 0 ? "yes" : dotFails + " FAILED")}");

            Console.WriteLine(riderDecided.Count == 0
                ? "zoneaudit: PASS — no effect's zone is decided by its rider"
                : $"zoneaudit: FAIL — {riderDecided.Count} still mis-inferred");
            return (riderDecided.Count == 0 && dotFails == 0) ? 0 : 1;
        }
    }
}
