using System;
using System.Linq;
using OnePieceTcg.Engine;

namespace OnePieceTcg.Sim
{
    /// <summary>
    /// Five cards word a DON!!-give recipient in the SINGULAR — "Give up to 1 rested DON!! card to 1 of
    /// your {Sky Island} type Leader or Character cards" (OP15-114 Wyper, OP14-114 Ran, OP16-094 Ace,
    /// P-096 Girl, ST21-009 Nami). Both the engine's glow rule and the client's DON-pick router
    /// discriminated on the PLURAL "Characters", so both read these as the no-choice "to your Leader"
    /// form: nothing lit up, and the DON!! went to the Leader with no way to give it to a Character the
    /// card explicitly allows. Surfaced by `leaderaudit all` once its fixture finally had rested DON!!.
    ///
    /// Run: dotnet run --project Tools/Sim/Sim.csproj -c Release -- donrecipient
    /// </summary>
    public static class DonRecipientTest
    {
        private static int passed;
        private static int failed;

        // The clause each of the five carries, in the singular wording that was being missed.
        private const string SingularClause =
            "Give up to 1 rested DON!! card to 1 of your {Sky Island} type Leader or Character cards.";
        // The plural wording that always worked — it must keep working.
        private const string PluralClause =
            "Give up to 1 rested DON!! card to your Leader or 1 of your Characters.";
        // The no-choice wording, which must STAY on the DON-pick flow and light no board card.
        private const string LeaderOnlyClause =
            "Give up to 1 rested DON!! card to your Leader.";

        public static int Run()
        {
            Console.WriteLine("=== DON!!-give: the recipient you are allowed to choose ===");
            RecipientLightsUp(SingularClause, "singular \"Leader or Character cards\"", expectGlow: true);
            RecipientLightsUp(PluralClause, "plural \"or 1 of your Characters\"", expectGlow: true);
            RecipientLightsUp(LeaderOnlyClause, "no-choice \"to your Leader\"", expectGlow: false);
            ClickingACharacterActuallyGivesItTheDon();
            NamedRecipientIsEnforced();
            Console.WriteLine($"donrecipient: {passed}/{passed + failed} passed ({failed} failed)");
            return failed == 0 ? 0 : 1;
        }

        private static void RecipientLightsUp(string clause, string label, bool expectGlow)
        {
            var b = new Board();
            // The clause's {Sky Island} filter applies to the LEADER as much as to Characters, so the
            // fixture needs a Leader of that type — with a Straw Hat Leader the engine was correctly
            // refusing to light it and the assertion, not the code, was wrong.
            b.SetLeader("south", "OP15-098");                 // Monkey.D.Luffy — {Sky Island}
            var src = b.Character("south", "OP15-114");
            var mate = b.Character("south", "EB01-054");      // Gan.Fall — {Sky Island}, a legal recipient
            var offType = b.Character("south", "ST29-010");   // Franky — NOT {Sky Island}
            b.Don("south", 4, rested: 2);
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", clause);
            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) { Check($"{label}: clause queues", false, "no pending effect"); return; }

            bool leaderGlows = GameEngine.IsValidEffectTarget(b.St, pe, b.S.Leader);
            bool charGlows = GameEngine.IsValidEffectTarget(b.St, pe, mate);
            bool oppGlows = GameEngine.IsValidEffectTarget(b.St, pe, b.N.Leader);
            bool offTypeGlows = GameEngine.IsValidEffectTarget(b.St, pe, offType);

            if (expectGlow)
                Check($"{label}: my Leader AND my Characters are offered as the recipient",
                    leaderGlows && charGlows, $"leader={leaderGlows} character={charGlows}");
            else
                Check($"{label}: stays on the DON-pick flow, no board card lights",
                    !leaderGlows && !charGlows, $"leader={leaderGlows} character={charGlows}");

            Check($"{label}: the opponent is never a recipient", !oppGlows);
            // A clause naming a type may only light cards of that type — the resolver refuses the rest,
            // so lighting them is a dead target dressed up as a legal one.
            if (clause.Contains("{"))
                Check($"{label}: a card outside the named type does NOT light", !offTypeGlows);
        }

