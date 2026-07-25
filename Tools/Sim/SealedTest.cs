using System;
using System.Collections.Generic;
using System.Linq;
using OnePieceTcg.Engine;
using OnePieceTcg.Sealed;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Statistical + behavioural gate for Sealed / Pre-Release mode.
    ///
    /// The point is to verify the pack generator BEFORE any UI exists: open thousands of boxes and
    /// check the observed pull rates against the published per-box figures (SR ~8/box, SEC ~1/box,
    /// Leader ~12/box, parallels ~2/box), confirm a shared seed is byte-identical, and confirm the
    /// A.I. builds a legal, sensibly-shaped sealed deck.
    ///
    /// Run: dotnet run --project Sim.csproj -c Release -- sealedtest
    /// </summary>
    public static class SealedTest
    {
        private static int passed, failed;

        public static int Run(string[] args)
        {
            Console.WriteLine("=== Sealed / Pre-Release ===");
            CatalogLooksRight();
            SetPoolMatchesPublishedComposition();
            PackShapeIsCorrect();
            PullRatesMatchPublishedFigures();
            EveryKitCanBuildALegalDeck();
            SeedsAreDeterministicAndShareable();
            PoolAndDeckRulesHold();
            AiBuildsALegalSealedDeck();
            EventRunsAFullSwiss();
            LeaderFormatVariants();
            Console.WriteLine($"sealedtest: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void CatalogLooksRight()
        {
            var products = SealedCatalog.Available();
            Check($"catalog lists booster sets ({products.Count} found)", products.Count >= 15);
            Check("newest set is first (OP16)", products.FirstOrDefault()?.SetCode == "OP16");
            Check("no starter/promo products leaked in",
                products.All(p => p.SetCode.StartsWith("OP") || p.SetCode.StartsWith("EB")));
        }

        // The published composition of a modern booster set. If ID-prefix filtering is right, OP16's
        // pool matches this exactly — which is also proof the library is complete and rarity-tagged.
        private static void SetPoolMatchesPublishedComposition()
        {
            var op16 = SealedCatalog.Find("OP16");
            if (op16 == null) { Check("OP16 present", false); return; }
            var byRarity = op16.PoolByRarity();
            int N(string r) => byRarity.TryGetValue(r, out var l) ? l.Count : 0;

            var expected = new (string rarity, int count)[]
            {
                (Rarity.Leader, 6), (Rarity.Common, 45), (Rarity.Uncommon, 30), (Rarity.Rare, 26),
                (Rarity.SuperRare, 10), (Rarity.SecretRare, 2),
                // NOTE: no SP entry. A set's "SP CARD x6" are alt-art prints of cards from EARLIER
                // sets, so they carry foreign card IDs (OP16's page lists an EB04, OP10, OP11, two
                // OP14s and an ST15 - exactly 6). Prefix filtering correctly leaves them out, and
                // CardData carries no seriesCode to attribute them by. At ~1 per CASE they are
                // irrelevant to a 6-pack kit.
            };
            bool all = expected.All(e => N(e.rarity) == e.count);
            Check("OP16 pool matches the published set composition "
                + $"(L{N(Rarity.Leader)} C{N(Rarity.Common)} UC{N(Rarity.Uncommon)} R{N(Rarity.Rare)} "
                + $"SR{N(Rarity.SuperRare)} SEC{N(Rarity.SecretRare)} SP{N(Rarity.Special)})", all);

            // The reprints listed on a set's official page must NOT be in the pack pool.
            Check("no foreign-set reprints in the pool",
                op16.CardPool().All(id => id.StartsWith("OP16-", StringComparison.OrdinalIgnoreCase)));
        }

        private static void PackShapeIsCorrect()
        {
            var product = SealedCatalog.Find("OP16");
            var packs = PackGenerator.Open(product, "shape-test");
            Check($"a prerelease kit opens {product.PackCount} packs", packs.Count == product.PackCount);
            Check("every pack has 12 cards", packs.All(p => p.Cards.Count == 12));
            Check("every pulled card is from the set",
                packs.SelectMany(p => p.Cards).All(c => c.CardId.StartsWith("OP16-", StringComparison.OrdinalIgnoreCase)));
            Check("every pack guarantees at least one Rare-or-better",
                packs.All(p => p.Cards.Any(c => c.RarityCode != Rarity.Common && c.RarityCode != Rarity.Uncommon)));
            Check("pack history is retained per pack with pull order",
                packs.All(p => p.Cards.Select(c => c.SlotIndex).SequenceEqual(Enumerable.Range(0, p.Cards.Count))));
            Check("every pack has a headline card to build the reveal to",
                packs.All(p => p.Headline() != null));
        }

        // The real check: open many boxes and compare observed rates to the published per-box figures.
        private static void PullRatesMatchPublishedFigures()
        {
            var product = SealedCatalog.Find("OP16");
            const int boxes = 2000;
            int packsPerBox = product.Collation.PacksPerBox;
            var counts = new Dictionary<string, int>();
            int parallels = 0, totalCards = 0;

            for (int b = 0; b < boxes; b++)
                foreach (var pack in PackGenerator.OpenCount(product, "rates-" + b, packsPerBox))
                    foreach (var c in pack.Cards)
                    {
                        counts[c.RarityCode] = counts.TryGetValue(c.RarityCode, out var n) ? n + 1 : 1;
                        if (c.IsParallel) parallels++;
                        totalCards++;
                    }

            double Per(string r) => counts.TryGetValue(r, out var n) ? (double)n / boxes : 0;
            double sr = Per(Rarity.SuperRare), sec = Per(Rarity.SecretRare), ld = Per(Rarity.Leader);
            double par = (double)parallels / boxes;

            Console.WriteLine($"    over {boxes} boxes ({totalCards:N0} cards): "
                + $"SR {sr:F2}/box  SEC {sec:F2}/box  Leader {ld:F2}/box  parallel {par:F2}/box");

            Check($"Super Rare ~8 per box (got {sr:F2})", Math.Abs(sr - 8.0) < 0.6);
            Check($"Secret Rare ~1 per box (got {sec:F2})", Math.Abs(sec - 1.0) < 0.25);
            Check($"Leader ~12 per box (got {ld:F2})", Math.Abs(ld - 12.0) < 0.8);
            Check($"parallels ~2 per box (got {par:F2})", Math.Abs(par - 2.0) < 0.5);
            Check($"box holds exactly {packsPerBox * 12} cards", totalCards == boxes * packsPerBox * 12);
        }

        private static void SeedsAreDeterministicAndShareable()
        {
            var product = SealedCatalog.Find("OP16");
            string Fingerprint(IEnumerable<SealedPack> packs) =>
                string.Join(",", packs.SelectMany(p => p.Cards).Select(c => c.CardId + (c.IsParallel ? "*" : "")));

            var a = PackGenerator.Open(product, "842918361");
            var b = PackGenerator.Open(product, "842918361");
            var c = PackGenerator.Open(product, "842918362");
            Check("same seed → identical packs", Fingerprint(a) == Fingerprint(b));
            Check("different seed → different packs", Fingerprint(a) != Fingerprint(c));

            // Pack N must not depend on how many packs were opened, so a shared seed survives a
            // product whose pack count changes.
            var six = PackGenerator.OpenCount(product, "stable", 6);
            var twelve = PackGenerator.OpenCount(product, "stable", 12);
            Check("pack N is stable regardless of how many packs are opened",
                Fingerprint(six) == Fingerprint(twelve.Take(6)));

            Check("generated seeds are 9 digits", PackGenerator.NewSeed().Length == 9
                && PackGenerator.NewSeed().All(char.IsDigit));
        }

        // 1.56% of 6-pack kits would open zero Leaders ((1-12/24)^6), which makes a legal deck
        // impossible. The product guarantees one; this proves it holds across many seeds.
        private static void EveryKitCanBuildALegalDeck()
        {
            var product = SealedCatalog.Find("OP16");
            int leaderless = 0;
            const int kits = 500;
            for (int i = 0; i < kits; i++)
            {
                var pool = SealedPool.Generate(product, "leader-guarantee-" + i);
                if (pool.AvailableLeaders().Count == 0) leaderless++;
            }
            Check($"every kit opens at least one Leader ({leaderless} of {kits} without)", leaderless == 0);
        }

        private static void PoolAndDeckRulesHold()
        {
            var product = SealedCatalog.Find("OP16");
            var pool = SealedPool.Generate(product, "pool-test");

            Check("pool aggregates every opened card",
                pool.PoolCounts().Values.Sum() == pool.Packs.Sum(p => p.Cards.Count));

            // Cannot add a card you did not open.
            var notOwned = product.CardPool().FirstOrDefault(id => !pool.PoolCounts().ContainsKey(id));
            Check("cannot add a card that is not in the pool", notOwned == null || pool.Add(notOwned) == 0);

            // Cannot exceed the copies you opened.
            var owned = pool.PoolCounts().FirstOrDefault(kv => CardData.GetCard(kv.Key)?.Type != "leader");
            if (owned.Key != null)
            {
                int added = pool.Add(owned.Key, 99);
                // Sealed has NO 4-copy limit: the only bound is how many you opened.
                Check($"copies are bounded by the pool alone ({added} of {owned.Value} opened)",
                    added == owned.Value);
                pool.RemoveAll(owned.Key);
            }

            var v = pool.Validate();
            Check("an empty deck is invalid and says why", !v.Ok && v.Problems.Count > 0);
            Check("sealed validates at 40 cards, not 50", v.RequiredCount == 40);
        }

        private static void AiBuildsALegalSealedDeck()
        {
            var product = SealedCatalog.Find("OP16");
            int legal = 0, curveOk = 0, counterOk = 0;
            const int trials = 40;

            for (int i = 0; i < trials; i++)
            {
                var pool = SealedDeckAI.BuildOpponent(product, "ai-build", i);
                var v = pool.Validate();
                if (v.Ok) legal++;
                var s = pool.DeckStats();
                if (s.AverageCost > 2.0 && s.AverageCost < 4.6) curveOk++;
                if (s.Counter1000 + s.Counter2000 >= 8) counterOk++;
            }
            Check($"A.I. builds a legal 40-card sealed deck ({legal}/{trials})", legal == trials);
            Check($"A.I. decks keep a sane curve ({curveOk}/{trials})", curveOk >= trials - 2);
            Check($"A.I. decks keep real Counter density ({counterOk}/{trials})", counterOk >= trials - 4);
        }

        private static void EventRunsAFullSwiss()
        {
            var product = SealedCatalog.Find("OP16");
            var human = SealedPool.Generate(product, "event-seed");
            var ev = SealedEvent.Create(product, "event-seed", human, playerCount: 8);

            Check("8-player event runs 3 Swiss rounds", ev.TotalRounds == 3);
            Check("field is filled with A.I. entrants", ev.Entrants.Count == 8);
            Check("every A.I. entrant arrives with a built deck",
                ev.Entrants.Where(e => !e.IsHuman).All(e => e.Pool.Validate().Ok));

            ev.StartPlay();
            int guard = 0;
            while (ev.Phase != EventPhase.Finished && guard++ < 10)
            {
                var mine = ev.CurrentPairingFor("player");
                if (mine != null && !mine.Reported) ev.Report(mine, "player");   // human wins out
                ev.ResolveAiPairings();
                if (ev.Phase == EventPhase.RoundComplete) ev.NextRound();
            }

            Check("event reaches a finish", ev.Phase == EventPhase.Finished);
            Check("every pairing was reported", ev.Pairings.All(p => p.Reported));
            var standings = ev.Standings();
            Check("standings rank the whole field", standings.Count == 8 && standings[0].Rank == 1);
            Check("an undefeated human tops the standings", standings[0].Entrant.IsHuman);
            Check("no player is paired against themselves", ev.Pairings.All(p => p.IsBye || p.AId != p.BId));
            int humanRounds = ev.Pairings.Count(p => p.AId == "player" || p.BId == "player");
            Check($"the human plays every round ({humanRounds}/3)", humanRounds == 3);
        }

        // Both real variants of the format: Rainbow Luffy (everyone plays the prerelease wildcard
        // Leader) and Free Select (any Leader in the game, banned Leaders excluded).
        private static void LeaderFormatVariants()
        {
            var product = SealedCatalog.Find("OP16");

            // --- Rainbow Luffy -----------------------------------------------------------------
            product.LeaderMode = SealedLeaderMode.RainbowLuffy;
            SealedLeaderRules.EnsureRegistered();
            var rainbow = CardData.GetCard(SealedLeaderRules.RainbowLuffyId);
            Check("Rainbow Luffy is registered as a Leader", rainbow != null && rainbow.Type == "leader");
            Check("Rainbow Luffy is every colour",
                SealedPool.SplitColors(rainbow?.Color).Count() == 6);
            Check("Rainbow Luffy counts as every type", rainbow.HasFeature("Land of Wano")
                && rainbow.HasFeature("Straw Hat Crew") && rainbow.HasFeature("Navy"));

            var st = GameEngine.CreateMatch(new MatchConfig { SouthDeck = "st01", NorthDeck = "st02", Seed = "rainbow" });
            var leaderInst = new CardInstance { InstanceId = "rl", CardId = SealedLeaderRules.RainbowLuffyId, Owner = "south", Zone = "leader" };
            Check("Rainbow Luffy counts as every card name",
                GameEngine.NameMatches(st, leaderInst, "Monkey.D.Luffy")
                && GameEngine.NameMatches(st, leaderInst, "Roronoa Zoro")
                && GameEngine.NameMatches(st, leaderInst, "Trafalgar Law"));

            var rainbowLegal = SealedLeaderRules.LegalLeaders(SealedLeaderMode.RainbowLuffy, null);
            Check("Rainbow Luffy is the only legal Leader in that variant",
                rainbowLegal.Count == 1 && rainbowLegal[0] == SealedLeaderRules.RainbowLuffyId);

            var rPool = SealedPool.Generate(product, "rainbow-pool");
            SealedDeckAI.Build(rPool);
            Check("A.I. plays Rainbow Luffy in that variant", rPool.LeaderId == SealedLeaderRules.RainbowLuffyId);
            Check("A.I. still builds a legal deck under Rainbow Luffy", rPool.Validate().Ok);

            // A Leader you opened is NOT legal in the Rainbow variant.
            var opened = rPool.AvailableLeaders().FirstOrDefault();
            if (opened != null)
            {
                rPool.LeaderId = opened;
                Check("an opened Leader is rejected in the Rainbow variant", !rPool.Validate().LeaderLegal);
                rPool.LeaderId = SealedLeaderRules.RainbowLuffyId;
            }

            // --- Free Select -------------------------------------------------------------------
            rPool.LeaderMode = SealedLeaderMode.FreeSelect;
            var free = SealedLeaderRules.LegalLeaders(SealedLeaderMode.FreeSelect, rPool);
            Check($"Free Select offers every Leader in the game ({free.Count})", free.Count > 100);
            Check("Free Select excludes banned Leaders",
                free.All(id => !SealedLeaderRules.IsBannedLeader(id)));

            // A Leader from a completely different set is legal here but not in pool-only.
            var foreign = free.FirstOrDefault(id => !id.StartsWith("OP16-", StringComparison.OrdinalIgnoreCase));
            if (foreign != null)
            {
                rPool.LeaderId = foreign;
                Check("Free Select accepts a Leader you never opened", rPool.Validate().LeaderLegal);
                rPool.LeaderMode = SealedLeaderMode.PoolOnly;
                Check("pool-only rejects that same Leader", !rPool.Validate().LeaderLegal);
            }

            // A banned Leader must be rejected even in Free Select.
            rPool.LeaderMode = SealedLeaderMode.FreeSelect;
            var banned = CardData.Library.Keys.FirstOrDefault(id =>
                CardData.GetCard(id)?.Type == "leader" && SealedLeaderRules.IsBannedLeader(id));
            if (banned != null)
            {
                rPool.LeaderId = banned;
                Check($"Free Select rejects the banned Leader {banned}", !rPool.Validate().LeaderLegal);
            }
            else Console.WriteLine("    (no banned Leaders on the current ban list — skipped)");

            rPool.LeaderMode = SealedLeaderMode.RainbowLuffy;
        }

        private static void Check(string name, bool ok)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name); }
        }
    }
}
