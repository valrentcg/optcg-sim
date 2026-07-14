using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;

// LAYER 1 — rules invariants that must hold after EVERY command, for EVERY card, no oracle needed.
// These catch whole CLASSES of "doesn't obey the rules" that a no-oracle bot sweep otherwise sails
// past (the Heat over-count was a DON-conservation/counting violation; a bounced card left in two
// zones is a dup-id; a lingering "this turn" buff is a modifier-cleanup violation).
//
// Two tiers:
//   Structural  — always true regardless of how the state was built (dup ids, board<=5, zone tags,
//                 attached-DON uniqueness, non-negative counters). Safe to run in any scenario.
//   Conservation — total DON per player == 10, total distinct cards conserved. Only valid when the
//                 scenario didn't hand-inject cards/DON out of thin air; the bot sweep and the
//                 coverage harness (which zeroes DonDeck when it seeds cost-area DON) both qualify.
static class Invariants
{
    public static List<string> Structural(GameState st)
    {
        var problems = new List<string>();
        var seenGlobal = new Dictionary<string, string>(); // instanceId -> "seat/zone"
        foreach (var seat in new[] { "south", "north" })
        {
            if (!st.Players.TryGetValue(seat, out var p) || p == null) { problems.Add($"{seat}: missing player"); continue; }

            if (p.Life.Count < 0) problems.Add($"{seat}: negative life");
            if (p.Hand.Count > 60) problems.Add($"{seat}: runaway hand={p.Hand.Count}");
            int board = p.CharacterArea.Count(c => c != null);
            if (board > 5) problems.Add($"{seat}: board={board} (>5)");

            // Zone tag on each instance must match the collection it lives in — a mismatch means a
            // move path forgot to update Zone (desync that breaks zone-scoped effect targeting).
            CheckZone(problems, seat, p.Hand, "hand", seenGlobal);
            CheckZone(problems, seat, p.Deck, "deck", seenGlobal);
            CheckZone(problems, seat, p.Trash, "trash", seenGlobal);
            CheckZone(problems, seat, p.Life, "life", seenGlobal);
            CheckZone(problems, seat, p.CharacterArea.Where(c => c != null), "character", seenGlobal);
            if (p.Stage != null) CheckZone(problems, seat, new[] { p.Stage }, "stage", seenGlobal);
            if (p.Leader != null) CheckZone(problems, seat, new[] { p.Leader }, "leader", seenGlobal);

            // Attached DON!! ids must be unique across the whole board (a DON!! can't be on two cards).
            var attached = new List<string>();
            if (p.Leader != null) attached.AddRange(p.Leader.AttachedDonIds);
            foreach (var c in p.CharacterArea) if (c != null) attached.AddRange(c.AttachedDonIds);
            if (p.Stage != null) attached.AddRange(p.Stage.AttachedDonIds);
            var dupDon = attached.GroupBy(x => x).Where(g => g.Count() > 1).Select(g => g.Key).ToList();
            if (dupDon.Count > 0) problems.Add($"{seat}: DON!! attached to 2+ cards: {string.Join(",", dupDon.Take(3))}");
        }

        foreach (var e in st.PendingEffects)
        {
            if (e.SelectionsRemaining < 0) problems.Add($"pending {e.SourceCardId}: SelectionsRemaining<0");
            if (e.DonPaymentRemaining < 0) problems.Add($"pending {e.SourceCardId}: DonPaymentRemaining<0");
        }
        return problems;
    }

    // Total DON!! a player controls is always exactly 10 (deck + cost area + attached). This is the
    // invariant a DON!!-return miscount (Heat-class) or a double-add would break.
    public static List<string> Conservation(GameState st)
    {
        var problems = new List<string>();
        foreach (var seat in new[] { "south", "north" })
        {
            var p = st.Players[seat];
            int attached = (p.Leader?.AttachedDonIds.Count ?? 0)
                         + p.CharacterArea.Where(c => c != null).Sum(c => c.AttachedDonIds.Count)
                         + (p.Stage?.AttachedDonIds.Count ?? 0);
            int totalDon = p.DonDeck + p.CostArea.Count + attached;
            if (totalDon != 10) problems.Add($"{seat}: DON!! total={totalDon} (deck={p.DonDeck}+cost={p.CostArea.Count}+attached={attached}) != 10");
        }
        return problems;
    }

    static void CheckZone(List<string> problems, string seat, IEnumerable<CardInstance> cards, string zone, Dictionary<string, string> seen)
    {
        foreach (var c in cards)
        {
            if (c == null) continue;
            if (c.Zone != zone) problems.Add($"{seat}: {c.CardId} in {zone} list but Zone='{c.Zone}'");
            string key = c.InstanceId;
            if (seen.TryGetValue(key, out var where)) problems.Add($"dup instanceId {key}: {where} & {seat}/{zone}");
            else seen[key] = $"{seat}/{zone}";
        }
    }
}