        // Lighting up is only half of it — the click has to actually hand the DON!! over.
        private static void ClickingACharacterActuallyGivesItTheDon()
        {
            var b = new Board();
            var src = b.Character("south", "OP15-114");
            var mate = b.Character("south", "EB01-054");      // {Sky Island} — matches the clause filter
            b.Don("south", 4, rested: 2);
            int before = mate.AttachedDonIds.Count;
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", SingularClause);
            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) { Check("clicking a Character gives it the DON!!", false, "no pending effect"); return; }

            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = mate.InstanceId });

            var after = b.S.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == mate.InstanceId);
            Check("clicking a Character actually gives it the rested DON!!",
                after != null && after.AttachedDonIds.Count == before + 1,
                $"attached {before} -> {after?.AttachedDonIds.Count}");
        }

        // Six cards name the recipient rather than typing it — "to 1 of your [Nami] cards" (P-096 Girl),
        // "[Monkey.D.Luffy]" (OP13-006, OP13-021, ST29-012), "your [Roronoa Zoro] Leader" (OP12-026,
        // OP12-031). The glow enforces that name; the resolver did not, so any Character could take the
        // DON!!. Surfaced as the last UNCLICKABLE pair once the audit fixture finally had rested DON!!.
        private static void NamedRecipientIsEnforced()
        {
            const string Named = "Give up to 1 rested DON!! card to 1 of your [Nami] cards.";
            var b = new Board();
            var src = b.Character("south", "P-096");
            var nami = b.Character("south", "ST29-008");      // named [Nami] — the only legal recipient
            var other = b.Character("south", "ST29-010");     // Franky — must be refused
            b.Don("south", 4, rested: 2);
            GameEngine.QueueClauseForTest(b.St, "south", src, "main", Named);
            var pe = b.St.PendingEffects.FirstOrDefault();
            if (pe == null) { Check("named recipient: clause queues", false, "no pending effect"); return; }

            Check("named recipient: the [Nami] card lights up",
                GameEngine.IsValidEffectTarget(b.St, pe, nami));
            Check("named recipient: a card with the wrong name does NOT light",
                !GameEngine.IsValidEffectTarget(b.St, pe, other));

            // And the resolver has to agree — a name the glow refuses must not be accepted by a click.
            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe.EffectId, Target = other.InstanceId });
            var otherAfter = b.S.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == other.InstanceId);
            Check("named recipient: the resolver refuses the wrong name too",
                otherAfter != null && otherAfter.AttachedDonIds.Count == 0,
                $"Franky ended up with {otherAfter?.AttachedDonIds.Count} DON!!");

            var pe2 = b.St.PendingEffects.FirstOrDefault();
            if (pe2 == null) { Check("named recipient: still resolvable after a bad click", false, "effect gone"); return; }
            b.Apply(new GameCommand
            { Type = "resolveEffect", Seat = "south", EffectId = pe2.EffectId, Target = nami.InstanceId });
            var namiAfter = b.S.CharacterArea.FirstOrDefault(c => c != null && c.InstanceId == nami.InstanceId);
            Check("named recipient: the [Nami] card does receive the DON!!",
                namiAfter != null && namiAfter.AttachedDonIds.Count == 1,
                $"Nami has {namiAfter?.AttachedDonIds.Count} DON!!");
        }

        // ---- plumbing ---------------------------------------------------------------------------

        private static void Check(string name, bool ok, string detail = null)
        {
            if (ok) { passed++; Console.WriteLine("  PASS  " + name); }
            else { failed++; Console.WriteLine("  FAIL  " + name + (string.IsNullOrEmpty(detail) ? "" : " — " + detail)); }
        }

        private sealed class Board
        {
            public GameState St;
            public PlayerState S => St.Players["south"];
            public PlayerState N => St.Players["north"];
            private int southSlot, northSlot, serial;

            public Board()
            {
                St = GameEngine.CreateMatch(new MatchConfig
                { SouthDeck = "st01", NorthDeck = "st01", Seed = "don-recipient" });
                St.Status = "active"; St.Phase = "main"; St.ActiveSeat = "south"; St.TurnNumber = 8;
                S.TurnsStarted = 4; N.TurnsStarted = 4;
                for (int i = 0; i < 5; i++) { S.CharacterArea[i] = null; N.CharacterArea[i] = null; }
                S.Hand.Clear(); N.Hand.Clear();
                S.Life.Clear(); N.Life.Clear();
                S.CostArea.Clear(); N.CostArea.Clear();
                S.DonDeck = 10; N.DonDeck = 10;
                S.Leader.Rested = false; N.Leader.Rested = false;
                S.Leader.PlayedOnTurn = 0; N.Leader.PlayedOnTurn = 0;
                St.PendingEffects.Clear();
                for (int i = 0; i < 3; i++) { S.Life.Add(Card("ST01-005", "south", "life")); N.Life.Add(Card("ST01-005", "north", "life")); }
            }

            public void SetLeader(string seat, string id)
            {
                var l = seat == "south" ? S.Leader : N.Leader;
                l.CardId = id; l.Rested = false; l.PlayedOnTurn = 0; l.AttachedDonIds.Clear();
            }

            public CardInstance Character(string seat, string id)
            {
                var p = seat == "south" ? S : N;
                var c = Card(id, seat, "character");
                p.CharacterArea[seat == "south" ? southSlot++ : northSlot++] = c;
                return c;
            }

            public void Don(string seat, int count, int rested)
            {
                var p = seat == "south" ? S : N;
                for (int i = 0; i < count; i++)
                    p.CostArea.Add(new DonInstance { InstanceId = $"{seat}-dr-don-{serial++}", Rested = i < rested });
                p.DonDeck = Math.Max(0, p.DonDeck - count);
            }

            public void Apply(GameCommand c) => St = GameEngine.ApplyCommand(St, c);

            private CardInstance Card(string id, string owner, string zone) => new CardInstance
            {
                InstanceId = $"{owner}-{id}-dr-{serial++}",
                CardId = id, Owner = owner, Zone = zone, Rested = false, PlayedOnTurn = 0,
            };
        }
    }
}
